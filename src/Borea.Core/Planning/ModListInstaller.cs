using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.State;

namespace Borea.Core.Planning;

/// <param name="Reasons">A mod without a reason is a dependency when another listed release requires it, and manual otherwise.</param>
public sealed record ModListRequest(
    ModList ModList,
    InstanceSource Source,
    IModRepository Repository,
    GameVersion? GameVersion = null,
    OsPlatform? TargetPlatform = null,
    IReadOnlyDictionary<string, InstallReason>? Reasons = null);

public sealed record ModListItem(ModListEntry Entry, ModVersionMetadata? Release)
{
    public bool IsUnknown => Release is null;

    public bool IsYanked => Release is { Yanked: true };
}

public sealed record ModListPlan(ModListRequest Request, IReadOnlyList<ModListItem> Items, InstallPlan Plan)
{
    public Guid InstanceId => Plan.InstanceId;

    public IEnumerable<ModListItem> Unknown => Items.Where(item => item.IsUnknown);

    public IEnumerable<ModListItem> Yanked => Items.Where(item => item.IsYanked);
}

/// <summary>
/// Creates an instance from a modlist, with each known entry as an exact request to the planner.
/// </summary>
public sealed class ModListInstaller
{
    private readonly IInstanceRepository _instances;
    private readonly IInstallPlanner _planner;
    private readonly IInstallPlanExecutor _executor;
    private readonly IModStateRepository _modState;

    public ModListInstaller(IInstanceRepository instances, IInstallPlanner planner, IInstallPlanExecutor executor, IModStateRepository modState)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _modState = modState ?? throw new ArgumentNullException(nameof(modState));
    }

    /// <summary>Plans the known releases into an instance that does not exist yet.</summary>
    public async Task<ModListPlan> PlanAsync(ModListRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ModList);
        ArgumentNullException.ThrowIfNull(request.Repository);

        var items = new List<ModListItem>();
        foreach (var entry in request.ModList.Mods)
        {
            var release = await request.Repository.GetReleaseAsync(entry.ModId, entry.Version, cancellationToken).ConfigureAwait(false);
            items.Add(new ModListItem(entry, release));
        }

        var releases = items.Where(item => item.Release is not null).Select(item => item.Release!).ToList();
        var requested = releases.Select(release => new RequestedMod(release, ReasonOf(request, releases, release), Exact: true)).ToList();
        var draftId = Guid.NewGuid();
        var draft = Instance.FromExisting(draftId, draftId.ToString(), request.Source, DateTimeOffset.UtcNow, [], [], isFavorite: false);
        var plan = await _planner.PlanAsync(
            new InstallPlanningRequest(draft, requested, request.Repository, request.GameVersion, request.TargetPlatform),
            cancellationToken).ConfigureAwait(false);
        return new ModListPlan(request, items, plan);
    }

    /// <summary>A failed or stopped install removes the new instance again.</summary>
    public async Task<Instance> InstallAsync(ModListPlan plan, string name, IProgress<InstallProgress>? progress = null, InstallStop? stop = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.Plan.IsReady)
            throw new InvalidOperationException("The install plan has unresolved choices or conflicts.");
        if (await _instances.GetByIdAsync(plan.InstanceId).ConfigureAwait(false) is not null)
            throw new InvalidOperationException("The instance of this plan exists already.");
        if (!await _instances.IsNameAvailableAsync(name).ConfigureAwait(false))
            throw new InvalidOperationException($"Instance name '{name}' is already in use.");

        var instance = Instance.FromExisting(plan.InstanceId, name, plan.Request.Source, DateTimeOffset.UtcNow, [], [], isFavorite: false);
        await _instances.SaveAsync(instance).ConfigureAwait(false);
        try
        {
            await _executor.ExecuteAsync(plan.Plan, enable: true, progress, stop, cancellationToken).ConfigureAwait(false);
            foreach (var item in plan.Items.Where(item => item.Release is not null && !item.Entry.Enabled))
                await _modState.SetInactiveAsync(instance.InstanceId, item.Entry.ModId, cancellationToken).ConfigureAwait(false);
            await ApplyLoadOrderAsync(instance.InstanceId, plan.Request.ModList, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await RemoveAsync(instance.InstanceId).ConfigureAwait(false);
            throw;
        }

        return await _instances.GetByIdAsync(instance.InstanceId).ConfigureAwait(false) ?? instance;
    }

    /// <summary>
    /// The wanted name when no instance uses it, or the first free "name (2)", "name (3)" and so on.
    /// </summary>
    public async Task<string> FreeNameAsync(string wanted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wanted);

        var name = wanted.Trim();
        for (var number = 2; !await _instances.IsNameAvailableAsync(name).ConfigureAwait(false); number++)
            name = $"{wanted.Trim()} ({number})";
        return name;
    }

    private async Task ApplyLoadOrderAsync(Guid instanceId, ModList modList, CancellationToken cancellationToken)
    {
        var position = modList.Mods
            .Select((entry, index) => (entry.ModId, index))
            .ToDictionary(item => item.ModId, item => item.index, ModIds.Comparer);
        var entries = await _modState.GetEntriesAsync(instanceId, cancellationToken).ConfigureAwait(false);

        // the game loads enabled mods in manifest order (ModLibrary.PrepareAll), and a mod the modlist does not name is a dependency the planner added, so it stays in front
        var order = entries
            .Select(entry => entry.ModId)
            .Where(modId => !string.IsNullOrWhiteSpace(modId))
            .OrderBy(modId => position.TryGetValue(modId, out var index) ? index : -1)
            .ToList();
        await _modState.ReorderAsync(instanceId, order, cancellationToken).ConfigureAwait(false);
    }

    private async Task RemoveAsync(Guid instanceId)
    {
        try
        {
            await _instances.DeleteAsync(instanceId).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // the install error is the one to report
        }
    }

    private static InstallReason ReasonOf(ModListRequest request, IReadOnlyList<ModVersionMetadata> releases, ModVersionMetadata release)
    {
        if (request.Reasons is not null && request.Reasons.TryGetValue(release.ModId, out var reason))
            return reason;

        var required = releases.Any(other => other.Dependencies.Any(dependency =>
            dependency.Kind == ModDependencyKind.Required
            && (dependency.IsAnyOf
                ? dependency.AnyOf.Any(alternative => ModIds.Equals(alternative.ModId, release.ModId))
                : ModIds.Equals(dependency.ModId, release.ModId))));
        return required ? InstallReason.Dependency : InstallReason.Manual;
    }
}
