using Borea.Core.Dependencies;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Core.ModPacks;

/// <summary>Finds a newer version of an instance's pack and what moving to it changes.</summary>
public static class ModPackUpdates
{
    /// <summary>The newest usable version of the source's pack, or null when it is not newer.</summary>
    public static async Task<ModPackResult?> FindNewerAsync(IModPackRepository packs, InstanceSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(source);
        if (source is not InstanceSource.FromModPack current)
            return null;

        var latest = await packs.GetLatestAsync(current.ModPackId, cancellationToken).ConfigureAwait(false);
        return latest?.Metadata is { } metadata
            && latest.VersionStatus?.State != IndexStatusState.Retracted
            && metadata.Version > current.Version
                ? latest
                : null;
    }

    /// <summary>
    /// The pins that <paramref name="target"/> adds or changes, and the pack mods that it no longer pins.
    /// A mod the player pinned keeps its version, and stays as a normal mod when the pack drops it.
    /// A mod the player detached keeps its version, or stays away when the player removed it.
    /// </summary>
    public static IReadOnlyList<ModPackChange> Compare(Instance instance, ModPackMetadata target)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(target);

        var changes = new List<ModPackChange>();
        foreach (var pin in target.Mods)
        {
            var installed = instance.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, pin.ContentId));
            if (IsDetached(instance.Source, pin.ContentId))
            {
                if (installed?.Version != pin.Version)
                    changes.Add(new ModPackChange(installed?.ModId ?? pin.ContentId, ModPackChangeKind.Detached, installed?.Version, installed?.Version));
            }
            else if (installed is null)
                changes.Add(new ModPackChange(pin.ContentId, ModPackChangeKind.Add, null, pin.Version));
            else if (installed.IsPinned && installed.Version != pin.Version)
                changes.Add(new ModPackChange(installed.ModId, ModPackChangeKind.Pinned, installed.Version, installed.Version));
            else if (installed.Version != pin.Version)
                changes.Add(new ModPackChange(installed.ModId, ModPackChangeKind.Change, installed.Version, pin.Version));
        }

        foreach (var dropped in instance.Mods.Where(mod => mod.Reason == InstallReason.ModPack && !target.Mods.Any(pin => ModIds.Equals(pin.ContentId, mod.ModId))))
        {
            changes.Add(dropped.CanDeleteFiles && !dropped.IsPinned
                ? new ModPackChange(dropped.ModId, ModPackChangeKind.Remove, dropped.Version, null)
                : new ModPackChange(dropped.ModId, ModPackChangeKind.Keep, dropped.Version, dropped.Version));
        }

        return Ordered(changes);
    }

    /// <summary>Whether a pack update leaves <paramref name="modId"/> alone, because the player detached it from the pack.</summary>
    public static bool IsDetached(InstanceSource source, string modId)
        => source is InstanceSource.FromModPack pack && pack.Detached.Contains(modId);

    /// <summary>
    /// The source that names <paramref name="target"/> once the update is complete. Every detached mod stays
    /// detached, also when <paramref name="target"/> does not pin it, so a later version that pins it again leaves it alone.
    /// </summary>
    public static InstanceSource.FromModPack SourceAfter(InstanceSource current, ModPackMetadata target)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(target);

        return new InstanceSource.FromModPack(target.ModPackId, target.Version)
            .WithDetached(current is InstanceSource.FromModPack pack ? pack.Detached : []);
    }

    /// <summary>Turns a removal into <see cref="ModPackChangeKind.Keep"/> while a mod that stays or a <paramref name="planned"/> release needs it.</summary>
    public static IReadOnlyList<ModPackChange> KeepNeeded(Instance instance, IReadOnlyList<ModPackChange> changes, IEnumerable<ModVersionMetadata> planned)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(planned);

        var removals = new HashSet<string>(changes.Where(change => change.Kind == ModPackChangeKind.Remove).Select(change => change.ModId), ModIds.Comparer);
        var mods = instance.Mods.ToDictionary(mod => mod.ModId, ModIds.Comparer);
        foreach (var release in planned)
        {
            if (removals.Remove(release.ModId) || instance.ForeignMods.Any(mod => ModIds.Equals(mod.ModId, release.ModId)))
                continue;

            mods[release.ModId] = new InstalledMod(release.ModId, release.Version, InstallReason.Dependency, DateTimeOffset.UnixEpoch, release);
        }

        var remaining = Instance.FromExisting(instance.InstanceId, instance.Name, instance.Source, instance.CreatedAt, mods.Values.ToList(), instance.ForeignMods, instance.IsFavorite);
        var resolver = new ModDependencyResolver();
        var removed = new HashSet<string>(ModIds.Comparer);
        bool progress;
        do
        {
            progress = false;
            foreach (var id in removals.Where(id => !removed.Contains(id)).Order(ModIds.Comparer).ToList())
            {
                if (!resolver.CheckUninstall(remaining, id, mods[id].Version, isActive: false).CanUninstall)
                    continue;

                remaining.RemoveMod(id);
                removed.Add(id);
                progress = true;
            }
        }
        while (progress);

        return Ordered(changes
            .Select(change => change.Kind == ModPackChangeKind.Remove && !removed.Contains(change.ModId)
                ? change with { Kind = ModPackChangeKind.Keep, To = change.From }
                : change)
            .ToList());
    }

    /// <summary>The instance the update plans for, without the mods it removes.</summary>
    public static Instance Draft(Instance instance, IReadOnlyList<ModPackChange> changes)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(changes);

        var mods = instance.Mods
            .Where(mod => changes.FirstOrDefault(change => ModIds.Equals(change.ModId, mod.ModId))?.Kind != ModPackChangeKind.Remove)
            .ToList();
        return Instance.FromExisting(instance.InstanceId, instance.Name, instance.Source, instance.CreatedAt, mods, instance.ForeignMods, instance.IsFavorite, instance.LastPlayedAt, instance.LaunchArguments, instance.GameBuild);
    }

    /// <summary>
    /// Adds what the plan installs besides the pins of a pack or the mods of a modlist, such as a new dependency or a new version of a kept mod.
    /// A detached mod that the plan installs is an addition or a change, because the update does not leave it alone.
    /// </summary>
    public static IReadOnlyList<ModPackChange> WithPlanned(IReadOnlyList<ModPackChange> changes, Instance draft, InstallPlan plan)
    {
        ModVersion? Planned(string modId) => plan.Operations.FirstOrDefault(operation => ModIds.Equals(operation.Release.ModId, modId))?.Release.Version;
        var kept = changes.Select(change => (change.Kind, Planned(change.ModId)) switch
        {
            (ModPackChangeKind.Keep, { } version) => change with { To = version },
            (ModPackChangeKind.Detached, { } version) => change with { Kind = change.From is null ? ModPackChangeKind.Add : ModPackChangeKind.Change, To = version },
            _ => change,
        });
        var added = plan.Operations
            .Where(operation => !changes.Any(change => ModIds.Equals(change.ModId, operation.Release.ModId)))
            .Select(operation => draft.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, operation.Release.ModId)) is { } installed
                ? new ModPackChange(installed.ModId, ModPackChangeKind.Change, installed.Version, operation.Release.Version)
                : new ModPackChange(operation.Release.ModId, ModPackChangeKind.Add, null, operation.Release.Version));
        return kept.Concat(added).OrderBy(change => change.Kind).ThenBy(change => change.ModId, ModIds.Comparer).ToList();
    }

    private static List<ModPackChange> Ordered(IEnumerable<ModPackChange> changes)
        => changes.OrderBy(change => change.Kind).ThenBy(change => change.ModId, ModIds.Comparer).ToList();
}
