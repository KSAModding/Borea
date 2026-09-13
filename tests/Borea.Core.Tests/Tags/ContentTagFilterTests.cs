using Borea.Core.Mods;
using Borea.Core.ModPacks;
using Borea.Core.Tags;

namespace Borea.Core.Tests.Tags;

public sealed class ContentTagFilterTests
{
    private static readonly CuratedTagVocabulary Vocabulary = new(1,
    [
        new CuratedTag("parts", "Parts", "New parts."),
        new CuratedTag("tools", "Tools", "Tools for players and modders."),
    ]);

    [Fact]
    public void Filter_OneSelectedTag_ReturnsMatchingListings()
    {
        var result = ContentTagFilter.Filter(Listings(), Vocabulary, ContentType.Mod, ["parts"]);
        Assert.Equal("parts-mod", Assert.Single(result).ModId);
    }

    [Fact]
    public void Filter_TwoSelectedTags_ReturnsListingsThatMatchEitherTag()
    {
        var result = ContentTagFilter.Filter(Listings(), Vocabulary, ContentType.Mod, ["parts", "tools"]);
        Assert.Equal(["parts-mod", "tools-mod"], result.Select(item => item.ModId));
    }

    [Fact]
    public void Filter_Other_ReturnsListingsWithoutACuratedTag()
    {
        var result = ContentTagFilter.Filter(Listings(), Vocabulary, ContentType.Mod, includeOther: true);
        Assert.Equal(["free-form-mod", "untagged-mod"], result.Select(item => item.ModId));
    }

    [Fact]
    public void MatchesSearch_MatchesCuratedAndFreeFormTags()
    {
        Assert.True(ContentTagFilter.MatchesSearch(Create("test", "parts", "custom-tag"), "parts"));
        Assert.True(ContentTagFilter.MatchesSearch(Create("test", "parts", "custom-tag"), "custom"));
    }

    [Fact]
    public void Filter_ModPacksSupportsCuratedTagsAndOther()
    {
        var curated = CreatePack("curated", "parts");
        var other = CreatePack("other", "custom-tag");

        Assert.Equal("curated", Assert.Single(ContentTagFilter.Filter([curated, other], Vocabulary, ["parts"])).ModPackId);
        Assert.Equal("other", Assert.Single(ContentTagFilter.Filter([curated, other], Vocabulary, includeOther: true)).ModPackId);
        Assert.True(ContentTagFilter.MatchesSearch(other, "custom"));
    }

    private static ModMetadata[] Listings() =>
    [
        Create("parts-mod", "parts"),
        Create("tools-mod", "tools"),
        Create("free-form-mod", "automation"),
        Create("untagged-mod"),
        Create("loader", "tools", type: ContentType.ModLoader),
    ];

    private static ModMetadata Create(string id, string? firstTag = null, string? secondTag = null, ContentType type = ContentType.Mod) => new(
        1, id, "test", id, ["Author"], "Test content.", "MIT",
        new Dictionary<string, string> { ["forums"] = "https://forums.example/thread/1" },
        "2026.9", type,
        new[] { firstTag, secondTag }.Where(tag => tag is not null).Select(tag => tag!).ToArray());

    private static ModPackMetadata CreatePack(string id, string tag) => new(
        1, id, "test", id, ["Author"], "Test pack.", "CC0-1.0",
        new Dictionary<string, string> { ["forums"] = "https://forums.example/thread/1" },
        "2026.9", ModVersion.Parse("1.0.0"), DateTimeOffset.UnixEpoch,
        [new ModPackEntry("test-mod", ModVersion.Parse("1.0.0"))], [tag]);
}
