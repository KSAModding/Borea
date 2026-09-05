using Borea.Core.ModPacks;
using Borea.Core.Mods;

namespace Borea.Core.Tests.ModPacks;

public sealed class ModPackMetadataTests
{
    private static ModPackEntry Entry(string contentId, string version = "1.0.0") =>
        new(contentId, ModVersion.Parse(version));

    private static IReadOnlyDictionary<string, string> SampleLinks() =>
        new Dictionary<string, string> { ["forums"] = "https://forums.example/thread/1" };

    private static ModPackMetadata Build(
        string modPackId = "test-pack",
        string source = "TestSource",
        string name = "Test Pack",
        IReadOnlyList<string>? authors = null,
        string license = "CC0-1.0",
        IReadOnlyDictionary<string, string>? links = null,
        string gameMin = "2026.7",
        string? supersededBy = null,
        IReadOnlyList<ModPackEntry>? mods = null,
        IReadOnlyList<ModPackEntry>? vehicles = null,
        IReadOnlyList<ModPackEntry>? saves = null,
        int specVersion = SpecVersions.Highest) =>
        new(
            specVersion: specVersion,
            modPackId: modPackId,
            source: source,
            name: name,
            authors: authors ?? new[] { "Author" },
            abstractText: "Abstract.",
            license: license,
            links: links ?? SampleLinks(),
            gameMin: gameMin,
            version: ModVersion.Parse("1.0.0"),
            releasedAt: DateTimeOffset.UtcNow,
            mods: mods ?? new[] { Entry("some-mod") },
            supersededBy: supersededBy,
            vehicles: vehicles,
            saves: saves);

    [Fact]
    public void Constructor_ValidInput_SetsAllProperties()
    {
        var pack = Build();

        Assert.Equal("test-pack", pack.ModPackId);
        Assert.Equal("TestSource", pack.Source);
        Assert.Equal("Test Pack", pack.Name);
        Assert.Equal(new[] { "Author" }, pack.Authors);
        Assert.Equal("Abstract.", pack.Abstract);
        Assert.Equal("CC0-1.0", pack.License);
        Assert.Equal("https://forums.example/thread/1", pack.ForumUrl);
        Assert.Equal("2026.7", pack.GameMin);
        Assert.Equal(ModVersion.Parse("1.0.0"), pack.Version);
        Assert.Equal(ModStatus.Active, pack.Status);
        Assert.Single(pack.Mods);
        Assert.Null(pack.Description);
        Assert.Null(pack.GameMax);
        Assert.Null(pack.Os);
        Assert.Null(pack.Changelog);
        Assert.Empty(pack.Tags);
        Assert.Empty(pack.Vehicles);
        Assert.Empty(pack.Saves);
    }

    [Fact]
    public void Constructor_MonthFormGameMin_IsAccepted()
    {
        Assert.Equal("2026.7", Build(gameMin: "2026.7").GameMin);
    }

    [Fact]
    public void Constructor_VehiclesAndSaves_UseTheSameEntryShape()
    {
        var pack = Build(
            vehicles: new[] { Entry("ReusableBoosterDemo", "1.2.0") },
            saves: new[] { Entry("ApolloRecreation") });

        Assert.Equal("ReusableBoosterDemo", pack.Vehicles[0].ContentId);
        Assert.Equal(ModVersion.Parse("1.2.0"), pack.Vehicles[0].Version);
        Assert.Equal("ApolloRecreation", pack.Saves[0].ContentId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CON")]
    [InlineData(".hidden")]
    [InlineData("not a valid id")]
    public void Constructor_InvalidModPackId_ThrowsArgumentException(string? modPackId)
    {
        // A pack id follows the same rules a mod id does.
        Assert.Throws<ArgumentException>(() => Build(modPackId: modPackId!));
    }

    [Fact]
    public void Constructor_MissingForumsLink_ThrowsArgumentException()
    {
        var links = new Dictionary<string, string> { ["homepage"] = "https://example.com" };

        Assert.Throws<ArgumentException>(() => Build(links: links));
    }

    [Fact]
    public void Constructor_ForumsLinkWithAuthoredCasing_IsAccepted()
    {
        var links = new Dictionary<string, string> { ["Forums"] = "https://forums.example/thread/2" };

        var pack = Build(links: links);

        Assert.Equal("https://forums.example/thread/2", pack.ForumUrl);
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
    public void Constructor_EmptyAuthors_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Build(authors: Array.Empty<string>()));
    }

    [Fact]
    public void Constructor_InvalidSupersededBy_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Build(supersededBy: "not a valid id"));
    }

    [Fact]
    public void Constructor_EmptyModsList_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Build(mods: Array.Empty<ModPackEntry>()));
    }

    [Fact]
    public void Constructor_NullModsList_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new ModPackMetadata(
            SpecVersions.Highest, "test-pack", "TestSource", "Test Pack", new[] { "Author" },
            "Abstract.", "CC0-1.0", SampleLinks(), "2026.7", ModVersion.Parse("1.0.0"),
            DateTimeOffset.UtcNow, null!));
    }

    [Fact]
    public void Constructor_SameContentPinnedTwice_ThrowsArgumentException()
    {
        var mods = new[] { Entry("some-mod", "1.0.0"), Entry("SOME-MOD", "2.0.0") };

        Assert.Throws<ArgumentException>(() => Build(mods: mods));
    }

    [Fact]
    public void Constructor_SameContentPinnedAcrossTwoSections_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            Build(mods: new[] { Entry("shared-id") }, saves: new[] { Entry("shared-id") }));
    }

    [Fact]
    public void Constructor_SameContentPinnedInModsAndVehicles_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            Build(mods: new[] { Entry("shared-id") }, vehicles: new[] { Entry("SHARED-ID") }));

        Assert.Equal("vehicles", exception.ParamName);
    }

    [Fact]
    public void Constructor_DefaultEntry_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Build(mods: new ModPackEntry[1]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_SpecVersionBelowOne_ThrowsArgumentOutOfRangeException(int specVersion)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(specVersion: specVersion));
    }

    [Fact]
    public void Constructor_SpecVersionAboveHighest_IsAccepted()
    {
        var pack = Build(specVersion: SpecVersions.Highest + 1);

        Assert.True(SpecVersions.IsAboveHighest(pack.SpecVersion));
    }

    [Fact]
    public void RepeatedPin_NamesTheSectionItIsIn()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            Build(mods: new[] { Entry("shared-id") }, saves: new[] { Entry("shared-id") }));

        Assert.Equal("saves", exception.ParamName);
    }
}
