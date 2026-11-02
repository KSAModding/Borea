using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Borea.App.ViewModels;

internal interface IInstallProgressRow
{
    bool IsInstalling { get; set; }

    double Progress { get; set; }

    /// <summary>
    /// What the install is doing, for example "Downloading MeasureTools 1.1.10 (2 of 3)",
    /// and after a stop how far it got.
    /// </summary>
    string? ProgressStatus { get; set; }

    /// <summary>Size and time left while downloading, otherwise null.</summary>
    string? ProgressDetail { get; set; }

    InstallRun? Run { get; set; }
}

/// <summary>A row that holds a plan until the user confirms it.</summary>
internal interface IPlanRow : IInstallProgressRow
{
    InstallPlan? PendingPlan { get; set; }

    /// <summary>What <see cref="PendingPlan"/> asks the user before it runs, or null.</summary>
    InstallChoices? Choices { get; set; }
}

/// <summary>
/// A row that installs a release: a Discover row, a row of the versions table,
/// or an update on the instance page.
/// </summary>
internal interface IInstallRow : IPlanRow
{
    string? InstallError { get; set; }

    string? InstallWarning { get; set; }
}

/// <summary>
/// Installs go through the planner and the executor the CLI uses. The planner
/// adds required dependencies and blocks an incompatible release, and the
/// executor writes only while the instance still matches the plan.
/// </summary>
public partial class MainViewModel
{
    private const int AddedModsShown = 3;

    private readonly List<InstallRun> _installRuns = [];

    /// <summary>True once the window asked the running installs to stop so that it can close.</summary>
    [ObservableProperty]
    private bool _isClosing;

    internal bool HasRunningInstalls => _installRuns.Count > 0;

    /// <summary>
    /// Stops every running install at its safe point and returns once all of
    /// them ended. An install that starts meanwhile stops at once.
    /// </summary>
    internal async Task StopInstallsAsync()
    {
        IsClosing = true;
        while (_installRuns.Count > 0)
        {
            var runs = _installRuns.ToList();
            foreach (var run in runs)
                run.Stop();
            await Task.WhenAll(runs.Select(run => run.Ended));
        }
    }

    private InstallRun StartInstallRun(TaskItem task)
    {
        var run = task.Run = new InstallRun(Localization, task);
        _installRuns.Add(run);
        if (IsClosing)
        {
            run.Stop();
            RefreshCloseWaitsFor();
        }

        return run;
    }

    private void EndInstallRun(InstallRun run, bool completed, bool stopped, string? error)
    {
        _installRuns.Remove(run);
        run.End();
        EndTask(run.TaskItem, completed, stopped, error);
    }

    /// <param name="stopped">What the row or the pack update notice shows after a stop, or null.</param>
    private static void EndRowInstall(IInstallProgressRow row, string? stopped)
    {
        row.IsInstalling = false;
        row.Run = null;
        row.Progress = 0;
        row.ProgressStatus = stopped;
        row.ProgressDetail = null;
    }

    /// <summary>What a stopped install shows where its progress was.</summary>
    private string StoppedText(IInstallProgressRow row, int completed = 0, int total = 0) => row is IUpdateRow or PackUpdateItem
        ? completed == 0 ? Localization.UpdateStopped : Localization.FormatUpdateStoppedAfter(completed, total)
        : completed == 0 ? Localization.InstallStopped : Localization.FormatInstallStoppedAfter(completed, total);

