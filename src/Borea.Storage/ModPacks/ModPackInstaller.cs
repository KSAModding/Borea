using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Storage.ModPacks;

public sealed class ModPackInstaller : IModPackInstaller
{
    private readonly IInstanceRepository _instances;
    private readonly IInstallPlanner _planner;
    private readonly IModInstaller _installer;
    private readonly IModReplacer _replacer;

    public ModPackInstaller(IInstanceRepository instances, IInstallPlanner planner, IModInstaller installer, IModReplacer replacer)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _replacer = replacer ?? throw new ArgumentNullException(nameof(replacer));
    }

    public async Task<ModPackInstallResult> CreateAndInstallAsync(string instanceName, ModPackInstallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var metadata = RequireMetadata(request.Pack);
        var instance = await _instances.CreateAsync(instanceName, new InstanceSource.FromModPack(metadata.ModPackId, metadata.Version)).ConfigureAwait(false);
        return await InstallAsync(request with { InstanceId = instance.InstanceId }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ModPackInstallResult> InstallAsync(ModPackInstallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Pack);
        ArgumentNullException.ThrowIfNull(request.Repository);

        var instance = await _instances.GetByIdAsync(request.InstanceId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Instance '{request.InstanceId}' does not exist.");
        var metadata = RequireMetadata(request.Pack);
        var warnings = new List<PlanningMessage>();

        if (request.Pack.VersionStatus?.State == IndexStatusState.Retracted && !request.ProceedWithRetractedPack)
        {
            warnings.Add(new PlanningMessage(metadata.ModPackId, "retracted-pack", request.Pack.VersionStatus.Reason ?? "The selected pack version is retracted."));
            return Result(instance.InstanceId, null, metadata.Mods.Select(pin => Member(pin, ModPackMemberStatus.Unresolved, "Caller confirmation is required for the retracted pack version.")).ToList(), warnings, false);
        }

        var listings = await request.Repository.GetAvailableModsAsync(cancellationToken).ConfigureAwait(false);
        var releases = new List<RequestedMod>();
        var members = new List<ModPackMemberResult>();
        foreach (var pin in metadata.Mods)
        {
            var release = await request.Repository.GetReleaseAsync(pin.ContentId, pin.Version, cancellationToken).ConfigureAwait(false);
            if (release is null)
            {
                var listing = listings.FirstOrDefault(value => ModIds.Equals(value.ModId, pin.ContentId));
                members.Add(Member(pin, ModPackMemberStatus.Unresolved, "The exact pinned release is not listed.", AuthorLocation(listing)));
                warnings.Add(new PlanningMessage(pin.ContentId, "unlisted-pin", "The exact pinned release is not listed and cannot be installed by Borea."));
                continue;
            }

            if (release.Yanked && !(request.ProceedWithYankedMembers?.Any(value => ModIds.Equals(value, release.ModId)) ?? false))
            {
                members.Add(Member(pin, ModPackMemberStatus.Unresolved, "Caller confirmation is required for the yanked release.", AuthorLocation(listings.FirstOrDefault(value => ModIds.Equals(value.ModId, pin.ContentId)))));
                warnings.Add(new PlanningMessage(pin.ContentId, "yanked", release.YankedReason ?? "The exact pinned release is yanked."));
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

        var plan = await _planner.PlanAsync(
            new InstallPlanningRequest(instance, releases, request.Repository, request.GameVersion, request.TargetPlatform, request.Recommended, request.Alternatives),
            cancellationToken).ConfigureAwait(false);
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

        var fresh = await _instances.GetByIdAsync(instance.InstanceId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Instance '{instance.InstanceId}' no longer exists.");
        if (!plan.InstanceState.Matches(fresh))
        {
            members.AddRange(metadata.Mods.Select(pin => Member(pin, ModPackMemberStatus.NotAttempted, "The instance changed after planning.")));
            return Result(instance.InstanceId, plan, members, warnings, false);
        }

        var expectedState = plan.InstanceState;
        var stopped = false;
        foreach (var operation in plan.Operations)
        {
            if (stopped)
            {
                members.Add(Member(operation.Release, operation.Reason, ModPackMemberStatus.NotAttempted, "An earlier operation failed."));
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
                    var completed = await _installer.InstallGuardedAsync(instance.InstanceId, operation.Release, operation.Reason, request.Enable, expectedState, cancellationToken: cancellationToken).ConfigureAwait(false);
                    expectedState = completed.State;
                    members.Add(Member(operation.Release, operation.Reason, ModPackMemberStatus.Installed));
                }
                else
                {
                    var completed = await _replacer.ReplaceGuardedAsync(instance.InstanceId, current, operation.Release, expectedState, cancellationToken: cancellationToken).ConfigureAwait(false);
                    expectedState = completed.State;
                    members.Add(Member(operation.Release, current.Reason, ModPackMemberStatus.Replaced));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                members.Add(Member(operation.Release, operation.Reason, ModPackMemberStatus.Failed, ex.Message));
                fresh = await _instances.GetByIdAsync(instance.InstanceId).ConfigureAwait(false) ?? fresh;
                expectedState = InstallPlanningState.Capture(fresh);
                stopped = true;
            }
        }

        foreach (var selection in plan.Selections.Where(value => value.IsAlreadyInstalled))
            members.Add(Member(selection.Release, ExistingReason(instance, selection.Release.ModId, selection.Reason), ModPackMemberStatus.AlreadyInstalled));

        var complete = members.All(value => value.Status is ModPackMemberStatus.Installed or ModPackMemberStatus.Replaced or ModPackMemberStatus.AlreadyInstalled)
            && metadata.Mods.All(pin => members.Any(value => ModIds.Equals(value.ModId, pin.ContentId) && value.Version == pin.Version && value.Status is ModPackMemberStatus.Installed or ModPackMemberStatus.Replaced or ModPackMemberStatus.AlreadyInstalled));
        return Result(instance.InstanceId, plan, members.OrderBy(value => value.ModId, ModIds.Comparer).ToList(), warnings, complete);
    }

    private static ModPackMetadata RequireMetadata(ModPackResult pack) => pack.Metadata ?? throw new InvalidOperationException($"Pack '{pack.Id}' does not have usable metadata.");

    private static InstallReason ExistingReason(Instance instance, string modId, InstallReason fallback) => instance.Mods.FirstOrDefault(value => ModIds.Equals(value.ModId, modId))?.Reason ?? fallback;

    private static string? AuthorLocation(ModMetadata? listing)
    {
        if (listing is null) return null;
        foreach (var key in new[] { "repository", "spacedock", "homepage", "forums" })
            if (listing.Links.TryGetValue(key, out var value)) return value;
        return null;
    }

    private static ModPackMemberResult Member(ModPackEntry pin, ModPackMemberStatus status, string? message = null, string? location = null) => new(pin.ContentId, pin.Version, InstallReason.ModPack, status, message, location);

    private static ModPackMemberResult Member(ModVersionMetadata release, InstallReason reason, ModPackMemberStatus status, string? message = null) => new(release.ModId, release.Version, reason, status, message);

    private static ModPackInstallResult Result(Guid instanceId, InstallPlan? plan, IReadOnlyList<ModPackMemberResult> members, IReadOnlyList<PlanningMessage> warnings, bool complete) => new(instanceId, plan, members, warnings, complete);
}
