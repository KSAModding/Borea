using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.History;
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

    /// <summary>What a stopped install shows where its progress was.</summary>
    private string StoppedText(IInstallProgressRow row, int completed = 0, int total = 0) => row is IUpdateRow
        ? completed == 0 ? Localization.UpdateStopped : Localization.FormatUpdateStoppedAfter(completed, total)
        : completed == 0 ? Localization.InstallStopped : Localization.FormatInstallStoppedAfter(completed, total);

    /// <summary>
    /// Plans the install of one release into the active instance. A ready plan
    /// that installs only that release and has no warnings or choices runs at
    /// once. Any other plan waits on the row until the user confirms or cancels it.
    /// </summary>
    /// <param name="exactVersion">The version the request pins. Null plans the newest release.</param>
    /// <param name="instanceId">The instance a Try again of the Tasks page installs into. Null installs into the active instance.</param>
    /// <param name="confirm">Holds even a plan without warnings or choices, for an install that a borea:// link asked for.</param>
    internal async Task PlanInstallAsync(IInstallRow row, Func<Task<ModVersionMetadata?>> findRelease, ModVersion? exactVersion, Guid? instanceId = null, bool confirm = false)
    {
        if (_services is null || row.IsInstalling || (instanceId ?? ActiveInstance?.InstanceId) is not { } target)
            return;

        RememberRequestedVersion(row, exactVersion);
        var executed = await PlanAndExecuteAsync(
            row,
            target,
            async _ =>
            {
                var release = await findRelease() ?? throw new InvalidOperationException(Localization.DiscoverNoRelease);
                return [new RequestedMod(release, InstallReason.Manual, exactVersion is not null)];
            },
            (_, plan) => Task.FromResult(confirm || AddedMods(plan, null).Any()));

        if (executed)
            await ReloadInstancesAsync();
    }

    /// <summary>
    /// Plans the requested mods into the instance and runs a ready plan without
    /// warnings or choices, unless <paramref name="waitForConfirmation"/> holds it.
    /// Returns whether the executor ran, so the caller reloads the instances.
    /// </summary>
    private async Task<bool> PlanAndExecuteAsync(IInstallRow row, Guid instanceId, Func<Instance, Task<IReadOnlyList<RequestedMod>>> requestMods, Func<Instance, InstallPlan, Task<bool>>? waitForConfirmation = null)
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
            var plan = await PlanWithChoicesAsync(services, PlanningRequest(services, instance, requested), null);
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
                if (row is not IUpdateRow)
                    ReplanOnChange(row, choices, replanned => HoldPlan(row, replanned));
            }
            else if (!plan.IsReady)
            {
                row.InstallError = Describe(plan.Conflicts.Concat(plan.UnresolvedChoices));
            }
            else if (plan.Warnings.Count > 0 || wait)
            {
                HoldPlan(row, plan);
            }
            else
            {
                executed = true;
                run.TaskItem.MarkRunning(plan);
                await services.PlanExecutor.ExecuteAsync(plan, enable: true, ProgressOf(row), run.InstallStop);
                completed = true;
            }
        }
        catch (InstallStoppedException exception)
        {
            run.TaskItem.StoppedAfter = (exception.Completed, exception.Total);
            stopped = StoppedText(row, exception.Completed, exception.Total);
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            row.InstallError = exception.Message;
        }
        finally
        {
            EndInstallRun(run, completed, stopped is not null, row.InstallError);
            row.IsInstalling = false;
            row.Run = null;
            row.Progress = 0;
            row.ProgressStatus = stopped;
            row.ProgressDetail = null;
        }

        return executed;
    }

    private static InstallPlanningRequest PlanningRequest(BoreaServices services, Instance instance, IReadOnlyList<RequestedMod> requested)
        => new(instance, requested, services.Mods, services.InstalledVersion.GetInstalledVersion()?.Version, CurrentPlatform());

    /// <summary>
    /// Plans with the user's choices and selects every recommendation the user has not seen yet,
    /// except one that blocks the plan, which starts deselected.
    /// </summary>
    private static async Task<InstallPlan> PlanWithChoicesAsync(BoreaServices services, InstallPlanningRequest request, InstallChoices? choices)
    {
        var kept = choices?.SelectedRecommendations ?? new HashSet<string>();
        var deselected = new HashSet<string>(choices?.DeselectedRecommendations ?? new HashSet<string>(), StringComparer.Ordinal);
        request = request with { Alternatives = choices?.SelectedAlternatives };
        var plan = await PlanWithRecommendationsAsync(services, request, kept, deselected);
        if (plan.IsReady)
            return plan;

        var blocking = plan.Choices
            .Where(choice => choice.Kind == PlanningChoiceKind.Recommendation && choice.Selected == "include" && !kept.Contains(choice.Key) && plan.Conflicts.Any(conflict => Blocks(conflict, choice)))
            .Select(choice => choice.Key)
            .ToList();
        if (blocking.Count == 0)
            return plan;

        deselected.UnionWith(blocking);
        var without = await PlanWithRecommendationsAsync(services, request, kept, deselected);
        return without.Conflicts.Count < plan.Conflicts.Count ? without : plan;
    }

    /// <summary>Repeats the plan until it names no recommendation that is neither selected nor deselected.</summary>
    private static async Task<InstallPlan> PlanWithRecommendationsAsync(BoreaServices services, InstallPlanningRequest request, IReadOnlySet<string> kept, IReadOnlySet<string> deselected)
    {
        var recommended = new HashSet<string>(kept, StringComparer.Ordinal);
        request = request with { Recommended = recommended };
        while (true)
        {
            var plan = await services.InstallPlanner.PlanAsync(request);
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

    private string ContentName(string modId)
        => _listings.FirstOrDefault(item => ModIds.Equals(item.ModId, modId))?.Name ?? modId;

    /// <summary>"Add" or "Install anyway", with the download size of the plan when the index states one.</summary>
    internal string ConfirmInstallText(string? warning, InstallPlan? plan)
    {
        var text = warning is null ? Localization.ContentAdd : Localization.InstallAnyway;
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
    /// <param name="all">Names every mod. Otherwise a long list ends in "and 2 more".</param>
    internal string? AddedModsText(InstallPlan? plan, InstallChoices? choices, bool all = false)
    {
        var names = AddedMods(plan, choices).Select(release => $"{ContentName(release.ModId)} {release.Version}").ToList();
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
    private static string? BlockedText(InstallPlan plan)
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
                var plan = await TryPlanAsync(choices);
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
    private static async Task WhenPlanningEndedAsync(InstallChoices choices)
        => await choices.Planning.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

    /// <summary>Plans again after Confirm when the user changed a choice while Confirm planned.</summary>
    private static void ReplanIfChanged(IPlanRow row, int? revision)
    {
        if (row.Choices is { } choices && choices.Revision != revision)
            choices.Replan?.Invoke();
    }

    /// <summary>Plans from the index that Borea holds, or returns null when that fails.</summary>
    private async Task<InstallPlan?> TryPlanAsync(InstallChoices choices)
    {
        if (_services is not { } services)
            return null;

        try
        {
            var instance = choices.NewInstance ?? await services.Instances.GetByIdAsync(choices.InstanceId);
            return instance is null
                ? null
                : await PlanWithChoicesAsync(services, PlanningRequest(services, instance, choices.Requested) with { Repository = ChoicePlanMods(services) }, choices);
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
                await WhenPlanningEndedAsync(choices);
                revision = choices.Revision;
                plan = await ReplanAsync(services, row, choices, revision.Value);
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
        }
        catch (InstallStoppedException exception)
        {
            run.TaskItem.StoppedAfter = (exception.Completed, exception.Total);
            stopped = StoppedText(row, exception.Completed, exception.Total);
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            error = exception.Message;
            if (row.Choices is { } choices)
                choices.BlockedText = error;
            else
                row.InstallError = error;
        }
        finally
        {
            EndInstallRun(run, completed, stopped is not null, error);
            row.IsInstalling = false;
            row.Run = null;
            row.Progress = 0;
            row.ProgressStatus = stopped;
            row.ProgressDetail = null;
            if (error is null)
                ReplanIfChanged(row, revision);
        }

        return executed;
    }

    /// <summary>
    /// Plans again with the user's choices, or returns null and keeps the row waiting when a choice changed meanwhile,
    /// or when that plan asks something new, cannot run, has a new warning, or installs a mod that neither the row nor the choices named.
    /// </summary>
    private async Task<InstallPlan?> ReplanAsync(BoreaServices services, IInstallRow row, InstallChoices choices, int revision)
    {
        var seen = row.PendingPlan ?? choices.ShownPlan;
        var shown = seen?.Warnings ?? [];
        var named = (seen?.Operations.Select(operation => operation.Release.ModId) ?? [])
            .Concat(choices.Requested.Select(mod => mod.Release.ModId))
            .Concat(choices.NamedModIds)
            .ToHashSet(ModIds.Comparer);
        var instance = await services.Instances.GetByIdAsync(choices.InstanceId)
            ?? throw new InvalidOperationException(Localization.InstallInstanceMissing);
        var plan = await PlanWithChoicesAsync(services, PlanningRequest(services, instance, choices.Requested), choices);
        if (revision != choices.Revision)
            return null;

        if (!choices.Apply(plan) && plan.IsReady && plan.Warnings.All(shown.Contains)
            && (row is IUpdateRow || plan.Operations.All(operation => named.Contains(operation.Release.ModId))))
            return plan;

        HoldPlan(row, plan);
        return null;
    }

    internal static void CancelInstall(IInstallRow row)
    {
        row.PendingPlan = null;
        row.InstallWarning = null;
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

    private static bool IsInstallFailure(Exception exception)
        => exception is HttpRequestException or IOException or InvalidOperationException or UnauthorizedAccessException
            or DownloadFailedException or NotSupportedException or TaskCanceledException or ModReplacementRecoveryException;

    /// <summary>
    /// The planner's messages on one line, each named by its mod.
    /// </summary>
    private static string Describe(IEnumerable<PlanningMessage> messages)
        => string.Join(" ", messages.Select(message => $"{message.ModId}: {PlanningText.Message(message)}"));

    private static OsPlatform CurrentPlatform()
        => OperatingSystem.IsWindows() ? OsPlatform.Windows : OperatingSystem.IsLinux() ? OsPlatform.Linux : OsPlatform.MacOs;
}
