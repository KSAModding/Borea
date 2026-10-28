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
/// The table of the Content tab for the mod folders of the instance that Borea did not install.
/// </summary>
public partial class MainViewModel
{
    private bool? _foreignFolderDeletionConfirmed;

    public ObservableCollection<ManualInstallItem> ManualInstallItems { get; } = [];

    public bool HasManualInstalls => ManualInstallItems.Count > 0;

    /// <summary>Whether the Content tab shows the table, which a failed scan shows to give its error.</summary>
    public bool ShowManualInstalls => HasManualInstalls || ManualInstallsError is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowManualInstalls))]
    private string? _manualInstallsError;

    /// <summary>Opens the mods folder of the instance on the page, where a mod installed by hand goes.</summary>
    [RelayCommand]
    private void OpenInstanceModsFolder()
    {
        if (_services is not { } services || SelectedInstance is not { } instance)
            return;

        var folder = services.Paths.GetInstanceModsFolder(instance.InstanceId);
        OpenCreatingFolder(() => folder, () => PathName(folder));
    }

    /// <summary>
    /// Every open of the instance page reads the rows again. A row of the same instance that checks or
    /// replaces its folder stays, so its progress and its end reach the row on the page.
    /// </summary>
    private async Task LoadManualInstallsAsync()
    {
        var instanceId = SelectedInstance?.InstanceId;
        if (ManualInstallItems.Any(row => row.InstanceId != instanceId))
            ShowManualInstallRows([], null);

        if (_services is null || instanceId is not { } id)
            return;

        var services = _services;
        // a row that is busy now stays, so its folder needs no index lookup
        var busyAtStart = ManualInstallItems.Where(row => row.IsBusy).ToDictionary(row => row.FolderName, StringComparer.Ordinal);
        var found = new List<(string FolderName, bool? InIndex)>();
        string? error = null;
        try
        {
            foreach (var foreign in await ReadForeignModsAsync(services, id))
            {
                bool? inIndex = null;
                if (error is null && !busyAtStart.ContainsKey(foreign.FolderName))
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

                found.Add((foreign.FolderName, inIndex));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error = exception.Message;
        }

        if (SelectedInstance?.InstanceId != id)
            return;

        // a row can start a replace while the load waits, so the rows to keep are read only now
        var current = ManualInstallItems.Where(row => row.InstanceId == id).ToDictionary(row => row.FolderName, StringComparer.Ordinal);
        var rows = new List<ManualInstallItem>();
        foreach (var (folderName, inIndex) in found)
        {
            if (current.Remove(folderName, out var row) && (row.IsBusy || busyAtStart.ContainsKey(folderName)))
                rows.Add(row);
            else
                rows.Add(busyAtStart.GetValueOrDefault(folderName) ?? new ManualInstallItem(this, id, folderName, inIndex));
        }

        // a replace moves the folder aside while it installs, so the scan can miss a busy row
        rows.AddRange(current.Values.Where(row => row.IsBusy));
        ShowManualInstallRows(rows.OrderBy(row => row.FolderName, ModIds.Comparer), error);
    }

    /// <summary>
    /// The scan writes the folders it finds into the instance record. A running install or update of the
    /// instance stops when that record changed after its plan, and a library folder change holds the record
    /// for the whole move, so while either runs the rows come from the record as it is.
    /// </summary>
    private async Task<IReadOnlyList<ForeignMod>> ReadForeignModsAsync(BoreaServices services, Guid instanceId)
    {
        if (!IsChangingContentOf(instanceId))
        {
            using var libraryUse = TryUseLibrary();
            if (libraryUse is not null)
                return await services.ForeignModAdopter.ScanAsync(instanceId);
        }

        var instance = await services.Instances.GetByIdAsync(instanceId)
            ?? throw new InvalidOperationException(Localization.InstallInstanceMissing);
        return instance.ForeignMods;
    }

    /// <summary>Whether an update, an install or a replace changes the content of the instance now.</summary>
    private bool IsChangingContentOf(Guid instanceId)
        => _runningUpdates.ContainsKey(instanceId) || _installRuns.Any(run => run.TaskItem.InstanceId == instanceId);

    private void ShowManualInstallRows(IEnumerable<ManualInstallItem> rows, string? error)
    {
        ManualInstallItems.Clear();
        foreach (var row in rows)
            ManualInstallItems.Add(row);
        OnPropertyChanged(nameof(HasManualInstalls));
        OnPropertyChanged(nameof(ShowManualInstalls));
        ManualInstallsError = error;
    }

    internal async Task ManageManualInstallAsync(ManualInstallItem row)
    {
        if (_services is null || row.IsBusy || _runningUpdates.ContainsKey(row.InstanceId))
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
    /// The page turns the row actions off while an update of the instance runs, because two executors on one
    /// instance can lose a write to its mod list.
    /// </summary>
    private async Task PlanManualReplaceAsync(ManualInstallItem row)
    {
        if (_services is null || row.IsBusy || _runningUpdates.ContainsKey(row.InstanceId))
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
                PlanningGameVersion(services, instance),
                services.GamePlatform.Current);
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
        if (_services is null || row.PendingPlan is not { } plan || row.IsBusy || _runningUpdates.ContainsKey(row.InstanceId))
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
            EndRowInstall(row, null);
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
/// One mod folder that Borea did not install. <see cref="IsInIndex"/> is null when the content index could not be read.
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
