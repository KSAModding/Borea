using Borea.Core.Mods;

namespace Borea.Core.ModPacks;

public readonly record struct ModPackEntry
{
    public string ContentId { get; }

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
