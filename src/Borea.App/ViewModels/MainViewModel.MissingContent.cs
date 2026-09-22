using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// The mods an instance records while their folder is gone. Opening the page
/// compares the record with the folders, marks the rows, and offers to install
/// the mods again or to drop them from the record.
/// </summary>
public partial class MainViewModel
{
    /// <summary>The notice of the shown instance, or null while every recorded mod is on disk.</summary>
    [ObservableProperty]
    private MissingContentItem? _missingContent;

    private async Task ShowMissingContentAsync(Guid instanceId)
    {
        // a running "Install again" keeps its notice, with its progress and its buttons
        if (_runningUpdates.GetValueOrDefault(instanceId) is MissingContentItem running)
        {
            MissingContent = running;
            return;
        }

        MissingContent = null;

        // a row the page carried over from a running task keeps the mark of the scan before it
        foreach (var item in _content)
            item.IsMissing = false;

        if (_services is not { } services)
            return;

        IReadOnlyList<string> missing;
        try
        {
            missing = await services.MissingMods.ScanAsync(instanceId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowErrorToast(() => Localization.ToastMissingScanFailed, exception.Message);
            return;
        }

        if (SelectedInstance?.InstanceId != instanceId)
            return;

        foreach (var item in _content)
            item.IsMissing = missing.Contains(item.ModId, ModIds.Comparer);

        if (missing.Count > 0)
            MissingContent = new MissingContentItem(this, instanceId, missing);
    }

    /// <summary>Whether the mod of the shown instance has no folder any more.</summary>
    private bool IsMissingContent(Guid instanceId, string modId)
        => MissingContent is { } missing
            && missing.InstanceId == instanceId
            && missing.MissingIds.Contains(modId, ModIds.Comparer);

    internal Task InstallMissingAgainAsync(IInstallRow row, Guid instanceId, IReadOnlyList<string> modIds)
        => RunUpdateAsync(row, instanceId, () => PlanMissingInstallAsync(row, instanceId, modIds));

    internal Task ConfirmMissingInstallAsync(IInstallRow row, Guid instanceId, IReadOnlyList<string> modIds)
        => RunUpdateAsync(row, instanceId, () => ConfirmedMissingInstallAsync(row, instanceId, modIds));

    /// <summary>
    /// Plans the mods at the version the record names, against the instance
    /// without those records. Planning first leaves the record alone while a
    /// conflict or a warning is still open. Returns whether the plan ran.
    /// </summary>
    private async Task<bool> PlanMissingInstallAsync(IInstallRow row, Guid instanceId, IReadOnlyList<string> modIds)
    {
        if (_services is not { } services || row.IsInstalling || modIds.Count == 0)
            return false;

        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            row.InstallError = Localization.LibraryFolderBusy;
            return false;
        }

        row.InstallError = null;
        row.InstallWarning = null;
        row.PendingPlan = null;
        row.ProgressStatus = null;

        // a folder without a mod.toml counts as missing but still holds the name
        // the install needs, and it is not Borea's to delete, so it is named
        if (await FindFolderInTheWayAsync(services, instanceId, modIds) is { } inTheWay)
        {
            row.InstallError = Localization.FormatContentFolderInTheWay(inTheWay);
            return false;
        }

        row.IsInstalling = true;
        InstallPlan? ready = null;
        try
        {
            var instance = await services.Instances.GetByIdAsync(instanceId)
                ?? throw new InvalidOperationException(Localization.InstallInstanceMissing);
            var requested = instance.Mods
                .Where(mod => modIds.Contains(mod.ModId, ModIds.Comparer))
                .Select(mod => new RequestedMod(mod.Metadata, mod.Reason, Exact: true))
                .ToList();
            if (requested.Count == 0)
                return false;

            var plan = await services.InstallPlanner.PlanAsync(PlanningRequest(services, WithoutMods(instance, modIds), requested));
            if (!plan.IsReady)
            {
                row.InstallError = Describe(plan.Conflicts.Concat(plan.UnresolvedChoices));
            }
            else if (plan.Warnings.Count > 0)
            {
                row.PendingPlan = plan;
                row.InstallWarning = Describe(plan.Warnings);
            }
            else
            {
                ready = plan;
            }
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            row.InstallError = exception.Message;
        }
        finally
        {
            if (ready is null)
                row.IsInstalling = false;
        }

        return ready is not null && await ExecuteMissingInstallAsync(services, row, instanceId, modIds, ready);
    }

