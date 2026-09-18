using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
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

/// <summary>
/// A row that installs a release: a Discover row, a row of the versions table,
/// or an update on the instance page.
/// </summary>
internal interface IInstallRow : IInstallProgressRow
{
    string? InstallError { get; set; }

    string? InstallWarning { get; set; }

    InstallPlan? PendingPlan { get; set; }

    /// <summary>What <see cref="PendingPlan"/> asks the user before it runs, or null.</summary>
    InstallChoices? Choices { get; set; }
}

/// <summary>
/// Installs go through the planner and the executor the CLI uses. The planner
/// adds required dependencies and blocks an incompatible release, and the
/// executor writes only while the instance still matches the plan.
/// </summary>
public partial class MainViewModel
{
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

    private InstallRun StartInstallRun()
    {
        var run = new InstallRun(Localization);
        _installRuns.Add(run);
        if (IsClosing)
            run.Stop();
        return run;
    }

    private void EndInstallRun(InstallRun run)
    {
        _installRuns.Remove(run);
        run.End();
    }

    /// <summary>What a stopped install shows where its progress was.</summary>
    private string StoppedText(IInstallProgressRow row, int completed = 0, int total = 0) => row is IUpdateRow
        ? completed == 0 ? Localization.UpdateStopped : Localization.FormatUpdateStoppedAfter(completed, total)
        : completed == 0 ? Localization.InstallStopped : Localization.FormatInstallStoppedAfter(completed, total);

    /// <summary>
    /// Plans the install of one release into the active instance. A ready plan
    /// without warnings or choices runs at once. Any other plan waits on the row
    /// until the user confirms or cancels it.
    /// </summary>
    internal async Task PlanInstallAsync(IInstallRow row, Func<Task<ModVersionMetadata?>> findRelease, bool exact)
    {
        if (_services is null || ActiveInstance is null)
            return;

        var executed = await PlanAndExecuteAsync(row, ActiveInstance.InstanceId, async _ =>
        {
            var release = await findRelease() ?? throw new InvalidOperationException(Localization.DiscoverNoRelease);
            return [new RequestedMod(release, InstallReason.Manual, exact)];
        });

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

        var services = _services;
        row.InstallError = null;
        row.InstallWarning = null;
        row.PendingPlan = null;
        row.Choices = null;
        row.ProgressStatus = null;
        row.IsInstalling = true;
        var run = row.Run = StartInstallRun();
        var executed = false;
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
                await services.PlanExecutor.ExecuteAsync(plan, enable: true, ProgressOf(row), run.InstallStop);
            }
        }
        catch (InstallStoppedException exception)
        {
            stopped = StoppedText(row, exception.Completed, exception.Total);
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            row.InstallError = exception.Message;
        }
        finally
        {
            EndInstallRun(run);
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

        var services = _services;
        var plan = row.PendingPlan;
        row.IsInstalling = true;
        var run = row.Run = StartInstallRun();
        var executed = false;
        string? stopped = null;
        try
        {
            if (row.Choices is { } choices)
            {
                plan = await ReplanAsync(services, row, choices);
                if (plan is null)
                    return false;
            }

            row.PendingPlan = null;
            row.InstallWarning = null;
            row.Choices = null;
            starting?.Invoke();
            executed = true;
            await services.PlanExecutor.ExecuteAsync(plan!, enable: true, ProgressOf(row), run.InstallStop);
        }
        catch (InstallStoppedException exception)
        {
            stopped = StoppedText(row, exception.Completed, exception.Total);
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            if (row.Choices is { } choices)
                choices.BlockedText = exception.Message;
            else
                row.InstallError = exception.Message;
        }
        finally
        {
            EndInstallRun(run);
            row.IsInstalling = false;
            row.Run = null;
            row.Progress = 0;
            row.ProgressStatus = stopped;
            row.ProgressDetail = null;
        }

        return executed;
    }

    /// <summary>Plans again with the user's choices, or returns null and keeps the row waiting when that plan asks something new, cannot run, or has a new warning.</summary>
    private async Task<InstallPlan?> ReplanAsync(BoreaServices services, IInstallRow row, InstallChoices choices)
    {
        var shown = row.PendingPlan?.Warnings ?? [];
        var instance = await services.Instances.GetByIdAsync(choices.InstanceId)
            ?? throw new InvalidOperationException(Localization.InstallInstanceMissing);
        var plan = await PlanWithChoicesAsync(services, PlanningRequest(services, instance, choices.Requested), choices);
        if (!choices.Apply(plan) && plan.IsReady && plan.Warnings.All(shown.Contains))
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
        return new Progress<InstallProgress>(value =>
        {
            if (!row.IsInstalling)
                return;

            text.Report(value);
            row.Progress = text.Percent;
            row.ProgressStatus = text.Status;
            row.ProgressDetail = text.Detail;
            if (row.Run is { } run)
                run.IsFinishingMod = value.Phase != InstallPhase.Downloading;
        });
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
