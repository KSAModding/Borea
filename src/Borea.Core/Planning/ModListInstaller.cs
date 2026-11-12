using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.State;

namespace Borea.Core.Planning;

/// <param name="Reasons">A mod without a reason is a dependency when another listed release requires it, and manual otherwise.</param>
/// <param name="GameBuild">The kept game build of the new instance. Only a duplicate keeps one, because a modlist can come from another PC that does not have the build.</param>
/// <param name="Loader">The mod loader setting of the new instance, or null for the automatic one.</param>
/// <param name="Releases">
/// The releases the caller checked for the entries, or null to read each entry from <paramref name="Repository"/>. With it, an entry
/// takes only its release from this list, so it cannot resolve to another release of the same id and version, such as one of another source.
/// </param>
/// <param name="ListId">The id of the modlist link, which the new instance records, or null when no link with an id made the request.</param>
public sealed record ModListRequest(
    ModList ModList,
    InstanceSource Source,
    IModRepository Repository,
    GameVersion? GameVersion = null,
    OsPlatform? TargetPlatform = null,
    IReadOnlyDictionary<string, InstallReason>? Reasons = null,
    InstanceOrigin Origin = InstanceOrigin.ModListImport,
    GameVersion? GameBuild = null,
    LoaderSetting? Loader = null,
    IReadOnlyList<ModVersionMetadata>? Releases = null,
    string? ListId = null);

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

/// <param name="Instance">The instance as it was when the update was planned.</param>
/// <param name="Changes">What the update adds, changes, removes and keeps, in the form of the changes of a pack update.</param>
public sealed record ModListUpdatePlan(ModListPlan Plan, Instance Instance, IReadOnlyList<ModPackChange> Changes);

/// <summary>
/// Creates an instance from a modlist, with each known entry as an exact request to the planner, and updates an instance
/// that an earlier modlist link with the same list id created.
/// </summary>
public sealed class ModListInstaller
{
    private readonly IInstanceRepository _instances;
    private readonly IInstallPlanner _planner;
    private readonly IInstallPlanExecutor _executor;
    private readonly IModStateRepository _modState;
    private readonly IModUninstaller? _uninstaller;

