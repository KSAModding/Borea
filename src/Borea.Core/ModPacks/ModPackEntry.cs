using Borea.Core.Mods;

namespace Borea.Core.ModPacks;

/// <summary>
/// One exact pin in a pack document: a content id and the version the pack was curated with.
/// The mods, vehicles and saves sections share this shape, and the section an entry sits in
/// says what kind of content the id names, so the entry carries no content type of its own.
/// </summary>
public readonly record struct ModPackEntry
{
    public string ContentId { get; }

    /// <summary>
    /// Valid by construction: <see cref="ModVersion"/> rejects a negative component and a
    /// malformed pre-release label when it is built or parsed, and its default value is the
    /// legal version 0.0.0.
    /// </summary>
    public ModVersion Version { get; }

    public ModPackEntry(string contentId, ModVersion version)
    {
        ModIds.Validate(contentId, nameof(contentId));

        ContentId = contentId;
        Version = version;
    }

    public bool Equals(ModPackEntry other) =>
        ModIds.Equals(ContentId, other.ContentId) && Version.Equals(other.Version);

    public override int GetHashCode() => HashCode.Combine(
        ContentId is null ? 0 : ModIds.Comparer.GetHashCode(ContentId),
        Version);
}
