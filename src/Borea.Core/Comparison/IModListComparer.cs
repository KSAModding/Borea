using Borea.Core.Mods;
using Borea.Core.ModPacks;

namespace Borea.Core.Comparison;

/// <summary>
/// Compares a set of currently installed mods against a target mod list.
/// </summary>
public interface IModListComparer
{
    /// <summary>
    /// Diffs an instance's currently installed mods against a target list
    /// (typically a newer ModPackMetadata's Mods).
    /// </summary>
    ModListDiff Compare(IReadOnlyList<InstalledMod> currentMods, IReadOnlyList<ModPackEntry> targetMods);
}