    /// <summary>
    /// Plans the install of one release into the active instance. A ready plan
    /// that installs only that release and has no warnings, choices or install
    /// steps runs at once. Any other plan waits on the row until the user confirms
    /// or cancels it.
    /// </summary>
    /// <param name="exactVersion">The version the request pins. Null plans the newest release.</param>
    /// <param name="instanceId">The instance a Try again of the Tasks page installs into. Null installs into the active instance.</param>
    /// <param name="confirm">Holds even a plan without warnings or choices, for an install that a borea:// link asked for.</param>
    internal async Task PlanInstallAsync(IInstallRow row, Func<Task<ModVersionMetadata?>> findRelease, ModVersion? exactVersion, Guid? instanceId = null, bool confirm = false)
    {
        if (_services is null || row.IsInstalling)
            return;

        if ((instanceId ?? ActiveInstance?.InstanceId) is not { } target)
        {
            ShowInstanceHintToast();
            return;
        }

        RememberRequestedVersion(row, exactVersion);
        var executed = await PlanAndExecuteAsync(
            row,
            target,
            async _ =>
            {
                var release = await findRelease() ?? throw new InvalidOperationException(Localization.DiscoverNoRelease);
                return [new RequestedMod(release, InstallReason.Manual, exactVersion is not null)];
            },
            (_, plan) => Task.FromResult(confirm || AddedMods(plan, null).Any() || InstallStepsOf(plan).Count > 0),
            () => PlanInstallAsync(row, findRelease, exactVersion, target, confirm));

        if (executed)
            await ReloadInstancesAsync();
    }

    /// <summary>
    /// Plans the requested mods into the instance and runs a ready plan without
    /// warnings or choices, unless <paramref name="waitForConfirmation"/> holds it.
    /// Returns whether the executor ran, so the caller reloads the instances.
    /// </summary>
    /// <param name="again">Plans the same change again, for the conflict modal.</param>
    private async Task<bool> PlanAndExecuteAsync(IInstallRow row, Guid instanceId, Func<Instance, Task<IReadOnlyList<RequestedMod>>> requestMods, Func<Instance, InstallPlan, Task<bool>>? waitForConfirmation = null, Func<Task>? again = null)
    {
        if (_services is null || row.IsInstalling)
            return false;

        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            row.InstallError = Localization.LibraryFolderBusy;
            return false;
        }

        var services = _services;
        row.InstallError = null;
        row.InstallWarning = null;
        row.PendingPlan = null;
        row.Choices = null;
        row.ProgressStatus = null;
        row.IsInstalling = true;
        var run = row.Run = StartInstallRun(StartInstallTask(row, instanceId));
        var executed = false;
        var completed = false;
        string? stopped = null;
        try
        {
            var instance = await services.Instances.GetByIdAsync(instanceId)
                ?? throw new InvalidOperationException(Localization.InstallInstanceMissing);
            var requested = await requestMods(instance);
            var plan = await PlanWithChoicesAsync(services, PlanningRequest(services, instance, requested), null, run.InstallStop.Token);
            var choices = InstallChoices.AreNeeded(plan) ? NewChoices(instanceId, requested, plan) : null;
            var wait = (plan.IsReady || choices is not null) && waitForConfirmation is not null && await waitForConfirmation(instance, plan);

            if (run.InstallStop.IsRequested)
            {
                if (row is IUpdateRow update)
                    update.Changelogs = [];
                stopped = StoppedText(row);
            }
            else if (choices is not null)
            {
                row.Choices = choices;
                HoldPlan(row, plan);
                if (row is not ContentItem)
                    ReplanOnChange(row, choices, replanned => HoldPlan(row, replanned));
                await ShowConflictsAsync(plan, again);
            }
            else if (!plan.IsReady)
            {
                row.InstallError = Describe(plan.Conflicts.Concat(plan.UnresolvedChoices));
                await ShowConflictsAsync(plan, again);
            }
            else if ((plan.Warnings.Count > 0 && !IsEmptyUpdateAll(row, plan)) || wait)
            {
                HoldPlan(row, plan);
            }
            else
            {
                executed = true;
                run.TaskItem.MarkRunning(plan);
                await services.PlanExecutor.ExecuteAsync(plan, enable: true, ProgressOf(row), run.InstallStop);
                completed = true;
                LeaveInstallStepsNotice(row, plan);
            }
        }
        catch (InstallStoppedException exception)
        {
            run.TaskItem.StoppedAfter = (exception.Completed, exception.Total);
            stopped = StoppedText(row, exception.Completed, exception.Total);
        }
        catch (OperationCanceledException) when (!executed && run.InstallStop.IsRequested)
        {
            // the plan writes nothing, so a stop during it ends the run the way a stop before the first mod does
            if (row is IUpdateRow update)
                update.Changelogs = [];
            stopped = StoppedText(row);
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            row.InstallError = InstallFailureText(exception);
        }
        finally
        {
            EndInstallRun(run, completed, stopped is not null, row.InstallError);
            EndRowInstall(row, stopped);
        }

