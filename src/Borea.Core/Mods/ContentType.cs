namespace Borea.Core.Mods;

/// <summary>
/// The content type of a listing, a required field of the metadata format.
/// </summary>
public enum ContentType
{
    /// <summary>
    /// A mod, installed into the game's mods folder.
    /// </summary>
    Mod = 0,

    /// <summary>
    /// A mod pack, a curated set of pinned content.
    /// </summary>
    ModPack = 1,

    /// <summary>
    /// A mod loader, installed outside the mods folder by its own mechanism.
    /// </summary>
    ModLoader = 2,

    /// <summary>
    /// A value this client version does not know.
    /// </summary>
    Unknown = 3,
}
