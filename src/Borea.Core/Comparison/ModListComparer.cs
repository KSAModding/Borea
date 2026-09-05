using Borea.Core.ModPacks;
using Borea.Core.Mods;

namespace Borea.Core.Comparison;

/// <summary>
/// Compares the mods installed in an instance against the mods a pack pins.
/// </summary>
public sealed class ModListComparer
{
    /// <summary>
    /// Compares the installed mods against the pinned mods and returns the
    /// <see cref="ModListDiff"/> between them.
    /// <paramref name="targetMods"/> is the mods section of a pack document, so
    /// <see cref="ModPackMetadata.Mods"/> and never its vehicles or saves. A pin carries
    /// no content type, the section it sits in does, and an installed mod is the only
    /// thing this comparison matches a pin against, so an entry from another section
    /// would come back as a mod to add.
    /// </summary>
    public ModListDiff Compare(IReadOnlyList<InstalledMod> currentMods, IReadOnlyList<ModPackEntry> targetMods)
    {
        if (currentMods is null) throw new ArgumentNullException(nameof(currentMods));
        if (targetMods is null) throw new ArgumentNullException(nameof(targetMods));

        var currentById = currentMods.ToDictionary(m => m.ModId, ModIds.Comparer);
        var targetIds = new HashSet<string>(targetMods.Select(m => m.ContentId), ModIds.Comparer);

        var toAdd = new List<ModPackEntry>();
        var toUpdate = new List<ModVersionChange>();
        var unchanged = new List<string>();

        foreach (var target in targetMods)
        {
            if (currentById.TryGetValue(target.ContentId, out var current))
            {
                if (current.Version.Equals(target.Version))
                    unchanged.Add(target.ContentId);
                else
                    toUpdate.Add(new ModVersionChange(target.ContentId, current.Version, target.Version));
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
