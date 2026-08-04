using System.Collections.ObjectModel;
using Borea.Core.ModPacks;

namespace Borea.Core.Comparison;

/// <summary>
/// Result of comparing an instance's currently installed mods against a
/// target mod list (typically a newer ModPackMetadata version), driving the
/// "Update" operation.
/// </summary>
public sealed class ModListDiff
{
    /// <summary>Mods present in the target but not currently installed.</summary>
    public IReadOnlyList<ModPackEntry> ToAdd { get; }

    /// <summary>Mod IDs currently installed but absent from the target.</summary>
    public IReadOnlyList<string> ToRemove { get; }

    /// <summary>Mods present in both, but at a different version.</summary>
    public IReadOnlyList<ModVersionChange> ToUpdate { get; }

    /// <summary>Mods present in both at the same version — no action needed.</summary>
    public IReadOnlyList<string> Unchanged { get; }

    public bool IsEmpty => ToAdd.Count == 0 && ToRemove.Count == 0 && ToUpdate.Count == 0;

    public ModListDiff(
        IReadOnlyList<ModPackEntry> toAdd,
        IReadOnlyList<string> toRemove,
        IReadOnlyList<ModVersionChange> toUpdate,
        IReadOnlyList<string> unchanged)
    {
        if (toAdd is null) throw new ArgumentNullException(nameof(toAdd));
        if (toRemove is null) throw new ArgumentNullException(nameof(toRemove));
        if (toUpdate is null) throw new ArgumentNullException(nameof(toUpdate));
        if (unchanged is null) throw new ArgumentNullException(nameof(unchanged));

        ToAdd = new ReadOnlyCollection<ModPackEntry>(toAdd.ToArray());
        ToRemove = new ReadOnlyCollection<string>(toRemove.ToArray());
        ToUpdate = new ReadOnlyCollection<ModVersionChange>(toUpdate.ToArray());
        Unchanged = new ReadOnlyCollection<string>(unchanged.ToArray());
    }
}

/// <summary>
/// A mod whose installed version differs from the target version.
/// </summary>
public readonly record struct ModVersionChange(string ModId, Mods.ModVersion CurrentVersion, Mods.ModVersion NewVersion);
