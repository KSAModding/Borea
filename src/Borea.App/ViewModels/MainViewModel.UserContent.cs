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
using Borea.Core.UserContent;

namespace Borea.App.ViewModels;

/// <summary>A row that installs or updates one vehicle or save, and holds its plan until the player confirms it.</summary>
internal interface IContentPlanRow : IInstallRow
{
    /// <summary>The install or update that waits for a confirmation. Its mods are <see cref="IPlanRow.PendingPlan"/>.</summary>
    UserContentPlan? PendingContentPlan { get; set; }
}

/// <summary>
/// The install and update of one vehicle or save with the mods it needs (RFC 0085), through the same code as the CLI.
/// A plan runs at once when it installs and enables nothing and asks nothing, as a mod does, and waits on the row otherwise.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Plans the install of a vehicle or save into an instance, or the update of the record that the instance has of it.</summary>
    /// <param name="name">A new name the player chose in the name modal, or null.</param>
    internal async Task PlanContentAsync(IContentPlanRow row, Guid instanceId, ModVersionMetadata release, string? name = null, bool discardChanges = false)
    {
        if (_services is not { } services || row.IsInstalling)
            return;

        UserContentPlan plan;
        row.InstallError = null;
        row.InstallWarning = null;
        row.PendingPlan = null;
        row.PendingContentPlan = null;
        row.Choices = null;
        row.ProgressStatus = null;
        row.IsInstalling = true;
        StartContentInstall(release);
        try
        {
            plan = await PlanContentWithRecommendationsAsync(services, instanceId, release, name, discardChanges, choices: null);
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            row.InstallError = InstallFailureText(exception);
            return;
        }
        finally
        {
            EndContentInstall(release);
            row.IsInstalling = false;
        }

        switch (plan.Check.Outcome)
        {
            case UserContentOutcome.NameTaken:
                AskContentName(row, instanceId, plan, discardChanges);
                return;
            case UserContentOutcome.Ready or UserContentOutcome.Changed:
                break;
            default:
                row.InstallError = ContentOutcomeText(plan.Check, release, instanceId);
                return;
        }

        if (InstallChoices.AreNeeded(plan.Mods))
        {
            row.Choices = NewChoices(instanceId, [new RequestedMod(release, InstallReason.Manual)], plan.Mods);
            HoldContentPlan(row, plan);
        }
        else if (!plan.Mods.IsReady)
        {
            row.InstallError = Describe(plan.Mods.Conflicts.Concat(plan.Mods.UnresolvedChoices));
        }
        else if (plan.Check.Outcome == UserContentOutcome.Changed || plan.Mods.Warnings.Count > 0 || plan.Enables.Count > 0
            || AddedMods(plan.Mods, null).Any() || InstallStepsOf(plan.Mods).Count > 0)
        {
            HoldContentPlan(row, plan);
        }
        else
        {
            await ExecuteContentPlanAsync(row, plan);
        }
    }

    /// <summary>
    /// Runs the plan the row holds. A plan that found changed files, or whose choices the player answered, plans again
    /// first, and waits once more when that plan differs from what the row showed.
    /// </summary>
    internal async Task ConfirmContentAsync(IContentPlanRow row)
    {
        if (_services is not { } services || row.PendingContentPlan is not { } shown || row.IsInstalling)
            return;

        var plan = shown;
        if (row.Choices is not null || shown.Check.Outcome == UserContentOutcome.Changed)
        {
            var instanceId = shown.Mods.InstanceId;
            try
            {
                row.IsInstalling = true;
                plan = await PlanContentWithRecommendationsAsync(services, instanceId, shown.Release, shown.Check.FolderName, shown.DiscardChanges || shown.Check.Outcome == UserContentOutcome.Changed, row.Choices, shown.Installed);
            }
            catch (Exception exception) when (IsInstallFailure(exception))
            {
                row.InstallError = InstallFailureText(exception);
                return;
            }
            finally
            {
                row.IsInstalling = false;
            }

            var asksMore = row.Choices?.Apply(plan.Mods) == true;
            if (plan.Check.Outcome != UserContentOutcome.Ready || !plan.Mods.IsReady || asksMore || !WereShown(plan.Mods.Warnings, shown.Mods.Warnings))
            {
                if (plan.Check.Outcome is UserContentOutcome.Ready or UserContentOutcome.Changed)
                    HoldContentPlan(row, plan);
                else
                    row.InstallError = ContentOutcomeText(plan.Check, plan.Release, instanceId);
                return;
            }
        }

        await ExecuteContentPlanAsync(row, plan);
    }

    private async Task ExecuteContentPlanAsync(IContentPlanRow row, UserContentPlan plan)
    {
        if (_services is not { } services || row.IsInstalling)
            return;

        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            row.InstallError = Localization.LibraryFolderBusy;
            return;
        }

        var instanceId = plan.Mods.InstanceId;
        row.PendingPlan = null;
        row.PendingContentPlan = null;
        row.InstallWarning = null;
        row.Choices = null;
        row.IsInstalling = true;
        StartContentInstall(plan.Release);
        var run = row.Run = StartInstallRun(StartInstallTask(row, instanceId));
        var completed = false;
        string? stopped = null;
        string? error = null;
        try
        {
            run.TaskItem.MarkRunning(plan.Mods);

            // the player saw the mods it misses before the go, so the install goes on without them
            var result = await services.UserContentInstallation.ExecuteAsync(plan, allowMissing: true, ProgressOf(row), run.InstallStop);
            completed = result.IsDone;
            error = completed ? null : ContentOutcomeText(result, plan.Release, instanceId);
        }
        catch (InstallStoppedException exception)
        {
            run.TaskItem.StoppedAfter = (exception.Completed, exception.Total);
            stopped = StoppedText(row, exception.Completed, exception.Total);
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            error = InstallFailureText(exception);
        }
        finally
        {
            EndContentInstall(plan.Release);
            EndInstallRun(run, completed, stopped is not null, error);
            row.IsInstalling = false;
            row.Run = null;
            row.Progress = 0;
            row.ProgressStatus = stopped;
            row.ProgressDetail = null;
        }

        await ReloadInstancesAsync();
        row.InstallError = error;
        if (completed && plan.Installed is null)
            LeaveInstalledWhereNotice(plan.Release.ModId, instanceId);
    }

    /// <summary>Repeats the plan until it names no recommendation that is neither selected nor deselected, as a mod install does.</summary>
    /// <param name="installed">The record an update replaces. Null takes the record of the listing in the instance, if any.</param>
    private async Task<UserContentPlan> PlanContentWithRecommendationsAsync(BoreaServices services, Guid instanceId, ModVersionMetadata release, string? name, bool discardChanges, InstallChoices? choices, InstalledContentRecord? installed = null)
    {
        var instance = await services.Instances.GetByIdAsync(instanceId)
            ?? throw new InvalidOperationException(Localization.InstallInstanceMissing);
        installed ??= (await services.InstalledContent.ListAsync(instanceId)).FirstOrDefault(record => ModIds.Equals(record.ListingId, release.ModId));
        var deselected = choices?.DeselectedRecommendations ?? new HashSet<string>(StringComparer.Ordinal);
        var recommended = new HashSet<string>(choices?.SelectedRecommendations ?? new HashSet<string>(), StringComparer.Ordinal);
        var gameVersion = PlanningGameVersion(services, instance);
        while (true)
        {
            var request = new UserContentRequest(instance, release, services.Mods, gameVersion, services.GamePlatform.Current, recommended, choices?.SelectedAlternatives, Name: name);
            var plan = installed is null
                ? await services.UserContentInstallation.PlanInstallAsync(request)
                : await services.UserContentInstallation.PlanUpdateAsync(installed, request, discardChanges);
            var added = false;
            foreach (var choice in plan.Mods.Choices.Where(choice => choice.Kind == PlanningChoiceKind.Recommendation && !deselected.Contains(choice.Key)))
                added |= recommended.Add(choice.Key);

            if (!added)
                return plan;
        }
    }

    /// <summary>Holds the plan on the row with what it does besides the mods: the mods it enables and the files the player changed.</summary>
    private void HoldContentPlan(IContentPlanRow row, UserContentPlan plan)
    {
        row.PendingContentPlan = plan;
        HoldPlan(row, plan.Mods);
        var name = ContentName(plan.Release.ModId);
        var notes = new List<string>();
        if (row.InstallWarning is { } warning)
            notes.Add(warning);
        if (plan.Enables.Count > 0)
            notes.Add(Localization.FormatPackContentEnables(string.Join(", ", plan.Enables.Select(ContentName)), name));
        if (plan.Check.Outcome == UserContentOutcome.Changed && plan.Check.Changes is { } changes)
            notes.Add(Localization.FormatPackContentChanged(name, ChangedFilesText(changes)));
        row.InstallWarning = notes.Count > 0 ? string.Join(" ", notes) : null;
    }

    /// <summary>Opens the name modal of #556 for an item whose name is taken. The new name plans the item again.</summary>
    private void AskContentName(IContentPlanRow row, Guid instanceId, UserContentPlan plan, bool discardChanges)
    {
        var release = plan.Release;
        var name = ContentName(release.ModId);
        var taken = plan.Check.FolderName ?? release.Install?.Folder ?? name;
        var names = new Dictionary<string, string>(ModIds.Comparer);
        InstanceError = null;
        IsCreatingInstance = false;
        RenamingInstance = null;
        RenamingGameSave = null;
        ModalInstanceName = FreeNameSuggestion(taken);
        var explanation = Localization.FormatContentNameTaken(taken, release.Type == ContentType.Save ? Localization.InstanceGroupSaves : Localization.InstanceGroupVehicles);
        NamingPackContent = new PackContentNaming(release.ModId, name, explanation, names, () => PlanContentAsync(row, instanceId, release, names[release.ModId], discardChanges), IsPackMember: false);
    }

    /// <summary>Why the folder step stopped, in the language of the App.</summary>
    private string ContentOutcomeText(UserContentResult result, ModVersionMetadata release, Guid instanceId) => result.Outcome switch
    {
        UserContentOutcome.GameRunning => Localization.GameSaveCloseGame,
        UserContentOutcome.AlreadyInstalled => Localization.FormatLinkAlreadyInstalled(ContentName(release.ModId), Instances.FirstOrDefault(instance => instance.InstanceId == instanceId)?.Name ?? string.Empty),
        UserContentOutcome.NameTaken => release.Type == ContentType.Save ? Localization.GameSaveSaveNameTaken : Localization.GameSaveVehicleNameTaken,
        UserContentOutcome.InvalidName => Localization.GameSaveNameNeedsLetter,
        UserContentOutcome.Changed when result.Changes is { } changes => Localization.FormatPackContentChanged(ContentName(release.ModId), ChangedFilesText(changes)),
        _ => Localization.ContentGoneFromInstance,
    };

    /// <summary>
    /// Removes the recorded vehicle or save of a Discover row or page through the code of the CLI. A folder the player
    /// changed asks once more and names the files, and the second go moves them into the backups with the folder.
    /// </summary>
    private async Task RemoveRecordedContentAsync(DiscoverItem item, InstanceItem instance)
    {
        if (_services is not { } services)
            return;

        if (instance.Content.FirstOrDefault(record => ModIds.Equals(record.ListingId, item.ModId)) is not { } record)
        {
            item.IsConfirmingRemove = false;
            item.InstallError = Localization.ContentGoneFromInstance;
            return;
        }

        item.IsRemoving = true;
        item.InstallError = null;
        var task = StartTask(TaskKind.ModRemoval, item.Name, instance.InstanceId, item.ModId);
        var completed = false;
        var asksAgain = false;
        string? error = null;
        try
        {
            // the page of this vehicle can be open, and its preview can read this folder
            await StopVehiclePreviewReadsAsync();
            var result = await services.UserContentInstaller.RemoveAsync(instance.InstanceId, record, discardChanges: item.RemoveChanges is not null);
            if (result.Outcome == UserContentOutcome.Changed && result.Changes is { } changes)
            {
                item.RemoveChanges = ChangedFilesText(changes);
                asksAgain = true;
            }
            else if (result.IsDone)
            {
                completed = true;
            }
            else
            {
                error = ContentOutcomeText(result, record.Metadata, instance.InstanceId);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            error = exception.Message;
        }
        finally
        {
            item.IsRemoving = false;
            item.IsConfirmingRemove = asksAgain;
            if (!asksAgain)
                item.RemoveChanges = null;
            EndTask(task, completed, stopped: false, error);
        }

        RestoreVehiclePreview();
        if (completed)
            await ReloadInstancesAsync();
        item.InstallError = error;
    }

    internal static string ChangedFilesText(InstalledContentChanges changes)
        => string.Join(", ", changes.Added.Concat(changes.Removed).Concat(changes.Changed));
}
