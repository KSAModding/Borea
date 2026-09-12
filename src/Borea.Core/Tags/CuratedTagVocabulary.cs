using System.Collections.ObjectModel;
using Borea.Core.Mods;

namespace Borea.Core.Tags;

public sealed class CuratedTagVocabulary
{
    public static CuratedTagVocabulary Empty { get; } = new(1, Array.Empty<CuratedTag>());
    public int SpecVersion { get; }
    public IReadOnlyList<CuratedTag> ModTags { get; }

    public CuratedTagVocabulary(int specVersion, IReadOnlyList<CuratedTag> modTags)
    {
        if (specVersion != 1)
            throw new ArgumentOutOfRangeException(nameof(specVersion), "Only curated tag vocabulary version 1 is supported.");
        ArgumentNullException.ThrowIfNull(modTags);
        if (modTags.Select(item => item.Tag).Distinct(StringComparer.OrdinalIgnoreCase).Count() != modTags.Count)
            throw new ArgumentException("Each curated tag must be unique.", nameof(modTags));
        SpecVersion = specVersion;
        ModTags = new ReadOnlyCollection<CuratedTag>(modTags.ToArray());
    }

    public IReadOnlyList<CuratedTag> GetTags(ContentType contentType) => contentType switch
    {
        ContentType.Mod or ContentType.ModLoader or ContentType.ModPack => ModTags,
        _ => Array.Empty<CuratedTag>(),
    };
}
