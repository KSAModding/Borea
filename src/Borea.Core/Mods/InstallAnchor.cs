namespace Borea.Core.Mods;

/// <summary>
/// Where installed content is written, per RFC 0035. A manager resolves the
/// anchor at install time, so the metadata never carries an absolute path.
/// </summary>
public enum InstallAnchor
{
    /// <summary>The game's mods folder. Moves with the profile under an instance path override.</summary>
    Mods = 0,

    /// <summary>The game's user data root: saves, vehicles, settings, manifest.</summary>
    UserData = 1,

    /// <summary>The directory holding the game executable.</summary>
    GameRoot = 2,

    /// <summary>A directory of the manager's own choosing, outside the other three.</summary>
    Standalone = 3,

    /// <summary>A value this client version does not know. A manager must not guess.</summary>
    Unknown = 4,
}
