namespace Borea.Storage.Preview.Ksa;

/// <summary>
/// The two ways the game compares Ids. See <see cref="KsaPartSchema"/> for the build they were verified against.
/// </summary>
internal static class KsaIdComparer
{
    /// <summary>
    /// For every registry key: parts, subparts, GameData, glTF mesh names, materials and files. The game keys its
    /// registries by KeyHash.Make, which lowercases the Id before it hashes it.
    /// </summary>
    public static StringComparer Registry => StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// For the two matches that the game does on the Id string itself: ModLibrary.AttachGameData attaches a
    /// PartGameData to the part template only when item.Id.Equals(partTemplate.Id), and
    /// PartTemplate.ApplyOrAddSubPartInstance replaces a subpart instance only when subPartInstance.Id == existing.Id.
    /// </summary>
    public static StringComparer Instance => StringComparer.Ordinal;
}
