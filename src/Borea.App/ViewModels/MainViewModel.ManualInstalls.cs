using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.History;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The Manual installs tab of the instance page.
/// </summary>
public partial class MainViewModel
{
    private bool? _foreignFolderDeletionConfirmed;

    public ObservableCollection<ManualInstallItem> ManualInstallItems { get; } = [];

    public bool HasManualInstalls => ManualInstallItems.Count > 0;

    [ObservableProperty]
    private string? _manualInstallsError;

    [RelayCommand]
    private async Task ShowInstanceManualInstallsAsync()
    {
        InstanceTab = InstanceTab.ManualInstalls;
        await LoadManualInstallsAsync();
    }

    /// <summary>Opens the mods folder of the instance on the page, where a mod installed by hand goes.</summary>
    [RelayCommand]
    private void OpenInstanceModsFolder()
    {
        if (_services is not { } services || SelectedInstance is not { } instance)
            return;

        var folder = services.Paths.GetInstanceModsFolder(instance.InstanceId);
        OpenCreatingFolder(() => folder, () => PathName(folder));
    }

    private async Task LoadManualInstallsAsync()
    {
        ManualInstallsError = null;
        ManualInstallItems.Clear();
        OnPropertyChanged(nameof(HasManualInstalls));
        if (_services is null || SelectedInstance is null)
            return;

        var services = _services;
        var instanceId = SelectedInstance.InstanceId;
        var items = new List<ManualInstallItem>();
        string? error = null;
        try
        {
            foreach (var foreign in await services.ForeignModAdopter.ScanAsync(instanceId))
            {
                bool? inIndex = null;
                if (error is null)
                {
                    try
                    {
                        inIndex = (await services.ContentIndex.GetListingAsync(foreign.FolderName))?.Type == ContentType.Mod;
                    }
                    catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or FormatException or TaskCanceledException)
                    {
                        error = exception.Message;
                    }
                }

                items.Add(new ManualInstallItem(this, instanceId, foreign.FolderName, inIndex));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error = exception.Message;
        }

        if (SelectedInstance?.InstanceId != instanceId)
            return;

        ManualInstallItems.Clear();
        foreach (var item in items)
            ManualInstallItems.Add(item);
        OnPropertyChanged(nameof(HasManualInstalls));
        ManualInstallsError = error;
    }

    internal async Task ManageManualInstallAsync(ManualInstallItem row)
    {
        if (_services is null || row.IsBusy)
            return;

        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            ShowErrorToast(() => Localization.FormatToastCheckFailed(row.FolderName), Localization.LibraryFolderBusy);
            return;
        }

        var services = _services;
        row.IsChecking = true;
        var adopted = false;
        try
        {
            var result = await services.ForeignModReleaseMatcher.AdoptMatchingReleaseAsync(row.InstanceId, row.FolderName);
            adopted = result?.Matched == true;
            row.Check = result switch
            {
                null => ManualInstallCheck.NoMatch,
                { Matched: false } => ManualInstallCheck.NotRecorded,
                _ => ManualInstallCheck.NotChecked,
            };
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            ShowErrorToast(() => Localization.FormatToastCheckFailed(row.FolderName), exception.Message);
        }
        finally
        {
            row.IsChecking = false;
        }

