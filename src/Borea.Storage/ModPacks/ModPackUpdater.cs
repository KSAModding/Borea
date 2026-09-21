using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Storage.ModPacks;

public sealed class ModPackUpdater : IModPackUpdater
{
    private const string StoppedMessage = "The update was stopped.";
    private const string EarlierStepFailedMessage = "An earlier step failed.";

    private readonly IInstanceRepository _instances;
    private readonly IInstallPlanner _planner;
    private readonly IInstallPlanExecutor _executor;
    private readonly IModUninstaller _uninstaller;

    public ModPackUpdater(IInstanceRepository instances, IInstallPlanner planner, IInstallPlanExecutor executor, IModUninstaller uninstaller)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _uninstaller = uninstaller ?? throw new ArgumentNullException(nameof(uninstaller));
    }

    public async Task<ModPackUpdateResult> PlanAsync(ModPackUpdateRequest request, CancellationToken cancellationToken = default)
        => (await PlanCoreAsync(request, stop: null, cancellationToken).ConfigureAwait(false)).Result;

    public async Task<ModPackUpdateResult> UpdateAsync(ModPackUpdateRequest request, IProgress<InstallProgress>? progress = null, InstallStop? stop = null, CancellationToken cancellationToken = default)
    {
        var (instance, planned) = await PlanCoreAsync(request, stop, cancellationToken).ConfigureAwait(false);
        if (!planned.CanRun || planned.IsStopped)
            return planned;

        var plan = planned.Plan!;
        var draft = ModPackUpdates.Draft(instance, planned.Changes);
        var removals = planned.Changes.Where(change => change.Kind == ModPackChangeKind.Remove).ToList();
        var removed = new HashSet<string>(ModIds.Comparer);
        var stopped = stop is { IsRequested: true };
        string? failure = null;

        if (!stopped)
        {
            try
            {
                var current = await _instances.GetByIdAsync(instance.InstanceId).ConfigureAwait(false);
                if (current is null || !InstallPlanningState.Capture(instance).Matches(current))
                    throw new InvalidOperationException("The instance changed after planning.");

                foreach (var removal in removals)
                {
                    if (stop is { IsRequested: true })
                    {
                        stopped = true;
                        break;
                    }

                    await _uninstaller.UninstallAsync(instance.InstanceId, removal.ModId, cancellationToken).ConfigureAwait(false);
                    removed.Add(removal.ModId);
                }

                if (!stopped)
                    await _executor.ExecuteAsync(plan, request.Enable, progress, stop, cancellationToken).ConfigureAwait(false);
            }
            catch (InstallStoppedException)
            {
                stopped = true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failure = exception.Message;
            }
        }

        var fresh = await _instances.GetByIdAsync(instance.InstanceId).ConfigureAwait(false) ?? instance;
        var members = new List<ModPackMemberResult>();
        var failureShown = false;
        ModPackMemberResult Outcome(string modId, ModVersion version, InstallReason reason, bool done, ModPackMemberStatus doneStatus)
        {
            if (done)
                return new ModPackMemberResult(modId, version, reason, doneStatus);
            if (failure is not null && !failureShown)
            {
                failureShown = true;
                return new ModPackMemberResult(modId, version, reason, ModPackMemberStatus.Failed, failure);
            }

            return new ModPackMemberResult(modId, version, reason, ModPackMemberStatus.NotAttempted, stopped ? StoppedMessage : EarlierStepFailedMessage);
        }

        foreach (var removal in removals)
            members.Add(Outcome(removal.ModId, removal.From!.Value, InstallReason.ModPack, removed.Contains(removal.ModId), ModPackMemberStatus.Removed));
        foreach (var operation in plan.Operations)
        {
            var done = fresh.Mods.Any(mod => ModIds.Equals(mod.ModId, operation.Release.ModId) && mod.Version == operation.Release.Version);
            var existing = draft.Mods.Any(mod => ModIds.Equals(mod.ModId, operation.Release.ModId));
            members.Add(Outcome(operation.Release.ModId, operation.Release.Version, ReasonAfter(planned.Changes, draft, operation.Release.ModId, operation.Reason), done, existing ? ModPackMemberStatus.Replaced : ModPackMemberStatus.Installed));
        }

        members.AddRange(AlreadyInstalled(planned.Changes, draft, plan));
        var complete = failure is null && !stopped && members.All(member => IsDone(member.Status));
        if (complete)
        {
            var target = planned.Target;
            var kept = planned.Changes.Where(change => change.Kind == ModPackChangeKind.Keep).ToList();
            await _instances.UpdateAsync(instance.InstanceId, current =>
            {
                foreach (var mod in current.Mods.Where(mod => kept.Any(change => ModIds.Equals(change.ModId, mod.ModId))))
                    mod.MarkAsManuallyInstalled();
                current.ChangeSource(new InstanceSource.FromModPack(target.ModPackId, target.Version));
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        return new ModPackUpdateResult(instance.InstanceId, planned.CurrentVersion, planned.Target, planned.Changes, plan, Ordered(members), planned.Warnings, complete, stopped);
    }

    private async Task<(Instance Instance, ModPackUpdateResult Result)> PlanCoreAsync(ModPackUpdateRequest request, InstallStop? stop, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Pack);
        ArgumentNullException.ThrowIfNull(request.Repository);

        var instance = await _instances.GetByIdAsync(request.InstanceId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Instance '{request.InstanceId}' does not exist.");
        var target = request.Pack.Metadata ?? throw new InvalidOperationException($"Pack '{request.Pack.Id}' does not have usable metadata.");
        if (instance.Source is not InstanceSource.FromModPack current || !ModIds.Equals(current.ModPackId, target.ModPackId))
            throw new InvalidOperationException($"Instance '{instance.Name}' was not created from pack '{target.ModPackId}'.");
        if (target.Version <= current.Version)
            throw new InvalidOperationException($"Pack '{target.ModPackId}' {target.Version} is not newer than version {current.Version} of instance '{instance.Name}'.");

        var changes = ModPackUpdates.Compare(instance, target);
        var warnings = new List<PlanningMessage>();
        var members = new List<ModPackMemberResult>();
        ModPackUpdateResult Result(InstallPlan? plan, bool stopped = false) => new(instance.InstanceId, current.Version, target, changes, plan, Ordered(members), warnings, false, stopped);

        if (request.Pack.VersionStatus?.State == IndexStatusState.Retracted)
        {
            warnings.Add(new PlanningMessage(target.ModPackId, PlanningMessageKind.RetractedPack) { Value = request.Pack.VersionStatus.Reason });
            members.AddRange(target.Mods.Select(pin => Member(pin, ModPackMemberStatus.Unresolved, "The pack version is retracted.")));
            return (instance, Result(null));
        }

        var requested = new List<RequestedMod>();
        InstallPlan plan;
        Instance draft;
        using (var planning = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stop?.Token ?? CancellationToken.None))
        {
            try
            {
                foreach (var pin in target.Mods)
                {
                    var installed = instance.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, pin.ContentId) && mod.Version == pin.Version);
                    var release = installed?.Metadata ?? await request.Repository.GetReleaseAsync(pin.ContentId, pin.Version, planning.Token).ConfigureAwait(false);
                    if (release is null)
                    {
                        members.Add(Member(pin, ModPackMemberStatus.Unresolved, "The exact pinned release is not listed."));
                        warnings.Add(new PlanningMessage(pin.ContentId, PlanningMessageKind.UnlistedPin));
                    }
                    else if (installed is null && release.Yanked && !(request.ProceedWithYankedMembers?.Contains(release.ModId, ModIds.Comparer) ?? false))
                    {
                        members.Add(Member(pin, ModPackMemberStatus.Unresolved, "Caller confirmation is required for the yanked release."));
                        warnings.Add(new PlanningMessage(pin.ContentId, PlanningMessageKind.YankedPin) { Value = release.YankedReason });
                    }
                    else
                    {
                        requested.Add(new RequestedMod(release, InstallReason.ModPack, Exact: true));
                    }
                }

                if (members.Count > 0)
                {
                    members.AddRange(target.Mods
                        .Where(pin => members.All(member => !ModIds.Equals(member.ModId, pin.ContentId)))
                        .Select(pin => Member(pin, ModPackMemberStatus.NotAttempted, "Another pack member needs caller action.")));
                    return (instance, Result(null));
                }

                while (true)
                {
                    draft = ModPackUpdates.Draft(instance, changes);
                    plan = await _planner.PlanAsync(
                        new InstallPlanningRequest(draft, requested, request.Repository, request.GameVersion, request.TargetPlatform, request.Recommended, request.Alternatives),
                        planning.Token).ConfigureAwait(false);
                    var next = ModPackUpdates.KeepNeeded(instance, changes, plan.Selections.Select(selection => selection.Release));
                    if (next.SequenceEqual(changes))
                        break;

                    changes = next;
                }
            }
            catch (OperationCanceledException) when (stop is { IsRequested: true } && !cancellationToken.IsCancellationRequested)
            {
                members.Clear();
                members.AddRange(target.Mods.Select(pin => Member(pin, ModPackMemberStatus.NotAttempted, StoppedMessage)));
                return (instance, Result(null, stopped: true));
            }
        }

        warnings.AddRange(plan.Warnings);
        changes = WithDependencies(changes, draft, plan);
        if (!plan.IsReady)
        {
            members.AddRange(target.Mods
                .Where(pin => !plan.Selections.Any(selection => selection.IsAlreadyInstalled && ModIds.Equals(selection.Release.ModId, pin.ContentId)))
                .Select(pin => Member(pin, ModPackMemberStatus.Unresolved, "The shared install plan is not ready.")));
            members.AddRange(AlreadyInstalled(changes, draft, plan));
            return (instance, Result(plan));
        }

        members.AddRange(changes
            .Where(change => change.Kind == ModPackChangeKind.Remove)
            .Select(change => new ModPackMemberResult(change.ModId, change.From!.Value, InstallReason.ModPack, ModPackMemberStatus.NotAttempted)));
        members.AddRange(plan.Operations.Select(operation => new ModPackMemberResult(
            operation.Release.ModId,
            operation.Release.Version,
            ReasonAfter(changes, draft, operation.Release.ModId, operation.Reason),
            ModPackMemberStatus.NotAttempted)));
        members.AddRange(AlreadyInstalled(changes, draft, plan));
        return (instance, Result(plan));
    }

    /// <summary>Adds what the plan installs besides the pins, such as a new dependency or a new version of a kept mod.</summary>
    private static IReadOnlyList<ModPackChange> WithDependencies(IReadOnlyList<ModPackChange> changes, Instance draft, InstallPlan plan)
    {
        ModVersion? Planned(string modId) => plan.Operations.FirstOrDefault(operation => ModIds.Equals(operation.Release.ModId, modId))?.Release.Version;
        var kept = changes.Select(change => change.Kind == ModPackChangeKind.Keep && Planned(change.ModId) is { } version
            ? change with { To = version }
            : change);
        var added = plan.Operations
            .Where(operation => !changes.Any(change => ModIds.Equals(change.ModId, operation.Release.ModId)))
            .Select(operation => draft.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, operation.Release.ModId)) is { } installed
                ? new ModPackChange(installed.ModId, ModPackChangeKind.Change, installed.Version, operation.Release.Version)
                : new ModPackChange(operation.Release.ModId, ModPackChangeKind.Add, null, operation.Release.Version));
        return kept.Concat(added).OrderBy(change => change.Kind).ThenBy(change => change.ModId, ModIds.Comparer).ToList();
    }

    private static InstallReason ReasonAfter(IReadOnlyList<ModPackChange> changes, Instance draft, string modId, InstallReason planned)
        => changes.Any(change => change.Kind == ModPackChangeKind.Keep && ModIds.Equals(change.ModId, modId))
            ? InstallReason.Manual
            : draft.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, modId))?.Reason ?? planned;

    private static IEnumerable<ModPackMemberResult> AlreadyInstalled(IReadOnlyList<ModPackChange> changes, Instance draft, InstallPlan plan) =>
        plan.Selections
            .Where(selection => selection.IsAlreadyInstalled)
            .Select(selection => new ModPackMemberResult(
                selection.Release.ModId,
                selection.Release.Version,
                ReasonAfter(changes, draft, selection.Release.ModId, selection.Reason),
                ModPackMemberStatus.AlreadyInstalled));

    private static bool IsDone(ModPackMemberStatus status) => status is ModPackMemberStatus.Installed or ModPackMemberStatus.Replaced or ModPackMemberStatus.AlreadyInstalled or ModPackMemberStatus.Removed;

    private static List<ModPackMemberResult> Ordered(IEnumerable<ModPackMemberResult> members) => members.OrderBy(member => member.ModId, ModIds.Comparer).ToList();

    private static ModPackMemberResult Member(ModPackEntry pin, ModPackMemberStatus status, string message) => new(pin.ContentId, pin.Version, InstallReason.ModPack, status, message);
}
