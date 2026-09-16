using Borea.Core.Mods;
using Borea.Core.State;

namespace Borea.Core.Instances;

/// <summary>
/// The mods of an instance at exact versions with their enabled flags, as a modlist file shares them.
/// </summary>
public sealed class ModList
{
    public const int CurrentFormat = 1;

    public string? Name { get; }

    public IReadOnlyList<ModListEntry> Mods { get; }

    public ModList(string? name, IReadOnlyList<ModListEntry> mods)
    {
        ArgumentNullException.ThrowIfNull(mods);

        var duplicate = mods
            .GroupBy(entry => entry.ModId, ModIds.Comparer)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicate is not null)
            throw new ArgumentException($"The modlist names '{duplicate}' more than once.", nameof(mods));

        Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        Mods = mods.ToList();
    }

    /// <summary>A mod without a manifest entry is disabled, because ModLibrary.AddMods adds a new folder as disabled.</summary>
    public static ModList FromInstance(Instance instance, IReadOnlyList<ModManifestEntry> manifest)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(manifest);

        var loadOrder = new Dictionary<string, int>(ModIds.Comparer);
        var enabled = new HashSet<string>(ModIds.Comparer);
        for (var index = 0; index < manifest.Count; index++)
        {
            loadOrder.TryAdd(manifest[index].ModId, index);
            if (manifest[index].Enabled)
                enabled.Add(manifest[index].ModId);
        }

        var entries = instance.Mods
            .OrderBy(mod => loadOrder.TryGetValue(mod.ModId, out var index) ? index : int.MaxValue)
            .ThenBy(mod => mod.ModId, ModIds.Comparer)
            .Select(mod => new ModListEntry(mod.ModId, mod.Version, enabled.Contains(mod.ModId)))
            .ToList();
        return new ModList(instance.Name, entries);
    }
}