        if (adopted)
            await ReloadInstancesAsync();
    }

    internal async Task BeginReplaceManualInstallAsync(ManualInstallItem row)
    {
        if (row.IsBusy)
            return;

        if (!(_foreignFolderDeletionConfirmed ?? _appPreferences.ForeignFolderDeletionConfirmed))
        {
            row.IsConfirmingReplace = true;
            return;
        }

        await PlanManualReplaceAsync(row);
    }

    internal async Task ConfirmReplaceManualInstallAsync(ManualInstallItem row)
    {
        row.IsConfirmingReplace = false;
        _foreignFolderDeletionConfirmed = true;
        QueuePreferenceSave(preferences => preferences.WithForeignFolderDeletionConfirmed(true));
        await PlanManualReplaceAsync(row);
    }

    /// <summary>
    /// Plans against the instance without the folder, so a conflict stops the replacement before the folder is touched.
    /// </summary>
    private async Task PlanManualReplaceAsync(ManualInstallItem row)
    {
        if (_services is null || row.IsBusy)
            return;

        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            ShowErrorToast(() => Localization.FormatToastReplaceFailed(row.FolderName), Localization.LibraryFolderBusy);
            return;
        }

        var services = _services;
        row.InstallError = null;
        row.InstallWarning = null;
        row.PendingPlan = null;
        row.IsInstalling = true;
        InstallPlan? plan = null;
        try
        {
            var release = await services.Mods.GetLatestReleaseAsync(row.FolderName)
                ?? throw new InvalidOperationException(Localization.DiscoverNoRelease);
            var instance = await services.Instances.GetByIdAsync(row.InstanceId)
                ?? throw new InvalidOperationException(Localization.InstallInstanceMissing);
            var request = new InstallPlanningRequest(
                WithoutForeignFolder(instance, row.FolderName),
                [new RequestedMod(release, InstallReason.Manual, false)],
                services.Mods,
                services.InstalledVersion.GetInstalledVersion()?.Version,
                CurrentPlatform());
            var planned = await services.InstallPlanner.PlanAsync(request);

            if (!planned.IsReady)
            {
                row.InstallError = Describe(planned.Conflicts.Concat(planned.UnresolvedChoices));
            }
            else if (planned.Warnings.Count > 0)
            {
                row.PendingPlan = planned;
                row.InstallWarning = Describe(planned.Warnings);
            }
            else
            {
                plan = planned;
            }
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            row.InstallError = exception.Message;
        }
        finally
        {
            if (plan is null)
                row.IsInstalling = false;
        }

        if (plan is not null)
            await ExecuteManualReplaceAsync(services, row, plan);
    }

    internal async Task ConfirmManualReplaceInstallAsync(ManualInstallItem row)
    {
        if (_services is null || row.PendingPlan is not { } plan || row.IsBusy)
            return;

        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            ShowErrorToast(() => Localization.FormatToastReplaceFailed(row.FolderName), Localization.LibraryFolderBusy);
            return;
        }

        row.PendingPlan = null;
        row.InstallWarning = null;
        row.IsInstalling = true;
        await ExecuteManualReplaceAsync(_services, row, plan);
    }

    /// <summary>
    /// The rows are read again after the change, so the toast of the task says why it failed.
    /// </summary>
    private async Task ExecuteManualReplaceAsync(BoreaServices services, ManualInstallItem row, InstallPlan plan)
    {
        string? error = null;
        var run = row.Run = StartInstallRun(StartTask(TaskKind.ManualReplace, row.FolderName, row.InstanceId));
        var completed = false;
        var stopped = false;
        try
        {
            var instance = await services.Instances.GetByIdAsync(row.InstanceId)
                ?? throw new InvalidOperationException(Localization.InstallInstanceMissing);
            if (!plan.InstanceState.Matches(WithoutForeignFolder(instance, row.FolderName)))
                throw new InvalidOperationException(Localization.ManualInstallsInstanceChanged);

            await services.ForeignModAdopter.ReplaceFolderAsync(
                row.InstanceId,
                row.FolderName,
                cancellationToken => services.PlanExecutor.ExecuteAsync(plan, enable: true, ProgressOf(row), run.InstallStop, cancellationToken));
            completed = true;
        }
        catch (InstallStoppedException)
        {
            // only a closing window stops a replace, and the adopter moved the folder back
            stopped = true;
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            error = InstallFailureText(exception);
        }
        finally
        {
            EndInstallRun(run, completed, stopped, error);
            row.IsInstalling = false;
            row.Run = null;
            row.Progress = 0;
            row.ProgressStatus = null;
            row.ProgressDetail = null;
        }

        await ReloadInstancesAsync();
    }

    private static Instance WithoutForeignFolder(Instance instance, string folderName)
        => Instance.FromExisting(
            instance.InstanceId,
            instance.Name,
            instance.Source,
            instance.CreatedAt,
            instance.Mods,
            instance.ForeignMods.Where(mod => !ModIds.Equals(mod.ModId, folderName)).ToList(),
            instance.IsFavorite);
}

public enum ManualInstallCheck
{
    NotChecked,
    NoMatch,
    NotRecorded,
}

/// <summary>
/// One folder on the Manual installs tab. <see cref="IsInIndex"/> is null when the content index could not be read.
/// </summary>
public sealed partial class ManualInstallItem : ObservableObject, IInstallRow
{
    private readonly MainViewModel _owner;

    public Guid InstanceId { get; }

    public string FolderName { get; }

    public bool? IsInIndex { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    [NotifyPropertyChangedFor(nameof(CanManage))]
    private bool _isChecking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    [NotifyPropertyChangedFor(nameof(CanManage))]
    private bool _isInstalling;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string? _progressStatus;

    [ObservableProperty]
    private string? _progressDetail;

    [ObservableProperty]
    private InstallRun? _run;

    [ObservableProperty]
    private string? _installError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    [NotifyPropertyChangedFor(nameof(CanManage))]
    private string? _installWarning;

    [ObservableProperty]
    private InstallPlan? _pendingPlan;

    public InstallChoices? Choices { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    [NotifyPropertyChangedFor(nameof(CanManage))]
    private bool _isConfirmingReplace;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(CanManage))]
    private ManualInstallCheck _check;

    public ManualInstallItem(MainViewModel owner, Guid instanceId, string folderName, bool? isInIndex)
    {
        _owner = owner;
        InstanceId = instanceId;
        FolderName = folderName;
        IsInIndex = isInIndex;
    }

    public bool IsBusy => IsChecking || IsInstalling;

    public bool CanAct => IsInIndex == true && !IsBusy && !IsConfirmingReplace && InstallWarning is null;

    public bool CanManage => CanAct && Check == ManualInstallCheck.NotChecked;

    public string? StatusText => IsInIndex switch
    {
        true => Check switch
        {
            ManualInstallCheck.NoMatch => _owner.Localization.ManualInstallsNoMatch,
            ManualInstallCheck.NotRecorded => _owner.Localization.ManualInstallsNotRecorded,
            _ => _owner.Localization.ManualInstallsInIndex,
        },
        false => _owner.Localization.ManualInstallsNotInIndex,
        null => null,
    };

    public string ReplaceWarningText => _owner.Localization.FormatManualInstallsReplaceWarning(FolderName);

    public void RefreshText()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ReplaceWarningText));
    }

    [RelayCommand]
    private Task ManageAsync() => _owner.ManageManualInstallAsync(this);

    [RelayCommand]
    private Task BeginReplaceAsync() => _owner.BeginReplaceManualInstallAsync(this);

    [RelayCommand]
    private Task ConfirmReplaceAsync() => _owner.ConfirmReplaceManualInstallAsync(this);

    [RelayCommand]
    private void CancelReplace() => IsConfirmingReplace = false;

    [RelayCommand]
    private Task ConfirmInstallAsync() => _owner.ConfirmManualReplaceInstallAsync(this);

    [RelayCommand]
    private void CancelInstall() => MainViewModel.CancelInstall(this);
}
