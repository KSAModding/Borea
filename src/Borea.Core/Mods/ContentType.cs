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

    /// <summary>
    /// A vehicle, installed as one folder under Vehicles (RFC 0085).
    /// </summary>
    Vehicle = 4,

    /// <summary>
    /// A save, installed as one folder under saves (RFC 0085).
    /// </summary>
    Save = 5,
}

public static class ContentTypeExtensions
{
    /// <summary>True for the types the player changes in their folder: no code, no loader, and one folder each.</summary>
    public static bool IsVehicleOrSave(this ContentType type) => type is ContentType.Vehicle or ContentType.Save;
}
