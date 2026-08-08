using Borea.Core.ModLoaders;
using Borea.Core.Mods;

namespace Borea.Core.Tests.Mods;

public sealed class ModMetadataTests
{
    private static ModMetadata Build(
        string modId = "test-mod",
        string source = "TestSource",
        string name = "Test Mod",
        IReadOnlyList<string>? authors = null,
        string license = "MIT",
        IReadOnlyDictionary<string, string>? links = null,
        ContentType type = ContentType.Mod,
        LoaderRequirement? loader = null,
        string? supersededBy = null) =>
        new(
            specVersion: 1,
            modId: modId,
            source: source,
            name: name,
            authors: authors ?? new[] { "Author" },
            abstractText: "Abstract.",
            license: license,
            links: links ?? TestFixtures.SampleLinks(),
            gameMin: "2026.7.4.2131",
            type: type,
            loader: loader,
            supersededBy: supersededBy);

    [Fact]
    public void Constructor_ValidInput_SetsAllProperties()
    {
        var metadata = Build();

        Assert.Equal("test-mod", metadata.ModId);
        Assert.Equal(ContentType.Mod, metadata.Type);
        Assert.Equal("TestSource", metadata.Source);
        Assert.Equal("https://forums.example/thread/1", metadata.ForumUrl);
        Assert.Null(metadata.Description);
        Assert.Null(metadata.Releases);
        Assert.Null(metadata.Loader);
        Assert.Empty(metadata.Dependencies);
        Assert.Empty(metadata.Tags);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CON")]
    [InlineData(".hidden")]
    public void Constructor_InvalidModId_ThrowsArgumentException(string? modId)
    {
        Assert.Throws<ArgumentException>(() => Build(modId: modId!));
    }

    [Fact]
    public void Constructor_MissingForumsLink_ThrowsArgumentException()
    {
        var links = new Dictionary<string, string> { ["repository"] = "https://example.com/repo" };

        Assert.Throws<ArgumentException>(() => Build(links: links));
    }

    [Fact]
    public void Constructor_ForumsLinkWithAuthoredCasing_IsAccepted()
    {
        var links = new Dictionary<string, string> { ["Forums"] = "https://forums.example/thread/2" };

        var metadata = Build(links: links);

        Assert.Equal("https://forums.example/thread/2", metadata.ForumUrl);
    }

    [Fact]
    public void Constructor_LinkKeysCollidingByCase_ThrowsArgumentException()
    {
        var links = new Dictionary<string, string>
        {
            ["forums"] = "https://forums.example/thread/1",
            ["Forums"] = "https://forums.example/thread/2",
        };

        Assert.Throws<ArgumentException>(() => Build(links: links));
    }

    [Fact]
    public void Constructor_PackType_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Build(type: ContentType.ModPack));
    }

    [Fact]
    public void Constructor_LoaderOnNonModType_ThrowsArgumentException()
    {
        var loader = new LoaderRequirement("StarMap", ModVersion.Parse("0.4.5"));

        Assert.Throws<ArgumentException>(() => Build(type: ContentType.ModLoader, loader: loader));
    }

    [Fact]
    public void Constructor_EmptyAuthors_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Build(authors: Array.Empty<string>()));
    }

    [Fact]
    public void Constructor_InvalidSupersededBy_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Build(supersededBy: "not a valid id"));
    }
}
