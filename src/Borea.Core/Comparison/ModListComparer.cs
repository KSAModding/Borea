using Borea.Core.ModPacks;
using Borea.Core.Mods;

namespace Borea.Core.Comparison;

/// <summary>
/// Compares a set of currently installed mods against a target mod list.
/// </summary>
public sealed class ModListComparer
{

    /// <summary>
    /// Compares a list of InstalledMods against a list of ModPackEntries,
    /// returning a ModListDiff that describes the differences.
    /// </summary>
    public ModListDiff Compare(IReadOnlyList<InstalledMod> currentMods, IReadOnlyList<ModPackEntry> targetMods)
    {
        if (currentMods is null) throw new ArgumentNullException(nameof(currentMods));
        if (targetMods is null) throw new ArgumentNullException(nameof(targetMods));

        var currentById = currentMods.ToDictionary(m => m.ModId, StringComparer.Ordinal);
        var targetIds = new HashSet<string>(targetMods.Select(m => m.ModId), StringComparer.Ordinal);

        var toAdd = new List<ModPackEntry>();
        var toUpdate = new List<ModVersionChange>();
        var unchanged = new List<string>();

        foreach (var target in targetMods)
        {
            if (currentById.TryGetValue(target.ModId, out var current))
            {
                if (current.Version.Equals(target.Version))
                    unchanged.Add(target.ModId);
                else
                    toUpdate.Add(new ModVersionChange(target.ModId, current.Version, target.Version));
            }
            else
            {
                toAdd.Add(target);
            }
        }

        var toRemove = currentMods
            .Where(m => !targetIds.Contains(m.ModId))
            .Select(m => m.ModId)
            .ToList();

        return new ModListDiff(toAdd, toRemove, toUpdate, unchanged);
    }
}