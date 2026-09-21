using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Storage.ModPacks;

public sealed class ModPackInstaller : IModPackInstaller
{
    private const string StoppedMessage = "The install was stopped.";

    private readonly IInstanceRepository _instances;
    private readonly IInstallPlanner _planner;
    private readonly IModInstaller _installer;
    private readonly IModReplacer _replacer;
    private readonly IInstallSpaceCheck? _space;

    public ModPackInstaller(IInstanceRepository instances, IInstallPlanner planner, IModInstaller installer, IModReplacer replacer, IInstallSpaceCheck? space = null)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _replacer = replacer ?? throw new ArgumentNullException(nameof(replacer));
        _space = space;
    }

    public async Task<ModPackInstallResult> CreateAndInstallAsync(string instanceName, ModPackInstallRequest request, IProgress<InstallProgress>? progress = null, InstallStop? stop = null, CancellationToken cancellationToken = default)
    {
        var planned = await PlanDraftAsync(instanceName, request, stop, cancellationToken).ConfigureAwait(false);
        if (planned.Plan is not { IsReady: true })
            return planned;

        var metadata = RequireMetadata(request.Pack);
        if (stop is { IsRequested: true })
            return Result(Guid.Empty, null, metadata.Mods.Select(pin => Member(pin, ModPackMemberStatus.NotAttempted, StoppedMessage)).ToList(), planned.Warnings, false, stopped: true);

        // The draft plan holds the same releases as the plan the install runs, so the refusal comes before the instance exists.
        _space?.EnsureFits(planned.Plan);

        var created = await _instances.CreateAsync(instanceName, Source(metadata)).ConfigureAwait(false);
        return await InstallAsync(request with { InstanceId = created.Instance.InstanceId }, progress, stop, cancellationToken).ConfigureAwait(false);
    }

    public Task<ModPackInstallResult> PlanNewAsync(string instanceName, ModPackInstallRequest request, CancellationToken cancellationToken = default) =>
        PlanDraftAsync(instanceName, request, stop: null, cancellationToken);

    public Task<ModPackInstallResult> PlanAsync(ModPackInstallRequest request, CancellationToken cancellationToken = default) =>
        RunAsync(request, write: false, progress: null, stop: null, cancellationToken);

    public Task<ModPackInstallResult> InstallAsync(ModPackInstallRequest request, IProgress<InstallProgress>? progress = null, InstallStop? stop = null, CancellationToken cancellationToken = default) =>
        RunAsync(request, write: true, progress, stop, cancellationToken);

    private async Task<ModPackInstallResult> PlanDraftAsync(string instanceName, ModPackInstallRequest request, InstallStop? stop, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Pack);
        cancellationToken.ThrowIfCancellationRequested();
        var draft = new Instance(instanceName, Source(RequireMetadata(request.Pack)));
        var planned = await RunAsync(request, write: false, progress: null, stop, cancellationToken, draft).ConfigureAwait(false);
        return Result(Guid.Empty, planned.Plan, planned.Members, planned.Warnings, planned.IsComplete, planned.IsStopped);
    }

    private async Task<ModPackInstallResult> RunAsync(ModPackInstallRequest request, bool write, IProgress<InstallProgress>? progress, InstallStop? stop, CancellationToken cancellationToken, Instance? draft = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Pack);
        ArgumentNullException.ThrowIfNull(request.Repository);

        var instance = draft ?? await _instances.GetByIdAsync(request.InstanceId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Instance '{request.InstanceId}' does not exist.");
        var metadata = RequireMetadata(request.Pack);
        var warnings = new List<PlanningMessage>();

        if (request.Pack.VersionStatus?.State == IndexStatusState.Retracted && !request.ProceedWithRetractedPack)
        {
            warnings.Add(new PlanningMessage(metadata.ModPackId, PlanningMessageKind.RetractedPack) { Value = request.Pack.VersionStatus.Reason });
            return Result(instance.InstanceId, null, metadata.Mods.Select(pin => Member(pin, ModPackMemberStatus.Unresolved, "Caller confirmation is required for the retracted pack version.")).ToList(), warnings, false);
        }

        var releases = new List<RequestedMod>();
        var members = new List<ModPackMemberResult>();
        InstallPlan plan;
        using (var planning = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stop?.Token ?? CancellationToken.None))
        {
            try
            {
                var listings = await request.Repository.GetAvailableModsAsync(planning.Token).ConfigureAwait(false);
                foreach (var pin in metadata.Mods)
                {
                    var release = await request.Repository.GetReleaseAsync(pin.ContentId, pin.Version, planning.Token).ConfigureAwait(false);
                    if (release is null)
                    {
                        var listing = listings.FirstOrDefault(value => ModIds.Equals(value.ModId, pin.ContentId));
                        members.Add(Member(pin, ModPackMemberStatus.Unresolved, "The exact pinned release is not listed.", AuthorLocation(listing)));
                        warnings.Add(new PlanningMessage(pin.ContentId, PlanningMessageKind.UnlistedPin));
                        continue;
                    }

                    if (release.Yanked && !(request.ProceedWithYankedMembers?.Any(value => ModIds.Equals(value, release.ModId)) ?? false))
                    {
                        members.Add(Member(pin, ModPackMemberStatus.Unresolved, "Caller confirmation is required for the yanked release.", AuthorLocation(listings.FirstOrDefault(value => ModIds.Equals(value.ModId, pin.ContentId)))));
                        warnings.Add(new PlanningMessage(pin.ContentId, PlanningMessageKind.YankedPin) { Value = release.YankedReason });
                        continue;
                    }

                    releases.Add(new RequestedMod(release, InstallReason.ModPack, Exact: true));
                }

                if (members.Count > 0)
                {
                    foreach (var pin in metadata.Mods.Where(pin => members.All(value => !ModIds.Equals(value.ModId, pin.ContentId))))
                        members.Add(Member(pin, ModPackMemberStatus.NotAttempted, "Another pack member needs caller action."));
                    return Result(instance.InstanceId, null, members, warnings, false);
                }

                plan = await _planner.PlanAsync(
                    new InstallPlanningRequest(instance, releases, request.Repository, request.GameVersion, request.TargetPlatform, request.Recommended, request.Alternatives),
                    planning.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop is { IsRequested: true } && !cancellationToken.IsCancellationRequested)
            {
                return Result(instance.InstanceId, null, metadata.Mods.Select(pin => Member(pin, ModPackMemberStatus.NotAttempted, StoppedMessage)).ToList(), warnings, false, stopped: true);
            }
        }

        warnings.AddRange(plan.Warnings);
        if (!plan.IsReady)
        {
            foreach (var pin in metadata.Mods)
            {
                var selection = plan.Selections.FirstOrDefault(value => ModIds.Equals(value.Release.ModId, pin.ContentId) && value.Release.Version == pin.Version);
                members.Add(selection is { IsAlreadyInstalled: true }
                    ? Member(selection.Release, ExistingReason(instance, selection.Release.ModId, selection.Reason), ModPackMemberStatus.AlreadyInstalled)
                    : Member(pin, ModPackMemberStatus.Unresolved, "The shared install plan is not ready."));
            }
            return Result(instance.InstanceId, plan, members, warnings, false);
        }

        if (!write)
        {
            members.AddRange(plan.Operations.Select(operation => Member(operation.Release, ExistingReason(instance, operation.Release.ModId, operation.Reason), ModPackMemberStatus.NotAttempted)));
            members.AddRange(AlreadyInstalled(instance, plan));
            return Result(instance.InstanceId, plan, Ordered(members), warnings, IsComplete(metadata, members));
        }

        _space?.EnsureFits(plan);

        var fresh = await _instances.GetByIdAsync(instance.InstanceId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Instance '{instance.InstanceId}' no longer exists.");
        if (!plan.InstanceState.Matches(fresh))
        {
            members.AddRange(metadata.Mods.Select(pin => Member(pin, ModPackMemberStatus.NotAttempted, "The instance changed after planning.")));
            return Result(instance.InstanceId, plan, members, warnings, false);
        }

        var expectedState = plan.InstanceState;
        var stopped = false;
        var stoppedOnRequest = false;
        var step = 0;
        foreach (var operation in plan.Operations)
        {
            var operationProgress = progress.ForStep(++step, plan.Operations.Count);
            if (stopped)
            {
                members.Add(Member(operation.Release, operation.Reason, ModPackMemberStatus.NotAttempted, "An earlier operation failed."));
                continue;
            }

            if (stop is { IsRequested: true })
            {
                members.Add(Member(operation.Release, operation.Reason, ModPackMemberStatus.NotAttempted, StoppedMessage));
                stoppedOnRequest = true;
                continue;
            }

            fresh = await _instances.GetByIdAsync(instance.InstanceId).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Instance '{instance.InstanceId}' no longer exists.");
            if (!expectedState.Matches(fresh))
            {
                members.Add(Member(operation.Release, operation.Reason, ModPackMemberStatus.NotAttempted, "The instance changed during pack installation."));
                stopped = true;
                continue;
            }

            try
            {
                var current = fresh.Mods.FirstOrDefault(value => ModIds.Equals(value.ModId, operation.Release.ModId));
                if (current is null)
                {
                    var completed = await stop.RunAsync((reports, token) => _installer.InstallGuardedAsync(instance.InstanceId, operation.Release, operation.Reason, request.Enable, expectedState, reports, token), operationProgress, cancellationToken).ConfigureAwait(false);
                    expectedState = completed.State;
                    members.Add(Member(operation.Release, operation.Reason, ModPackMemberStatus.Installed));
                }
                else
                {
                    var completed = await stop.RunAsync((reports, token) => _replacer.ReplaceGuardedAsync(instance.InstanceId, current, operation.Release, expectedState, reports, token), operationProgress, cancellationToken).ConfigureAwait(false);
                    expectedState = completed.State;
                    members.Add(Member(operation.Release, current.Reason, ModPackMemberStatus.Replaced));
                }
            }
            catch (InstallStoppedException)
            {
                members.Add(Member(operation.Release, operation.Reason, ModPackMemberStatus.NotAttempted, StoppedMessage));
                stoppedOnRequest = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                members.Add(Member(operation.Release, operation.Reason, ModPackMemberStatus.Failed, ex.Message));
                fresh = await _instances.GetByIdAsync(instance.InstanceId).ConfigureAwait(false) ?? fresh;
                expectedState = InstallPlanningState.Capture(fresh);
                stopped = true;
            }
        }

        members.AddRange(AlreadyInstalled(instance, plan));
        return Result(instance.InstanceId, plan, Ordered(members), warnings, IsComplete(metadata, members), stoppedOnRequest);
    }

    private static ModPackMetadata RequireMetadata(ModPackResult pack) => pack.Metadata ?? throw new InvalidOperationException($"Pack '{pack.Id}' does not have usable metadata.");

    private static InstanceSource Source(ModPackMetadata metadata) => new InstanceSource.FromModPack(metadata.ModPackId, metadata.Version);

    private static InstallReason ExistingReason(Instance instance, string modId, InstallReason fallback) => instance.Mods.FirstOrDefault(value => ModIds.Equals(value.ModId, modId))?.Reason ?? fallback;

    private static IEnumerable<ModPackMemberResult> AlreadyInstalled(Instance instance, InstallPlan plan) =>
        plan.Selections
            .Where(value => value.IsAlreadyInstalled)
            .Select(selection => Member(selection.Release, ExistingReason(instance, selection.Release.ModId, selection.Reason), ModPackMemberStatus.AlreadyInstalled));

    private static bool IsDone(ModPackMemberStatus status) => status is ModPackMemberStatus.Installed or ModPackMemberStatus.Replaced or ModPackMemberStatus.AlreadyInstalled;

    private static bool IsComplete(ModPackMetadata metadata, IReadOnlyList<ModPackMemberResult> members) =>
        members.All(value => IsDone(value.Status))
        && metadata.Mods.All(pin => members.Any(value => ModIds.Equals(value.ModId, pin.ContentId) && value.Version == pin.Version && IsDone(value.Status)));

    private static List<ModPackMemberResult> Ordered(IEnumerable<ModPackMemberResult> members) => members.OrderBy(value => value.ModId, ModIds.Comparer).ToList();

    private static string? AuthorLocation(ModMetadata? listing)
    {
        if (listing is null) return null;
        foreach (var key in new[] { "repository", "spacedock", "homepage", "forums" })
            if (listing.Links.TryGetValue(key, out var value)) return value;
        return null;
    }

    private static ModPackMemberResult Member(ModPackEntry pin, ModPackMemberStatus status, string? message = null, string? location = null) => new(pin.ContentId, pin.Version, InstallReason.ModPack, status, message, location);

    private static ModPackMemberResult Member(ModVersionMetadata release, InstallReason reason, ModPackMemberStatus status, string? message = null) => new(release.ModId, release.Version, reason, status, message);

    private static ModPackInstallResult Result(Guid instanceId, InstallPlan? plan, IReadOnlyList<ModPackMemberResult> members, IReadOnlyList<PlanningMessage> warnings, bool complete, bool stopped = false) => new(instanceId, plan, members, warnings, complete, stopped);
}
