using Borea.Core.Mods;
using Borea.Core.ModPacks;

namespace Borea.Core.Tags;

public static class ContentTagFilter
{
    public static IReadOnlyList<ModMetadata> Filter(IEnumerable<ModMetadata> listings, CuratedTagVocabulary vocabulary, ContentType contentType, IEnumerable<string>? selectedTags = null, bool includeOther = false)
    {
        ArgumentNullException.ThrowIfNull(listings);
        ArgumentNullException.ThrowIfNull(vocabulary);
        var selected = selectedTags?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var curated = vocabulary.GetTags(contentType).Select(item => item.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return listings.Where(listing => listing.Type == contentType).Where(listing => Matches(listing, selected, curated, includeOther)).ToArray();
    }

    public static IReadOnlyList<ModPackMetadata> Filter(IEnumerable<ModPackMetadata> packs, CuratedTagVocabulary vocabulary, IEnumerable<string>? selectedTags = null, bool includeOther = false)
    {
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(vocabulary);
        var selected = selectedTags?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var curated = vocabulary.GetTags(ContentType.ModPack).Select(item => item.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return packs.Where(pack => Matches(pack.Tags, selected, curated, includeOther)).ToArray();
    }

    public static bool MatchesSearch(ModMetadata listing, string query)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(query);
        return Contains(listing.ModId, query) || Contains(listing.Name, query) || Contains(listing.Abstract, query) || Contains(listing.Description, query) || listing.Tags.Any(tag => Contains(tag, query));
    }

    public static bool MatchesSearch(ModPackMetadata pack, string query)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(query);
        return Contains(pack.ModPackId, query)
            || Contains(pack.Name, query)
            || Contains(pack.Abstract, query)
            || Contains(pack.Description, query)
            || pack.Authors.Any(author => Contains(author, query))
            || pack.Tags.Any(tag => Contains(tag, query));
    }

    private static bool Matches(ModMetadata listing, IReadOnlySet<string> selected, IReadOnlySet<string> curated, bool includeOther)
    {
        if (selected.Count == 0 && !includeOther)
            return true;
        return Matches(listing.Tags, selected, curated, includeOther);
    }

    private static bool Matches(IReadOnlyList<string> tags, IReadOnlySet<string> selected, IReadOnlySet<string> curated, bool includeOther) =>
        tags.Any(selected.Contains) || (includeOther && !tags.Any(curated.Contains));

    private static bool Contains(string? value, string query) => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
}