    private async Task<bool> ConfirmedMissingInstallAsync(IInstallRow row, Guid instanceId, IReadOnlyList<string> modIds)
    {
        if (_services is not { } services || row.PendingPlan is not { } plan || row.IsInstalling)
            return false;

        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            row.InstallError = Localization.LibraryFolderBusy;
            return false;
        }

        row.PendingPlan = null;
        row.InstallWarning = null;
        row.IsInstalling = true;
        return await ExecuteMissingInstallAsync(services, row, instanceId, modIds, plan);
    }

    /// <summary>
    /// Drops the records and runs the plan. A run that does not install a mod
    /// puts its record back, so the page keeps offering the mod instead of
    /// losing it. Returns whether the plan ran.
    /// </summary>
    private async Task<bool> ExecuteMissingInstallAsync(BoreaServices services, IInstallRow row, Guid instanceId, IReadOnlyList<string> modIds, InstallPlan plan)
    {
        // read again, because a folder can appear while a warning waits for the user
        if (await FindFolderInTheWayAsync(services, instanceId, modIds) is { } inTheWay)
        {
            row.InstallError = Localization.FormatContentFolderInTheWay(inTheWay);
            row.IsInstalling = false;
            return false;
        }

        var run = row.Run = StartInstallRun(StartTask(TaskKind.ModInstall, MissingSubject(modIds), instanceId, modIds.Count == 1 ? modIds[0] : null));
        var completed = false;
        string? stopped = null;
        string? error = null;
        var dropped = new List<InstalledMod>();
        try
        {
            var instance = await services.Instances.GetByIdAsync(instanceId)
                ?? throw new InvalidOperationException(Localization.InstallInstanceMissing);
            if (!plan.InstanceState.Matches(WithoutMods(instance, modIds)))
                throw new InvalidOperationException(Localization.ManualInstallsInstanceChanged);

            // the records go first, because Borea replaces a mod through the
            // folder it owns and there is none left to replace
            foreach (var modId in modIds)
            {
                var record = instance.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, modId))
                    ?? throw new InvalidOperationException(Localization.ManualInstallsInstanceChanged);
                if (!await services.MissingMods.DropAsync(instanceId, modId))
                    throw new InvalidOperationException(Localization.FormatContentBackOnDisk(ContentName(modId)));

                dropped.Add(record);
            }

            run.TaskItem.MarkRunning(plan);
            await services.PlanExecutor.ExecuteAsync(plan, enable: true, ProgressOf(row), run.InstallStop);
            completed = true;
        }
        catch (InstallStoppedException exception)
        {
            run.TaskItem.StoppedAfter = (exception.Completed, exception.Total);
            stopped = StoppedText(row, exception.Completed, exception.Total);
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            error = row.InstallError = exception.Message;
        }
        finally
        {
            await RestoreMissingRecordsAsync(services, instanceId, dropped);
            EndInstallRun(run, completed, stopped is not null, error);
            row.IsInstalling = false;
            row.Run = null;
            row.Progress = 0;
            row.ProgressStatus = stopped;
            row.ProgressDetail = null;
        }

        return true;
    }

    /// <summary>
    /// The folder that carries the id of one of the mods but no mod.toml, which
    /// an install cannot write over. Null when the folders cannot be read, so
    /// the install runs and reports what it meets.
    /// </summary>
    private static async Task<string?> FindFolderInTheWayAsync(BoreaServices services, Guid instanceId, IReadOnlyList<string> modIds)
    {
        try
        {
            foreach (var modId in modIds)
            {
                if (await services.MissingMods.FindLeftoverFolderAsync(instanceId, modId) is { } folder)
                    return folder;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// Puts back the records the run dropped for the mods it did not install.
    /// A mod the run did install keeps the record the install wrote.
    /// </summary>
    private static async Task RestoreMissingRecordsAsync(BoreaServices services, Guid instanceId, IReadOnlyList<InstalledMod> dropped)
    {
        if (dropped.Count == 0)
            return;

        try
        {
            await services.Instances.UpdateAsync(instanceId, instance =>
            {
                var restored = false;
                foreach (var record in dropped)
                {
                    if (instance.Mods.Any(mod => ModIds.Equals(mod.ModId, record.ModId)))
                        continue;

                    instance.AddMod(record);
                    restored = true;
                }

                return restored;
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // the records stay dropped, and the reloaded page shows the instance as it is
        }
    }

    /// <summary>
    /// Drops the records of mods whose folder is gone. Nothing on disk changes,
    /// because the files are already gone.
    /// </summary>
    internal async Task DropMissingContentAsync(Guid instanceId, IReadOnlyList<string> modIds)
    {
        if (_services is not { } services || modIds.Count == 0 || _runningUpdates.ContainsKey(instanceId))
            return;

        var subject = MissingSubject(modIds);
        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            ShowErrorToast(() => Localization.FormatToastRemoveFailed(subject), Localization.LibraryFolderBusy);
            return;
        }

        var task = StartTask(TaskKind.ModRemoval, subject, instanceId, modIds.Count == 1 ? modIds[0] : null);
        var completed = false;
        string? error = null;
        try
        {
            foreach (var modId in modIds)
                await services.MissingMods.DropAsync(instanceId, modId);
            completed = true;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            error = exception.Message;
        }
        finally
        {
            EndTask(task, completed, stopped: false, error);
        }

        await ReloadInstancesAsync();
    }

    /// <summary>What the task of an install or a drop is named after.</summary>
    private string MissingSubject(IReadOnlyList<string> modIds) => string.Join(", ", modIds.Select(ContentName));

    /// <summary>The instance as it is once the records of the named mods are gone.</summary>
    private static Instance WithoutMods(Instance instance, IReadOnlyList<string> modIds)
        => Instance.FromExisting(
            instance.InstanceId,
            instance.Name,
            instance.Source,
            instance.CreatedAt,
            instance.Mods.Where(mod => !modIds.Contains(mod.ModId, ModIds.Comparer)).ToList(),
            instance.ForeignMods,
            instance.IsFavorite,
            instance.LastPlayedAt,
            instance.LaunchArguments);
}

/// <summary>
/// The notice of the instance page for the mods whose folder is gone, which
/// acts on all of them at once.
/// </summary>
public sealed partial class MissingContentItem : ObservableObject, IInstallRow
{
    private readonly MainViewModel _owner;

    internal Guid InstanceId { get; }

    internal IReadOnlyList<string> MissingIds { get; }

    public string NoticeText => _owner.Localization.FormatInstanceModsMissing(MissingIds.Count);

    public string ConfirmDropText => _owner.Localization.ContentRemoveFromListConfirm;

    [ObservableProperty]
    private bool _isConfirmingDrop;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingInstall))]
    private string? _installWarning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingInstall))]
    private InstallPlan? _pendingPlan;

    public bool IsConfirmingInstall => PendingPlan is not null;

    [ObservableProperty]
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

    /// <summary>The notice plans one exact version per mod, so it asks nothing.</summary>
    public InstallChoices? Choices { get; set; }

    public MissingContentItem(MainViewModel owner, Guid instanceId, IReadOnlyList<string> missingIds)
    {
        _owner = owner;
        InstanceId = instanceId;
        MissingIds = missingIds;
    }

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(NoticeText));
        OnPropertyChanged(nameof(ConfirmDropText));
    }

    [RelayCommand]
    private Task InstallAgainAsync() => _owner.InstallMissingAgainAsync(this, InstanceId, MissingIds);

    [RelayCommand]
    private Task ConfirmInstallAsync() => _owner.ConfirmMissingInstallAsync(this, InstanceId, MissingIds);

    [RelayCommand]
    private void CancelInstall()
    {
        PendingPlan = null;
        InstallWarning = null;
    }

    [RelayCommand]
    private void BeginDrop() => IsConfirmingDrop = true;

    [RelayCommand]
    private void CancelDrop() => IsConfirmingDrop = false;

    [RelayCommand]
    private Task ConfirmDropAsync()
    {
        IsConfirmingDrop = false;
        return _owner.DropMissingContentAsync(InstanceId, MissingIds);
    }
}
