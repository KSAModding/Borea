using Borea.Core.Mods;

namespace Borea.Network.SpaceDock;

/// <summary>
/// Maps a mod's true, permanent ModId to SpaceDock's own numeric mod ID.
/// SpaceDock never exposes a mod.toml-style ID, so its listings carry the
/// numeric ID as a placeholder and TryResolveId accepts that directly. A true
/// ModId resolves only after something has registered it, and nothing does so
/// today.
/// </summary>
public sealed class SpaceDockResolver
{
    private readonly Dictionary<string, int> _map = new(ModIds.Comparer);

    /// <summary>Registers the mapping, and replaces an existing one for the same <paramref name="modId"/>.</summary>
    public void Register(string modId, int spaceDockId)
    {
        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("Mod ID cannot be null or whitespace.", nameof(modId));

        _map[modId] = spaceDockId;
    }

    public bool TryResolve(string modId, out int spaceDockId) =>
        _map.TryGetValue(modId, out spaceDockId);

    /// <summary>
    /// Combined resolution: a raw integer ModId means "still the browse-time
    /// placeholder" and is used directly; anything else is looked up as a
    /// registered true ModId.
    /// </summary>
    public bool TryResolveId(string modId, out int spaceDockId)
    {
        if (int.TryParse(modId, out spaceDockId))
            return true;

        return TryResolve(modId, out spaceDockId);
    }
}
