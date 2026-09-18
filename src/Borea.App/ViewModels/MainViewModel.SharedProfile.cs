using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Borea.Core.Instances;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The offer to create an instance from the mods of the shared profile.
/// </summary>
public partial class MainViewModel
{
    private bool? _sharedProfileBannerDismissed;
    private CancellationTokenSource? _sharedProfileImport;
    private SharedProfileImportResult? _sharedProfileImportResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSharedProfileMods))]
    [NotifyPropertyChangedFor(nameof(ShowSharedProfileBanner))]
    [NotifyPropertyChangedFor(nameof(SharedProfileBannerText))]
    private int _sharedProfileModCount;

    public bool HasSharedProfileMods => SharedProfileModCount > 0;

    public bool ShowSharedProfileBanner => HasSharedProfileMods
        && GameSetupState == GameSetupState.Ready
        && !(_sharedProfileBannerDismissed ?? _appPreferences.SharedProfileBannerDismissed);

    public string SharedProfileBannerText => Localization.FormatSharedProfileModCount(SharedProfileModCount);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameModalTitle))]
    private bool _isImportingSharedProfile;

    [ObservableProperty]
    private bool _isSharedProfileImportRunning;

    /// <summary>What the instance created from the shared profile does not take over, while its page is open.</summary>
    public string? SharedProfileImportNotice
    {
        get
        {
            if (_sharedProfileImportResult is not { } result || SelectedInstance?.InstanceId != result.Instance.InstanceId)
                return null;

            var lines = new List<string>();
            var disabled = result.Mods.Where(mod => mod.Enabled && !mod.HasManifestEntry).Select(mod => mod.FolderName).ToList();
            if (disabled.Count > 0)
                lines.Add(Localization.FormatSharedProfileImportDisabled(disabled));

            var notChecked = result.Mods.Where(mod => mod.MatchError is not null).Select(mod => mod.FolderName).ToList();
            if (notChecked.Count > 0)
                lines.Add(Localization.FormatSharedProfileImportNotChecked(notChecked));

            return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
        }
    }

    private async Task RefreshSharedProfileAsync()
    {
        var count = 0;
        if (_services is not null)
        {
            try
            {
                count = (await _services.SharedProfileImporter.GetModsAsync()).Count;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
            }
        }

        SharedProfileModCount = count;
        OnPropertyChanged(nameof(ShowSharedProfileBanner));
    }

    [RelayCommand]
    private void BeginImportSharedProfile()
    {
        ModalInstanceName = Localization.SharedProfileInstanceName;
        InstanceError = null;
        RenamingInstance = null;
        IsImportingSharedProfile = true;
        IsCreatingInstance = true;
    }

    [RelayCommand]
    private void DismissSharedProfileBanner()
    {
        _sharedProfileBannerDismissed = true;
        OnPropertyChanged(nameof(ShowSharedProfileBanner));
        QueuePreferenceSave(preferences => preferences.WithSharedProfileBannerDismissed(true));
    }

    partial void OnIsCreatingInstanceChanged(bool value)
    {
        if (value)
            return;

        IsImportingSharedProfile = false;
        IsSharedProfileImportRunning = false;
        _sharedProfileImport?.Cancel();
    }

    partial void OnSelectedInstanceChanged(InstanceItem? value)
    {
        if (_sharedProfileImportResult is { } result && value?.InstanceId != result.Instance.InstanceId)
            _sharedProfileImportResult = null;

        OnPropertyChanged(nameof(SharedProfileImportNotice));
    }

    private async Task ImportSharedProfileAsync(string name)
    {
        if (_services is null)
            return;

        var services = _services;
        using var cancellation = new CancellationTokenSource();
        _sharedProfileImport = cancellation;
        SharedProfileImportResult? result = null;
        IsSharedProfileImportRunning = true;
        try
        {
            await RunInstanceOperationAsync(async _ =>
            {
                try
                {
                    result = await services.SharedProfileImporter.ImportAsync(name, cancellation.Token);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
            });
        }
        finally
        {
            _sharedProfileImport = null;
            IsSharedProfileImportRunning = false;
        }

        if (result is not { } imported)
            return;

        DismissSharedProfileBanner();

        // the modal was closed just as the import finished, so the instance stays and the page is left as the user has it
        if (cancellation.IsCancellationRequested)
            return;

        IsCreatingInstance = false;
        ModalInstanceName = string.Empty;
        _sharedProfileImportResult = imported;
        if (Instances.FirstOrDefault(item => item.InstanceId == imported.Instance.InstanceId) is { } created)
            await OpenInstanceAsync(created);
    }
}
