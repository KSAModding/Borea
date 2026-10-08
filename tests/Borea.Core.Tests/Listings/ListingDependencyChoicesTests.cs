using Borea.Core.Dependencies;
using Borea.Core.Index;
using Borea.Core.Listings;
using Borea.Core.Mods;
using static Borea.Core.Tests.ModPacks.PackMemberRepository;

namespace Borea.Core.Tests.Listings;

public sealed class ListingDependencyChoicesTests
{
    [Fact]
    public void Candidates_AreTheListedModsAndLoaders_WithoutTheDelistedOnes()
    {
        var snapshot = Snapshot(
            Mod("Lib", Release("Lib", "1.0.0")),
            new ContentIndexListing("Gone", Listing("Gone", "Gone"), [Release("Gone", "1.0.0")], new IndexStatus(IndexStatusState.Delisted, "delisted")),
            Loader("StarMap"));

        Assert.Equal(["Lib", "StarMap"], ListingDependencyChoices.Candidates(snapshot).Select(listing => listing.Id));
        Assert.NotNull(ListingDependencyChoices.Listing(snapshot, "lib"));
        Assert.Null(ListingDependencyChoices.Listing(snapshot, "Gone"));
        Assert.True(ListingDependencyChoices.IsLoader(ListingDependencyChoices.Listing(snapshot, "StarMap")!));
        Assert.False(ListingDependencyChoices.IsLoader(ListingDependencyChoices.Listing(snapshot, "Lib")!));
    }

    [Fact]
    public void IsHeld_CountsEveryListing_AlsoADelistedOne()
    {
        var snapshot = Snapshot(
            Mod("Lib", Release("Lib", "1.0.0")),
            new ContentIndexListing("Gone", Listing("Gone", "Gone"), [Release("Gone", "1.0.0")], new IndexStatus(IndexStatusState.Delisted, "delisted")));

        Assert.True(ListingDependencyChoices.IsHeld(snapshot, "lib"));
        Assert.True(ListingDependencyChoices.IsHeld(snapshot, "Gone"));
        Assert.False(ListingDependencyChoices.IsHeld(snapshot, "Other"));
    }

    [Fact]
    public void Releases_AreNewestFirst_WithoutTheYankedOnes()
    {
        var listing = Mod("Lib", Release("Lib", "1.0.0"), Release("Lib", "1.2.0-dev.1", ReleaseStatus.Dev), Release("Lib", "1.1.0", yanked: true), Release("Lib", "1.0.5"));

        Assert.Equal(["1.2.0-dev.1", "1.0.5", "1.0.0"], ListingDependencyChoices.Releases(listing).Select(release => release.Version.ToString()));
        Assert.Equal("1.0.5", ListingDependencyChoices.Newest(listing)?.Version.ToString());
        Assert.Equal("0.1.0-dev.1", ListingDependencyChoices.Newest(Mod("Dev", Release("Dev", "0.1.0-dev.1", ReleaseStatus.Dev)))?.Version.ToString());
    }

    [Fact]
    public void Derived_AreTheDerivedEntriesOfTheNewestReleaseThatIsNotYanked()
    {
        var snapshot = Snapshot(Mod(
            "MyMod",
            Stamped("MyMod", "1.0.0", [new ModDependency("Old", ModDependencyKind.Required, source: MetadataSource.Derived)]),
            Stamped("MyMod", "1.1.0", [
                new ModDependency("Lib", ModDependencyKind.Required, source: MetadataSource.Derived),
                new ModDependency("Extra", ModDependencyKind.Optional, source: MetadataSource.Derived),
                new ModDependency("Bounded", ModDependencyKind.Required, ModVersion.Parse("1.0.0"), source: MetadataSource.Authored),
            ]),
            Stamped("MyMod", "1.2.0", [new ModDependency("Bad", ModDependencyKind.Required, source: MetadataSource.Derived)], yanked: true)));

        var derived = ListingDependencyChoices.Derived(snapshot, "mymod");

        Assert.Equal("1.1.0", derived?.Release);
        Assert.Equal([new ListingDeclaredDependency("Lib", "required"), new ListingDeclaredDependency("Extra", "optional")], derived?.Dependencies);
        Assert.Null(ListingDependencyChoices.Derived(snapshot, "Other"));
    }

    [Fact]
    public void FromModToml_GivesTheKindsTheStamperGives()
    {
        var declared = ListingDependencyChoices.FromModToml("v1.0.0", [new LocalModDependency("Lib", optional: false), new LocalModDependency("Extra", optional: true)]);

        Assert.Equal("v1.0.0", declared.Release);
        Assert.Equal([new ListingDeclaredDependency("Lib", "required"), new ListingDeclaredDependency("Extra", "optional")], declared.Dependencies);
    }

    private static ContentIndexSnapshot Snapshot(params ContentIndexListing[] listings) => new(1, listings, [], null, []);

    private static ContentIndexListing Mod(string id, params ModVersionMetadata[] releases) => new(id, Listing(id, id), releases, null);

    private static ContentIndexListing Loader(string id) => new(
        id,
        new ModMetadata(1, id, "index", id, ["Maxi"], id + " abstract.", "MIT", new Dictionary<string, string> { ["forums"] = $"https://forums.example/{id}" }, "2026.7.4.2131", ContentType.ModLoader),
        [Release(id, "0.4.6")],
        null);

    private static ModVersionMetadata Stamped(string id, string version, IReadOnlyList<ModDependency> dependencies, bool yanked = false) => new(
        1,
        id,
        ModVersion.Parse(version),
        ReleaseStatus.Stable,
        DateTimeOffset.UnixEpoch,
        "2026.7.4.2131",
        2131,
        new DownloadInfo($"https://example.com/{id}/{version}.zip", new string('A', 64), 1, "application/zip"),
        1,
        dependencies,
        yanked: yanked);
}
