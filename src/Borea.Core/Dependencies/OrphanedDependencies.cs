using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.Core.Dependencies;

/// <summary>
/// The mods that Borea installed only because another mod named them, and that no mod of the instance names any more
/// once a mod is removed.
/// </summary>
public static class OrphanedDependencies
{
    /// <summary>
    /// The mods that become unused when <paramref name="removedModId"/> leaves <paramref name="instance"/>, in the order in
    /// which they become unused. A mod counts only when Borea installed it as a dependency and owns its files, and it stays
    /// while any other mod names it, by any kind other than a conflict and also as one choice of an any_of entry. A
    /// dependency that only another unused dependency named becomes unused too.
    /// </summary>
    public static IReadOnlyList<InstalledMod> After(Instance instance, string removedModId)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(removedModId);

        var remaining = instance.Mods.Where(mod => !ModIds.Equals(mod.ModId, removedModId)).ToList();
        var unused = new List<InstalledMod>();
        while (remaining.FirstOrDefault(mod => IsUnusedDependency(mod, remaining)) is { } found)
        {
            unused.Add(found);
            remaining.Remove(found);
        }

        return unused;
    }

    private static bool IsUnusedDependency(InstalledMod mod, IReadOnlyList<InstalledMod> remaining) =>
        mod.Reason == InstallReason.Dependency
        && mod.Ownership == ModInstallOwnership.Borea
        && !remaining.Any(other => !ModIds.Equals(other.ModId, mod.ModId) && Names(other, mod.ModId));

    private static bool Names(InstalledMod other, string modId) => other.Metadata.Dependencies.Any(dependency =>
        dependency.Kind != ModDependencyKind.Conflict
        && (ModIds.Equals(dependency.ModId, modId) || dependency.AnyOf?.Any(choice => ModIds.Equals(choice.ModId, modId)) == true));
}