        return executed;
    }

    /// <summary>"Update all" that changes no mod has nothing to confirm, also when the planner warns about a release that the instance keeps.</summary>
    private static bool IsEmptyUpdateAll(IInstallRow row, InstallPlan plan) => row is UpdateAllItem && plan.Operations.Count == 0;

    private InstallPlanningRequest PlanningRequest(BoreaServices services, Instance instance, IReadOnlyList<RequestedMod> requested)
        => new(instance, requested, services.Mods, PlanningGameVersion(services, instance), services.GamePlatform.Current);

    /// <summary>
    /// The build that a display for <paramref name="instance"/> compares with, or the build of the settings without an instance.
    /// A kept build that is missing or reads another version gives null, which reads as unknown, so the build of the
    /// settings never stands in for it.
    /// </summary>
    internal static GameVersion? GameVersionOf(BoreaServices services, Instance? instance)
        => GameVersionOf(services, instance is null ? null : services.InstanceGames.Resolve(instance));

    /// <summary>The same build as the overload for an instance, from a game that the caller already resolved.</summary>
    internal static GameVersion? GameVersionOf(BoreaServices services, InstanceGame? game)
        => game is null ? services.InstalledVersion.GetInstalledVersion()?.Version : game.Version?.Version;

    /// <summary>
    /// The build that a plan for <paramref name="instance"/> is checked against. A kept build that is missing or reads
    /// another version stops the plan with a message that names the build, because against an unknown game the planner
    /// would pick releases that the kept build cannot load.
    /// </summary>
    private GameVersion? PlanningGameVersion(BoreaServices services, Instance instance)
    {
        var game = services.InstanceGames.Resolve(instance);
        if (game is { Problem: not KeptBuildProblem.None, KeptBuild: { } build })
            throw new InvalidOperationException(Localization.FormatInstallKeptBuildUnavailable(build.ToString(), KeptBuildFolderText(services.Paths, build)));

        return game.Version?.Version;
    }

    /// <summary>
    /// Plans with the user's choices and selects every recommendation the user has not seen yet,
    /// except one that blocks the plan, which starts deselected.
    /// </summary>
    private static async Task<InstallPlan> PlanWithChoicesAsync(BoreaServices services, InstallPlanningRequest request, InstallChoices? choices, CancellationToken cancellationToken = default)
    {
        var kept = choices?.SelectedRecommendations ?? new HashSet<string>();
        var deselected = new HashSet<string>(choices?.DeselectedRecommendations ?? new HashSet<string>(), StringComparer.Ordinal);
        request = request with { Alternatives = choices?.SelectedAlternatives };
        var plan = await PlanWithRecommendationsAsync(services, request, kept, deselected, cancellationToken);
        if (plan.IsReady)
            return plan;

        var blocking = plan.Choices
            .Where(choice => choice.Kind == PlanningChoiceKind.Recommendation && choice.Selected == "include" && !kept.Contains(choice.Key) && plan.Conflicts.Any(conflict => Blocks(conflict, choice)))
            .Select(choice => choice.Key)
            .ToList();
        if (blocking.Count == 0)
            return plan;

        deselected.UnionWith(blocking);
        var without = await PlanWithRecommendationsAsync(services, request, kept, deselected, cancellationToken);
        return without.Conflicts.Count < plan.Conflicts.Count ? without : plan;
    }

    /// <summary>Repeats the plan until it names no recommendation that is neither selected nor deselected.</summary>
    private static async Task<InstallPlan> PlanWithRecommendationsAsync(BoreaServices services, InstallPlanningRequest request, IReadOnlySet<string> kept, IReadOnlySet<string> deselected, CancellationToken cancellationToken)
    {
        var recommended = new HashSet<string>(kept, StringComparer.Ordinal);
        request = request with { Recommended = recommended };
        while (true)
        {
            var plan = await services.InstallPlanner.PlanAsync(request, cancellationToken);
            var added = false;
            foreach (var choice in plan.Choices.Where(choice => choice.Kind == PlanningChoiceKind.Recommendation && !deselected.Contains(choice.Key)))
                added |= recommended.Add(choice.Key);

            if (!added)
                return plan;
        }
    }

    private static bool Blocks(PlanningMessage conflict, PlanningChoice recommendation)
        => ReferenceEquals(conflict.Dependency, recommendation.Dependency)
            || (recommendation.Dependency.IsAnyOf ? recommendation.Dependency.AnyOf.Select(value => value.ModId) : [recommendation.Dependency.ModId!]).Contains(conflict.ModId, ModIds.Comparer);

    private InstallChoices NewChoices(Guid instanceId, IReadOnlyList<RequestedMod> requested, InstallPlan plan)
    {
        var choices = new InstallChoices(instanceId, requested, ContentName);
        choices.Apply(plan);
        return choices;
    }

    internal string ContentName(string modId)
        => _listings.FirstOrDefault(item => ModIds.Equals(item.ModId, modId))?.Name ?? modId;

    /// <summary>
    /// "Add" or "Install anyway", with the download size of the plan when the
    /// index states one. A row that changes the version of an installed mod
    /// names the version it replaces instead.
    /// </summary>
    internal string ConfirmInstallText(string? warning, InstallPlan? plan, string? replacedVersion = null)
    {
        var text = (warning, replacedVersion) switch
        {
            (null, null) => Localization.ContentAdd,
            (null, { } replaced) => Localization.FormatContentReplaceVersion(replaced),
            (_, null) => Localization.InstallAnyway,
            (_, { } replaced) => Localization.FormatContentReplaceVersionAnyway(replaced),
        };
        return PlanSizeText(plan) is { } size ? $"{text} ({size})" : text;
    }

    /// <summary>What a plan downloads, summed over its operations, or null when no release states a size.</summary>
    internal static string? PlanSizeText(InstallPlan? plan)
    {
        if (plan is null)
            return null;

        long total = 0;
        var known = false;
        foreach (var operation in plan.Operations)
        {
            if (operation.Release.Download.SizeBytes is { } size)
            {
                total += size;
                known = true;
            }
        }

        return known ? SizeText(total) : null;
    }

    /// <summary>
    /// "Also adds KSP-Redux 1.3.2" for what the plan installs besides the requested mods,
    /// without the mods that the choices show, or null when it installs nothing else.
    /// </summary>
    /// <param name="plan">The plan that waits for a confirmation.</param>
    /// <param name="choices">The choices that the row shows, whose mods are named there already.</param>
    /// <param name="all">Names every mod. Otherwise a long list ends in "and 2 more".</param>
    /// <param name="ownModId">The mod that the row itself changes, which the plan does not add.</param>
    internal string? AddedModsText(InstallPlan? plan, InstallChoices? choices, bool all = false, string? ownModId = null)
    {
        var names = AddedMods(plan, choices)
            .Where(release => ownModId is null || !ModIds.Equals(release.ModId, ownModId))
            .Select(release => $"{ContentName(release.ModId)} {release.Version}")
            .ToList();
        if (names.Count == 0)
            return null;

        return all || names.Count <= AddedModsShown
            ? Localization.FormatInstallAlsoAdds(string.Join(", ", names))
            : Localization.FormatInstallAlsoAddsMore(string.Join(", ", names.Take(AddedModsShown - 1)), names.Count - AddedModsShown + 1);
    }

    private static IEnumerable<ModVersionMetadata> AddedMods(InstallPlan? plan, InstallChoices? choices)
        => plan?.Operations
            .Where(operation => operation.Reason != InstallReason.Manual && choices?.NamedModIds.Contains(operation.Release.ModId) != true)
            .Select(operation => operation.Release) ?? [];

    private void HoldPlan(IInstallRow row, InstallPlan plan)
    {
        row.PendingPlan = plan;
        row.InstallWarning = plan.Warnings.Count > 0 ? Describe(plan.Warnings) : null;
        if (row.Choices is { } choices)
            choices.BlockedText = BlockedText(plan);
    }

    /// <summary>What stops a plan whose choices are shown, without the open alternatives that the choices already show.</summary>
    private string? BlockedText(InstallPlan plan)
    {
        var messages = plan.Conflicts.Concat(plan.UnresolvedChoices.Where(message => message.Kind != PlanningMessageKind.AlternativeChoice)).ToList();
        return messages.Count > 0 ? Describe(messages) : null;
    }

    /// <summary>Where a plan after a change of the choices finds its releases.</summary>
    internal Func<BoreaServices, IModRepository> ChoicePlanMods { get; set; } = services => services.OfflineMods;

    /// <summary>Plans again after each change of the choices, so that the confirm button shows the size of the plan that Confirm runs.</summary>
    private void ReplanOnChange(IPlanRow row, InstallChoices choices, Action<InstallPlan> hold)
    {
        choices.Replan = () =>
        {
            choices.ShownPlan = row.PendingPlan ?? choices.ShownPlan;
            row.PendingPlan = null;
            choices.BlockedText = null;
            if (choices.Planning.IsCompleted)
                choices.Planning = ReplanChoicesAsync(row, choices, hold);
        };
    }

    /// <summary>Plans one change at a time and drops a plan that a later change made outdated. A plan that fails or cannot run is left to Confirm.</summary>
    private async Task ReplanChoicesAsync(IPlanRow row, InstallChoices choices, Action<InstallPlan> hold)
    {
        try
        {
            while (IsShown())
            {
                var revision = choices.Revision;
                var plan = await TryPlanAsync(row, choices);
                if (revision != choices.Revision)
                    continue;

                if (plan is { IsReady: true } && IsShown())
                    hold(plan);
                return;
            }
        }
        catch (Exception exception)
        {
            _services?.Log.Write("The plan after a change of the install choices failed.", exception);
        }

        bool IsShown() => ReferenceEquals(row.Choices, choices) && !row.IsInstalling;
    }

    /// <summary>Lets the plan of an earlier change end before Confirm plans, so that no plan outlives the install.</summary>
    private static async Task WhenPlanningEndedAsync(InstallChoices choices, CancellationToken cancellationToken = default)
        => await choices.Planning.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

    /// <summary>Plans again after Confirm when the user changed a choice while Confirm planned.</summary>
    private static void ReplanIfChanged(IPlanRow row, int? revision)
    {
        if (row.Choices is { } choices && choices.Revision != revision)
            choices.Replan?.Invoke();
    }

    /// <summary>Plans from the index that Borea holds, or returns null when that fails.</summary>
    private async Task<InstallPlan?> TryPlanAsync(IPlanRow row, InstallChoices choices)
    {
        if (_services is not { } services)
            return null;

        try
        {
            var instance = choices.NewInstance ?? await services.Instances.GetByIdAsync(choices.InstanceId);
            if (instance is null)
                return null;

            var request = PlanningRequest(services, instance, choices.Requested);
            try
            {
                return await PlanWithChoicesAsync(services, request with { Repository = ChoicePlanMods(services) }, choices);
            }
            catch (NotSupportedException) when (row is UpdateAllItem)
            {
                // the list and the count of this row state the plan, and a mod from SpaceDock can only be read with a request
                return await PlanWithChoicesAsync(services, request, choices);
            }
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            return null;
        }
    }

    /// <summary>
    /// Runs the plan the row holds after the user confirmed it. The executor
    /// refuses it when the instance changed since it was planned.
    /// </summary>
    internal async Task ConfirmInstallAsync(IInstallRow row)
    {
        if (row is IContentPlanRow { PendingContentPlan: not null } content)
        {
            await ConfirmContentAsync(content);
            return;
        }

        if (await ExecutePendingPlanAsync(row))
            await ReloadInstancesAsync();
    }

    /// <param name="starting">Runs right before the executor starts.</param>
    private async Task<bool> ExecutePendingPlanAsync(IInstallRow row, Action? starting = null)
    {
        if (_services is null || row.IsInstalling || (row.PendingPlan is null && row.Choices is null))
            return false;

        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            if (row.Choices is { } shown)
                shown.BlockedText = Localization.LibraryFolderBusy;
            else
                row.InstallError = Localization.LibraryFolderBusy;
            return false;
        }

        var services = _services;
        var plan = row.PendingPlan;
        row.InstallError = null;
        row.IsInstalling = true;
        var run = row.Run = StartInstallRun(StartInstallTask(row, plan?.InstanceId ?? row.Choices?.InstanceId));
        var executed = false;
        var completed = false;
        string? stopped = null;
        string? error = null;
        int? revision = null;
        try
        {
            if (row.Choices is { } choices)
            {
                await WhenPlanningEndedAsync(choices, run.InstallStop.Token);
                revision = choices.Revision;
                plan = await ReplanAsync(services, row, choices, revision.Value, run.InstallStop.Token);
                if (plan is null)
                    return false;
            }

            row.PendingPlan = null;
            row.InstallWarning = null;
            row.Choices = null;
            starting?.Invoke();
            executed = true;
            run.TaskItem.MarkRunning(plan!);
            await services.PlanExecutor.ExecuteAsync(plan!, enable: true, ProgressOf(row), run.InstallStop);
            completed = true;
            LeaveInstallStepsNotice(row, plan!);
        }
        catch (InstallStoppedException exception)
        {
            run.TaskItem.StoppedAfter = (exception.Completed, exception.Total);
            stopped = StoppedText(row, exception.Completed, exception.Total);
        }
        catch (OperationCanceledException) when (!executed && run.InstallStop.IsRequested)
        {
            // the plan writes nothing, so a stop during it ends the run the way a stop before the first mod does
            CancelInstall(row);
            if (row is IUpdateRow update)
                update.Changelogs = [];
            stopped = StoppedText(row);
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            error = InstallFailureText(exception);
            if (row.Choices is { } choices)
                choices.BlockedText = error;
            else
                row.InstallError = error;
        }
        finally
        {
            EndInstallRun(run, completed, stopped is not null, error);
            EndRowInstall(row, stopped);
            if (error is null)
                ReplanIfChanged(row, revision);
        }

        return executed;
    }

    /// <summary>
    /// Plans again with the user's choices, or returns null and keeps the row waiting when a choice changed meanwhile,
    /// or when that plan asks something new, cannot run, has a new warning, or installs a mod that neither the row nor the choices named.
    /// </summary>
    private async Task<InstallPlan?> ReplanAsync(BoreaServices services, IInstallRow row, InstallChoices choices, int revision, CancellationToken cancellationToken)
    {
        var seen = row.PendingPlan ?? choices.ShownPlan;
        var shown = seen?.Warnings ?? [];
        var named = (seen?.Operations.Select(operation => operation.Release.ModId) ?? [])
            .Concat(choices.Requested.Select(mod => mod.Release.ModId))
            .Concat(choices.NamedModIds)
            .ToHashSet(ModIds.Comparer);
        var instance = await services.Instances.GetByIdAsync(choices.InstanceId)
            ?? throw new InvalidOperationException(Localization.InstallInstanceMissing);
        var plan = await PlanWithChoicesAsync(services, PlanningRequest(services, instance, choices.Requested), choices, cancellationToken);
        if (revision != choices.Revision)
            return null;

        if (!choices.Apply(plan) && plan.IsReady && WereShown(plan.Warnings, shown)
            && (row is IUpdateRow || plan.Operations.All(operation => named.Contains(operation.Release.ModId))))
            return plan;

        HoldPlan(row, plan);
        return null;
    }

    /// <summary>Whether the player saw each of <paramref name="warnings"/> in <paramref name="shown"/>.</summary>
    internal static bool WereShown(IEnumerable<PlanningMessage> warnings, IReadOnlyList<PlanningMessage> shown)
        => warnings.All(warning => shown.Any(warning.SameAs));

    internal static void CancelInstall(IInstallRow row)
    {
        if (row is IContentPlanRow content)
            content.PendingContentPlan = null;
        row.PendingPlan = null;
        row.InstallWarning = null;
        row.InstallError = null;
        row.Choices = null;
    }

    /// <summary>
    /// Reports land on the UI thread through <see cref="Progress{T}"/>, and
    /// each install gets its own text so its download rate starts fresh.
    /// </summary>
    private IProgress<InstallProgress> ProgressOf(IInstallProgressRow row)
    {
        var text = new InstallProgressText(Localization);
        InstallProgress? paused = null;
        void Show(InstallProgress value)
        {
            if (!row.IsInstalling)
                return;

            text.Report(value);
            paused = text.IsPaused ? value : null;
            row.Progress = text.Percent;
            row.ProgressStatus = text.Status;
            row.ProgressDetail = text.Detail;
            if (row.Run is { } run)
            {
                run.Report(value.Phase);
                run.TaskItem.Report(text);
            }
        }

        if (row.Run is not { } started)
            return new Progress<InstallProgress>(Show);

        started.RepeatPausedReport = () => { if (paused is { } value) started.ShowReport(() => Show(value)); };
        return new Progress<InstallProgress>(value => started.ShowReport(() => Show(value)));
    }

    /// <summary>
    /// What a failed install, update or handover says. A failure whose recovery
    /// failed too also names the folder that holds the previous files, which
    /// the message of the exception leaves out.
    /// </summary>
    private string InstallFailureText(Exception exception) => exception switch
    {
        ModReplacementRecoveryException recovery => $"{recovery.Message} {Localization.FormatInstallRecoveryFolder(recovery.RecoveryDirectory)}",
        PlannedInstanceChangedException => Localization.ManualInstallsInstanceChanged,
        _ => exception.Message,
    };

    private static bool IsInstallFailure(Exception exception)
        => exception is HttpRequestException or IOException or InvalidOperationException or UnauthorizedAccessException
            or DownloadFailedException or NotSupportedException or TaskCanceledException or ModReplacementRecoveryException;

    /// <summary>
    /// The messages after the name of their mod. A message about a recorded vehicle or save comes first and names the mod
    /// itself, so it stays visible where a row shows only the start of the text. A stopped search belongs to no mod, so it has no name.
    /// </summary>
    private string Describe(IEnumerable<PlanningMessage> messages)
        => string.Join(" ", messages
            .OrderBy(message => !BreaksContent(message))
            .Select(message => BreaksContent(message) || message.Kind == PlanningMessageKind.SearchLimit ? PlanningText.Message(message) : $"{ContentName(message.ModId)}: {PlanningText.Message(message)}"));

    private static bool BreaksContent(PlanningMessage message) => message.Kind is PlanningMessageKind.BreaksVehicle or PlanningMessageKind.BreaksSave;
}
