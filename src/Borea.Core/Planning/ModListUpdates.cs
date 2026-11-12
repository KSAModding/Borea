using Borea.Core.Instances;
using Borea.Core.ModPacks;
using Borea.Core.Mods;

namespace Borea.Core.Planning;

/// <summary>What a later modlist link with the same list id changes in the instance that an earlier link created.</summary>
public static class ModListUpdates
{
    /// <summary>
    /// The mods of <paramref name="target"/> that the instance does not have or has at another version, and the mods that an
    /// earlier link put into the instance and <paramref name="target"/> no longer names. A mod the player pinned keeps its version,
    /// the same as in a pack update. A mod the player added stays and shows as kept, and a dependency follows the planner.
    /// </summary>
    public static IReadOnlyList<ModPackChange> Compare(Instance instance, ModList target)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(target);

        var changes = new List<ModPackChange>();
        foreach (var entry in target.Mods)
        {
            var installed = instance.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, entry.ModId));
            if (installed is null)
                changes.Add(new ModPackChange(entry.ModId, ModPackChangeKind.Add, null, entry.Version));
            else if (installed.Version == entry.Version)
                continue;
            else if (installed.IsPinned)
                changes.Add(new ModPackChange(installed.ModId, ModPackChangeKind.Pinned, installed.Version, installed.Version));
            else
                changes.Add(new ModPackChange(installed.ModId, ModPackChangeKind.Change, installed.Version, entry.Version));
        }

        var linked = instance.LinkedModList?.Mods ?? new HashSet<string>(ModIds.Comparer);
        foreach (var mod in instance.Mods.Where(mod => !target.Mods.Any(entry => ModIds.Equals(entry.ModId, mod.ModId))))
        {
            if (linked.Contains(mod.ModId))
            {
                changes.Add(mod.CanDeleteFiles && !mod.IsPinned
                    ? new ModPackChange(mod.ModId, ModPackChangeKind.Remove, mod.Version, null)
                    : new ModPackChange(mod.ModId, ModPackChangeKind.Keep, mod.Version, mod.Version));
            }
            else if (mod.Reason == InstallReason.Manual)
            {
                changes.Add(new ModPackChange(mod.ModId, ModPackChangeKind.Keep, mod.Version, mod.Version));
            }
        }

        return changes.OrderBy(change => change.Kind).ThenBy(change => change.ModId, ModIds.Comparer).ToList();
    }
}
