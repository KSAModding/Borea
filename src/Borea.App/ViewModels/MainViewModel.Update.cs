using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Core.Mods;
using Borea.Core.Updates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>The notice that a newer Borea release exists, checked once per start, and its release notes.</summary>
public partial class MainViewModel
{
    private Task? _updateCheck;

    private bool? _checkForUpdatesAtStart;

    private BoreaUpdateChannel? _updateChannel;

    private IReadOnlyList<BoreaUpdateChannelOption>? _updateChannelOptions;

    private IReadOnlyList<BoreaRelease> _releases = [];

    private ModVersion? _dismissedBoreaRelease;

    /// <summary>The newer release, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvailableUpdate))]
    [NotifyPropertyChangedFor(nameof(AvailableUpdateVersion))]
    [NotifyPropertyChangedFor(nameof(AvailableUpdateText))]
    [NotifyPropertyChangedFor(nameof(AvailableUpdateUrl))]
    [NotifyPropertyChangedFor(nameof(ShowReleaseBanner))]
    private BoreaRelease? _availableUpdate;

    public bool HasAvailableUpdate => AvailableUpdate is not null;

    public string? AvailableUpdateVersion => AvailableUpdate?.Version.ToString();

    public string? AvailableUpdateText => AvailableUpdate is null ? null : $"{Localization.UpdateAvailable} {AvailableUpdateVersion}";

    public string? AvailableUpdateUrl => AvailableUpdate?.PageUrl;

    /// <summary>The Home banner shows until the player closes it for this release or a newer one.</summary>
    public bool ShowReleaseBanner => AvailableUpdate is { } release
        && !((_dismissedBoreaRelease ?? _appPreferences.DismissedBoreaRelease) >= release.Version);

    [ObservableProperty]
    private bool _isReleaseNotesOpen;

    /// <summary>The newer releases, newest first, and the installed one last.</summary>
    [ObservableProperty]
    private IReadOnlyList<BoreaReleaseNotesItem> _releaseNotes = [];

    /// <summary>The switch in the General settings. A change takes effect at the next start.</summary>
    public bool CheckForUpdatesAtStart
    {
        get => _checkForUpdatesAtStart ?? _appPreferences.CheckForUpdatesAtStart;
        set
        {
            if (value == CheckForUpdatesAtStart)
                return;

            _checkForUpdatesAtStart = value;
            OnPropertyChanged();
            QueuePreferenceSave(preferences => preferences.WithCheckForUpdatesAtStart(value));
        }
    }

    public IReadOnlyList<BoreaUpdateChannelOption> UpdateChannelOptions
        => _updateChannelOptions ??= Enum.GetValues<BoreaUpdateChannel>().Select(channel => new BoreaUpdateChannelOption(Localization, channel)).ToArray();

    /// <summary>The update channel in the General settings. A change takes effect at the next start.</summary>
    public BoreaUpdateChannelOption SelectedUpdateChannel
    {
        get => UpdateChannelOptions.First(option => option.Channel == (_updateChannel ?? _appPreferences.UpdateChannel));
        set
        {
            if (value is null || value == SelectedUpdateChannel)
                return;

            _updateChannel = value.Channel;
            OnPropertyChanged();
            QueuePreferenceSave(preferences => preferences.WithUpdateChannel(value.Channel));
        }
    }

    /// <summary>Starts the release check once, in the background.</summary>
    private void StartUpdateCheck() => _updateCheck ??= CheckForUpdateAsync();

    /// <summary>Completes when the release check has finished.</summary>
    internal Task WhenUpdateCheckedAsync() => _updateCheck ?? Task.CompletedTask;

    private async Task CheckForUpdateAsync()
    {
        if (_services is null || !_appPreferences.CheckForUpdatesAtStart)
            return;

        try
        {
            _releases = await _services.ReleaseCheck.GetReleasesAsync(_appPreferences.UpdateChannel);
            if (_releases.FirstOrDefault() is { } newest && newest.IsNewerThan(BoreaInformationalVersion))
                AvailableUpdate = newest;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or ObjectDisposedException)
        {
            // a failed check shows nothing
        }
    }

    [RelayCommand]
    private void OpenReleaseNotes()
    {
        var notes = _releases
            .Where(release => release.IsNewerThan(BoreaInformationalVersion))
            .Select(release => new BoreaReleaseNotesItem(release.Version, release, isInstalled: false))
            .ToList();
        if (ModVersion.TryParse(BoreaInformationalVersion, out var running))
        {
            var installed = _releases.FirstOrDefault(release => release.Version.CompareTo(running) == 0);
            notes.Add(new BoreaReleaseNotesItem(running, installed, isInstalled: true));
        }

        ReleaseNotes = notes;
        IsReleaseNotesOpen = true;
    }

    [RelayCommand]
    private void CloseReleaseNotes() => IsReleaseNotesOpen = false;

    [RelayCommand]
    private void DismissReleaseBanner()
    {
        if (AvailableUpdate is not { } release)
            return;

        _dismissedBoreaRelease = release.Version;
        OnPropertyChanged(nameof(ShowReleaseBanner));
        QueuePreferenceSave(preferences => preferences.WithDismissedBoreaRelease(release.Version));
    }
}

/// <summary>One Borea update channel, as the General settings name it.</summary>
public sealed class BoreaUpdateChannelOption : ObservableObject
{
    private readonly LocalizationService _localization;

    public BoreaUpdateChannel Channel { get; }

    public string Text => Channel switch
    {
        BoreaUpdateChannel.Testing => _localization.UpdateChannelTesting,
        BoreaUpdateChannel.Dev => _localization.UpdateChannelDev,
        _ => _localization.UpdateChannelStable,
    };

    public BoreaUpdateChannelOption(LocalizationService localization, BoreaUpdateChannel channel)
    {
        _localization = localization;
        Channel = channel;
    }

    internal void RefreshText() => OnPropertyChanged(nameof(Text));
}

/// <summary>One Borea release in the release notes. The installed version shows even when the check did not return it.</summary>
public sealed class BoreaReleaseNotesItem : ObservableObject
{
    private readonly BoreaRelease? _release;

    public string Version { get; }

    public bool IsInstalled { get; }

    public bool IsPreRelease { get; }

    public string? Notes => _release?.Notes;

    public bool HasNoNotes => _release is not null && _release.Notes is null;

    public string? PageUrl => IsInstalled ? null : _release?.PageUrl;

    public string? DateText => _release?.PublishedAt is { } at ? MainViewModel.DateText(at) : null;

    public BoreaReleaseNotesItem(ModVersion version, BoreaRelease? release, bool isInstalled)
    {
        _release = release;
        Version = version.ToString();
        IsInstalled = isInstalled;
        IsPreRelease = version.PreRelease is not null;
    }

    internal void RefreshText() => OnPropertyChanged(nameof(DateText));
}
