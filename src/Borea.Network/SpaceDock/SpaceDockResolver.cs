namespace Borea.Network.SpaceDock;

/// <summary>
/// Maps a mod's true, permanent ModId (confirmed by reading mod.toml after
/// download) to SpaceDock's own numeric mod ID. Registration happens once,
/// after the first successful download of any version of a given mod —
/// never at browse/search time, since SpaceDock never exposes a mod.toml
/// -style ID and the true ModId isn't knowable until the files are actually
/// read.
/// </summary>
public sealed class SpaceDockResolver
{
    private readonly Dictionary<string, int> _map = new(StringComparer.Ordinal);

    /// <summary>
    /// Maps a mod's true, permanent ModId (confirmed by reading mod.toml) to SpaceDock's own numeric mod ID. No-op if the modId is already registered.
    /// </summary>
    /// <param name="modId">The true, permanent ModId of the mod.</param>
    /// <param name="spaceDockId">The numeric mod ID used by SpaceDock.</param>
    /// <exception cref="ArgumentException"></exception>
    public void Register(string modId, int spaceDockId)
    {
        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("Mod ID cannot be null or whitespace.", nameof(modId));

        _map[modId] = spaceDockId;
    }

    /// <summary>
    /// Attempts to resolve a mod's true, permanent ModId to SpaceDock's own numeric mod ID. Returns true if the mapping exists, false otherwise.
    /// </summary>
    /// <param name="modId">The true, permanent ModId of the mod.</param>
    /// <param name="spaceDockId">The numeric mod ID used by SpaceDock.</param>
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