    /// <param name="uninstaller">Removes the mods that an update drops. Null refuses an update that removes a mod.</param>
    public ModListInstaller(IInstanceRepository instances, IInstallPlanner planner, IInstallPlanExecutor executor, IModStateRepository modState, IModUninstaller? uninstaller = null)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _modState = modState ?? throw new ArgumentNullException(nameof(modState));
        _uninstaller = uninstaller;
    }

    /// <summary>Plans the known releases into an instance that does not exist yet.</summary>
    public async Task<ModListPlan> PlanAsync(ModListRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ModList);
        ArgumentNullException.ThrowIfNull(request.Repository);

        var items = await ReadItemsAsync(request, cancellationToken).ConfigureAwait(false);
        var releases = items.Where(item => item.Release is not null).Select(item => item.Release!).ToList();
        var requested = releases.Select(release => new RequestedMod(release, ReasonOf(request, releases, release), Exact: true)).ToList();
        var draftId = Guid.NewGuid();
        var draft = Instance.FromExisting(draftId, draftId.ToString(), request.Source, DateTimeOffset.UtcNow, [], [], isFavorite: false);
        var plan = await _planner.PlanAsync(
            new InstallPlanningRequest(draft, requested, request.Repository, request.GameVersion, request.TargetPlatform),
            cancellationToken).ConfigureAwait(false);
        return new ModListPlan(request, items, plan);
    }

    internal static async Task<IReadOnlyList<ModListItem>> ReadItemsAsync(ModListRequest request, CancellationToken cancellationToken)
    {
        var items = new List<ModListItem>();
        foreach (var entry in request.ModList.Mods)
        {
            var release = request.Releases is { } releases
                ? releases.FirstOrDefault(checkedRelease => ModIds.Equals(checkedRelease.ModId, entry.ModId) && checkedRelease.Version == entry.Version)
                : await request.Repository.GetReleaseAsync(entry.ModId, entry.Version, cancellationToken).ConfigureAwait(false);
            items.Add(new ModListItem(entry, release));
        }

        return items;
    }

    /// <summary>A failed or stopped install removes the new instance again.</summary>
    public async Task<InstanceCreateResult> InstallAsync(ModListPlan plan, string name, IProgress<InstallProgress>? progress = null, InstallStop? stop = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.Plan.IsReady)
            throw new InvalidOperationException("The install plan has unresolved choices or conflicts.");
        if (await _instances.GetByIdAsync(plan.InstanceId).ConfigureAwait(false) is not null)
            throw new InvalidOperationException("The instance of this plan exists already.");

        var record = Instance.FromExisting(plan.InstanceId, name, plan.Request.Source, DateTimeOffset.UtcNow, [], [], isFavorite: false, gameBuild: plan.Request.GameBuild);
        record.SetLoader(plan.Request.Loader ?? LoaderSetting.Automatic);
        if (plan.Request.ListId is { } listId)
            record.SetLinkedModList(new LinkedModList(listId, plan.Items.Where(item => item.Release is not null).Select(item => item.Entry.ModId)));
        var created = await _instances.CreateAsync(record, plan.Request.Origin).ConfigureAwait(false);
        var instance = created.Instance;
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

        return created with { Instance = await _instances.GetByIdAsync(instance.InstanceId).ConfigureAwait(false) ?? instance };
    }

    /// <summary>
    /// Plans the update of an instance that an earlier modlist link with <see cref="ModListRequest.ListId"/> created. Every entry
    /// is an exact request, except a mod the player pinned, and the mods that only earlier links put into the instance and the
    /// list no longer names are removed while nothing that stays needs them.
    /// </summary>
    public async Task<ModListUpdatePlan> PlanUpdateAsync(ModListRequest request, Guid instanceId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ModList);
        ArgumentNullException.ThrowIfNull(request.Repository);

        var instance = await _instances.GetByIdAsync(instanceId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Instance '{instanceId}' does not exist.");
        if (instance.LinkedModList is not { } linked || !ModIds.Equals(linked.ListId, request.ListId))
            throw new InvalidOperationException($"Instance '{instance.Name}' was not created from the modlist '{request.ListId}'.");

        var items = await ReadItemsAsync(request, cancellationToken).ConfigureAwait(false);
        if (items.Any(item => item.Release is null))
            throw new InvalidOperationException("An update needs a release for every entry of the modlist.");

        var releases = items.Select(item => item.Release!).ToList();
        var changes = ModListUpdates.Compare(instance, request.ModList);
        var requested = releases
            .Where(release => !changes.Any(change => change.Kind == ModPackChangeKind.Pinned && ModIds.Equals(change.ModId, release.ModId)))
            .Select(release => new RequestedMod(
                release,
                instance.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, release.ModId))?.Reason ?? ReasonOf(request, releases, release),
                Exact: true))
            .ToList();

        InstallPlan plan;
        Instance draft;
        while (true)
        {
            draft = ModPackUpdates.Draft(instance, changes);
            plan = await _planner.PlanAsync(
                new InstallPlanningRequest(draft, requested, request.Repository, request.GameVersion, request.TargetPlatform) { Current = instance },
                cancellationToken).ConfigureAwait(false);
            var next = ModPackUpdates.KeepNeeded(instance, changes, plan.Selections.Select(selection => selection.Release));
            if (next.SequenceEqual(changes))
                break;

            changes = next;
        }

        // the update installs before it removes, so its plan runs against the instance with the mods it removes
        plan = plan.WithInstanceState(InstallPlanningState.Capture(instance));
        return new ModListUpdatePlan(new ModListPlan(request, items, plan), instance, ModPackUpdates.WithPlanned(changes, draft, plan));
    }

    /// <summary>
    /// Runs a planned update. The removals are the last step, so a stop or a failure before them keeps the mods, and the instance
    /// records the new mods of the link only when every step completed. The caller reads the instance again to see what is left.
    /// </summary>
    public async Task<Instance> UpdateAsync(ModListUpdatePlan update, IProgress<InstallProgress>? progress = null, InstallStop? stop = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        var plan = update.Plan.Plan;
        if (!plan.IsReady)
            throw new InvalidOperationException("The install plan has unresolved choices or conflicts.");

        var removals = update.Changes.Where(change => change.Kind == ModPackChangeKind.Remove).ToList();
        if (removals.Count > 0 && _uninstaller is null)
            throw new InvalidOperationException("This installer cannot remove mods.");

        var instanceId = update.Instance.InstanceId;
        var current = await _instances.GetByIdAsync(instanceId).ConfigureAwait(false);
        if (current is null || !InstallPlanningState.Capture(update.Instance).Matches(current) || current.LinkedModList != update.Instance.LinkedModList)
            throw new InvalidOperationException("The instance changed after planning.");

        await _executor.ExecuteAsync(plan, enable: true, progress, stop, cancellationToken).ConfigureAwait(false);
        if (stop is { IsRequested: true })
            throw new InstallStoppedException(plan.Operations.Count, plan.Operations.Count + removals.Count);

        // once the removals start they all run, because they are the step that commits the update
        foreach (var removal in removals)
            await _uninstaller!.UninstallAsync(instanceId, removal.ModId, cancellationToken).ConfigureAwait(false);

        var modList = update.Plan.Request.ModList;
        foreach (var entry in modList.Mods.Where(entry => !entry.Enabled))
            await _modState.SetInactiveAsync(instanceId, entry.ModId, cancellationToken).ConfigureAwait(false);

        // a mod the player added before and that the list names now stays the player's, so a later list that drops it never removes it
        var listed = modList.Mods.Select(entry => entry.ModId).ToHashSet(ModIds.Comparer);
        var added = update.Changes.Where(change => change.Kind == ModPackChangeKind.Add).Select(change => change.ModId);
        var before = update.Instance.LinkedModList!;
        var linked = new LinkedModList(before.ListId, before.Mods.Concat(added).Where(listed.Contains));
        await _instances.UpdateAsync(instanceId, instance =>
        {
            instance.SetLinkedModList(linked);
            return true;
        }, cancellationToken).ConfigureAwait(false);
        await ApplyUpdateLoadOrderAsync(instanceId, modList, update.Changes, cancellationToken).ConfigureAwait(false);
        return await _instances.GetByIdAsync(instanceId).ConfigureAwait(false) ?? current;
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

    /// <summary>
    /// Puts the mods that the list names into its order in the places that these mods hold, and leaves every other mod in its place,
    /// because the player can have put a mod of their own after the mod it extends. A dependency that this update added goes in front, as in a new instance.
    /// </summary>
    private async Task ApplyUpdateLoadOrderAsync(Guid instanceId, ModList modList, IReadOnlyList<ModPackChange> changes, CancellationToken cancellationToken)
    {
        var position = modList.Mods
            .Select((entry, index) => (entry.ModId, index))
            .ToDictionary(item => item.ModId, item => item.index, ModIds.Comparer);
        var entries = (await _modState.GetEntriesAsync(instanceId, cancellationToken).ConfigureAwait(false))
            .Select(entry => entry.ModId)
            .Where(modId => !string.IsNullOrWhiteSpace(modId))
            .ToList();
        var listed = new Queue<string>(entries.Where(position.ContainsKey).OrderBy(modId => position[modId]));
        bool IsNewDependency(string modId) => !position.ContainsKey(modId)
            && changes.Any(change => change.Kind == ModPackChangeKind.Add && ModIds.Equals(change.ModId, modId));

        var order = entries.Where(IsNewDependency)
            .Concat(entries.Where(modId => !IsNewDependency(modId)).Select(modId => position.ContainsKey(modId) ? listed.Dequeue() : modId))
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
