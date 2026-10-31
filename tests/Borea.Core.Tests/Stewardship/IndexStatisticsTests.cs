using System.Text.Json;
using Borea.Core.Dependencies;
using Borea.Core.Index;
using Borea.Core.ModLoaders;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Stewardship;

namespace Borea.Core.Tests.Stewardship;

public sealed class IndexStatisticsTests
{
    private const string Forums = "https://forums.ahwoo.com/threads/a-listing.1/";

    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly ContentImages Icon = new(new IconImage("https://example.com/icon.png", new string('A', 64), 256, 256, 1), []);

    [Fact]
    public void GameSupport_CountsOnlyTheMods_AndTakesAReleaseThatBoreaOffersAndThatFitsTheNewestBuild()
    {
        var statistics = From(
            Listing("Fits", releases: [Release("Fits", "1.0.0")]),
            Listing("TooOld", releases: [Release("TooOld", "1.0.0", gameMax: (Older, OlderRevision))]),
            Listing("TooNew", releases: [Release("TooNew", "1.0.0", gameMin: (Newer, NewerRevision))]),
            Listing("OldAndFits", releases: [Release("OldAndFits", "2.0.0", gameMax: (Older, OlderRevision)), Release("OldAndFits", "1.0.0")]),
            Listing("OnlyYanked", releases: [Release("OnlyYanked", "1.0.0", yanked: true)]),
            Listing("OnlyGone", releases: [Release("OnlyGone", "1.0.0", gone: true)]),
            Listing("NoRelease", releases: []),
            Listing("Loader", ContentType.ModLoader, releases: [Release("Loader", "1.0.0", ContentType.ModLoader, gameMax: (Older, OlderRevision))]));

        Assert.Equal(Build, statistics.CurrentBuild?.ToString());
        Assert.Equal(2, statistics.ModsOnCurrentBuild);
        Assert.Equal(["NoRelease", "OnlyGone", "OnlyYanked", "TooNew", "TooOld"], statistics.ModsNotOnCurrentBuild.Select(row => row.Id));
        Assert.Equal(["Loader", "NoRelease", "OnlyGone", "OnlyYanked", "TooNew", "TooOld"], statistics.NoCurrentRelease.Select(row => row.Id));
    }

    [Fact]
    public void SnapshotWithoutABuild_SaysNothingAboutGameSupport()
    {
        var snapshot = new ContentIndexSnapshot(1, [Listing("Mod", releases: [])], [Pack("Pack", "1.0.0")], null, []);

        var statistics = IndexStatistics.From(snapshot, Now);

        Assert.Null(statistics.CurrentBuild);
        Assert.Equal(0, statistics.ModsOnCurrentBuild);
        Assert.Empty(statistics.ModsNotOnCurrentBuild);
        Assert.Empty(statistics.NoCurrentRelease);
    }

    [Fact]
    public void MostDependencies_CountsRequiredAndOptionalApart_AnAnyOfAsOne_AndNoConflict()
    {
        var statistics = From(
            Listing("Wide", releases: [Release("Wide", "1.0.0", dependencies:
            [
                new ModDependency("A", ModDependencyKind.Required),
                ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("B"), new ModDependencyAlternative("C")]),
                new ModDependency("D", ModDependencyKind.Optional),
                new ModDependency("E", ModDependencyKind.Recommends),
                new ModDependency("F", ModDependencyKind.Suggests),
                new ModDependency("G", ModDependencyKind.Conflict),
            ])]),
            Listing("Narrow", releases: [Release("Narrow", "1.0.0", dependencies: [new ModDependency("A", ModDependencyKind.Required)])]),
            Listing("None", releases: [Release("None", "1.0.0")]));

        Assert.Equal(
            [("Wide", 2, 3), ("Narrow", 1, 0)],
            statistics.MostDependencies.Select(row => (row.Id, row.Required, row.Optional)));
    }

    [Fact]
    public void MostDependencies_ReadsTheNewestOfferedRelease_OrTheAuthoredDocumentWithoutOne()
    {
        var one = new ModDependency("One", ModDependencyKind.Required);
        var two = new ModDependency("Two", ModDependencyKind.Required);
        var statistics = From(
            Listing("Released", releases:
            [
                Release("Released", "3.0.0", yanked: true, dependencies: [one, two]),
                Release("Released", "2.0.0", dependencies: [one]),
                Release("Released", "1.0.0", dependencies: [one, two]),
            ]),
            Listing("Unreleased", dependencies: [one, two], releases: []));

        Assert.Equal([("Unreleased", 2), ("Released", 1)], statistics.MostDependencies.Select(row => (row.Id, row.Required)));
    }

    [Fact]
    public void MostDependedOn_CountsEachListingOnce_EachAlternative_NoLoaderField_AndNamesAnIdThatIsNotListed()
    {
        var loader = new LoaderRequirement("Loader", ModVersion.Parse("1.0.0"));
        var statistics = From(
            Listing("Loader", ContentType.ModLoader, releases: [Release("Loader", "1.0.0", ContentType.ModLoader)]),
            Listing("Base", name: "The base", releases: [Release("Base", "1.0.0", loader: loader)]),
            Listing("First", releases: [Release("First", "1.0.0", loader: loader, dependencies:
            [
                new ModDependency("Base", ModDependencyKind.Required),
                new ModDependency("Base", ModDependencyKind.Optional),
                new ModDependency("Unlisted", ModDependencyKind.Optional),
            ])]),
            Listing("Second", releases: [Release("Second", "1.0.0", loader: loader, dependencies:
            [
                new ModDependency("loader", ModDependencyKind.Required),
                ModDependency.OfAlternatives(ModDependencyKind.Recommends, [new ModDependencyAlternative("Base"), new ModDependencyAlternative("Unlisted")]),
            ])]));

        Assert.Equal(
            [("Base", "The base", 1, 1, true), ("Unlisted", "Unlisted", 0, 2, false), ("loader", "Loader", 1, 0, true)],
            statistics.MostDependedOn.Select(row => (row.Id, row.Name, row.Required, row.Optional, row.IsListed)));
        Assert.DoesNotContain("Base", statistics.MostDependencies.Select(row => row.Id));
        Assert.Equal((1, 1), statistics.MostDependencies.Where(row => row.Id == "Second").Select(row => (row.Required, row.Optional)).Single());
    }

    [Fact]
    public void RankedLists_StopAtTheirLength_ButAListOfUpkeepDoesNot()
    {
        var count = IndexStatistics.RankedRows + 1;
        var statistics = From([.. Enumerable.Range(0, count).Select(number => Listing(
            $"Mod{number:D2}",
            downloads: Downloads(number),
            releases: [Release($"Mod{number:D2}", "1.0.0", dependencies: [new ModDependency("Base", ModDependencyKind.Required)])]))]);

        Assert.Equal(IndexStatistics.RankedRows, statistics.MostDependencies.Count);
        Assert.Equal(IndexStatistics.RankedRows, statistics.MostDownloaded.Count);
        Assert.Equal(("Mod10", 10), statistics.MostDownloaded.Select(row => (row.Id, row.Total)).First());
        Assert.Equal(count, statistics.NoImage.Count);
    }

    [Fact]
    public void Upkeep_NamesAListingThatIsNotUpdatedOnlyPastTheLimit_AndNotOneWithoutADate()
    {
        var limit = TimeSpan.FromDays(IndexStatistics.NoUpdateDays);
        var statistics = From(
            Listing("Past", releases: [], updatedAt: Now - limit - TimeSpan.FromSeconds(1)),
            Listing("At", releases: [], updatedAt: Now - limit),
            Listing("NoDate", releases: []));

        Assert.Equal(["Past"], statistics.NoRecentUpdate.Select(row => row.Id));
    }

    [Fact]
    public void Upkeep_NamesAListingWithoutAnImage_AndAcceptsAnIconOrADescriptionImage()
    {
        var description = new ContentImages(null, [new DescriptionImage("shot", "https://example.com/shot.png", new string('A', 64), 16, 16, 1)]);
        var statistics = From(
            Listing("NoImages", releases: []),
            Listing("EmptyImages", images: new ContentImages(null, []), releases: []),
            Listing("HasIcon", images: Icon, releases: []),
            Listing("HasDescriptionImage", images: description, releases: []));

        Assert.Equal(["EmptyImages", "NoImages"], statistics.NoImage.Select(row => row.Id));
    }

    [Fact]
    public void Upkeep_NamesALinkThatIsNoThread_OnlyWhereTheTypeRequiresOne()
    {
        var statistics = From(
            Listing("NoThread", forums: "https://example.com/somewhere", releases: []),
            Listing("HasThread", releases: []),
            Listing("Vehicle", ContentType.Vehicle, forums: null, releases: []),
            Listing("Save", ContentType.Save, forums: null, releases: []));

        Assert.Equal(["NoThread"], statistics.NoForumsThread.Select(row => row.Id));
    }

    [Fact]
    public void Overview_CountsEachTypeAndName_TheMostFirst_AndTheHostsOfTheReleasesTable()
    {
        var hosts = new ReleaseSource([new ReleaseHost("GitHub", "owner/repo"), new ReleaseHost("spacedock", "1")], authority: "GitHub");
        var snapshot = new ContentIndexSnapshot(
            1,
            [
                Listing("ModA", tags: ["parts", "PARTS", "tools"], license: "MIT", releaseSource: hosts, releases: []),
                Listing("ModB", tags: ["tools"], license: "mit", releaseSource: new ReleaseSource([new ReleaseHost("github", "owner/other")]), releases: []),
                Listing("Loader", ContentType.ModLoader, license: "GPL-3.0", releases: []),
            ],
            [Pack("Pack", "1.0.0")],
            null,
            [],
            vehicles: [Listing("Rover", ContentType.Vehicle, releases: [])],
            saves: [Listing("SaveA", ContentType.Save, releases: []), Listing("SaveB", ContentType.Save, releases: [])]);

        var statistics = IndexStatistics.From(snapshot, Now);

        Assert.Equal(
            [(ContentType.Mod, 2), (ContentType.ModLoader, 1), (ContentType.ModPack, 1), (ContentType.Vehicle, 1), (ContentType.Save, 2)],
            statistics.Types.Select(row => (row.Type, row.Count)));
        Assert.Equal([("tools", 2), ("parts", 1)], statistics.Tags.Select(row => (row.Name, row.Count)));
        Assert.Equal([("MIT", 6), ("GPL-3.0", 1)], statistics.Licenses.Select(row => (row.Name, row.Count)));
        Assert.Equal([("github", 2), ("spacedock", 1)], statistics.ReleaseHosts.Select(row => (row.Name, row.Count)));
    }

    [Fact]
    public void AnEntryThatAPlayerCannotSee_DoesNotCount()
    {
        var delisted = new IndexStatus(IndexStatusState.Delisted, "delisted");
        var snapshot = new ContentIndexSnapshot(
            1,
            [
                Listing("Seen", releases: []),
                Listing("Delisted", releases: [], indexStatus: delisted),
                new ContentIndexListing("NoDocument", null, [], null),
            ],
            [
                new ContentIndexPack("DelistedPack", [PackVersion("DelistedPack", "1.0.0")], delisted),
                new ContentIndexPack("RetractedPack", [PackVersion("RetractedPack", "1.0.0", retracted: true)], null),
            ],
            null,
            []);

        var statistics = IndexStatistics.From(snapshot, Now);

        Assert.Equal(1, statistics.Types.Sum(row => row.Count));
    }

    [Fact]
    public void Pack_IsRatedByItsNewestVersionThatIsNotRetracted_WithItsBoundsAndItsPins()
    {
        var retracted = PackVersion("Pinned", "3.0.0", retracted: true, pins: [new ModPackEntry("Fits", ModVersion.Parse("1.0.0"))], images: Icon);
        var snapshot = new ContentIndexSnapshot(
            1,
            [
                Listing("Fits", images: Icon, releases: [Release("Fits", "1.0.0")]),
                Listing("TooOld", images: Icon, releases: [Release("TooOld", "1.0.0", gameMax: (Older, OlderRevision))]),
            ],
            [
                new ContentIndexPack("Pinned", [retracted, PackVersion("Pinned", "2.0.0", pins: [new ModPackEntry("TooOld", ModVersion.Parse("1.0.0"))]), PackVersion("Pinned", "1.0.0", images: Icon)], null),
                new ContentIndexPack("Good", [PackVersion("Good", "1.0.0", pins: [new ModPackEntry("Fits", ModVersion.Parse("1.0.0"))], images: Icon)], null),
            ],
            Versions,
            []);

        var statistics = IndexStatistics.From(snapshot, Now);

        Assert.Equal(["Pinned", "TooOld"], statistics.NoCurrentRelease.Select(row => row.Id));
        Assert.Equal(["Pinned"], statistics.NoImage.Select(row => row.Id));
    }

    [Fact]
    public void NoRow_CarriesAnAuthor()
    {
        var statistics = From(Listing("Mod", tags: ["parts"], downloads: Downloads(1), forums: "https://example.com/somewhere", releases: []));

        Assert.DoesNotContain(AuthorName, JsonSerializer.Serialize(statistics), StringComparison.Ordinal);
    }

    private const string Build = "2026.10.25.5707";
    private const string Newer = "2026.10.26.5708";
    private const int NewerRevision = 5708;
    private const string Older = "2026.10.13.5602";
    private const int OlderRevision = 5602;
    private const string AuthorName = "Some Author";

    private static readonly ContentIndexGameVersions Versions = new(1, "test", ["2026.1.1.1000", Build, Older]);

    private static IndexStatistics From(params ContentIndexListing[] listings) =>
        IndexStatistics.From(new ContentIndexSnapshot(1, listings, [], Versions, []), Now);

    private static ListingDownloadCounts Downloads(long total) => new(total, new Dictionary<string, long> { ["github"] = total }, []);

    private static ContentIndexListing Listing(
        string id,
        ContentType type = ContentType.Mod,
        string? name = null,
        IReadOnlyList<string>? tags = null,
        string license = "MIT",
        string? forums = Forums,
        IReadOnlyList<ModDependency>? dependencies = null,
        ReleaseSource? releaseSource = null,
        IReadOnlyList<ModVersionMetadata>? releases = null,
        ContentImages? images = null,
        ListingDownloadCounts? downloads = null,
        DateTimeOffset? updatedAt = null,
        IndexStatus? indexStatus = null) => new(
            id,
            new ModMetadata(
                1,
                id,
                "index",
                name ?? id,
                [AuthorName],
                "Abstract.",
                license,
                forums is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["forums"] = forums },
                type.IsVehicleOrSave() ? null : "2026.1",
                type,
                tags,
                dependencies: dependencies,
                releases: releaseSource),
            releases ?? [],
            indexStatus,
            downloads,
            images,
            updatedAt: updatedAt);

    private static ModVersionMetadata Release(
        string id,
        string version,
        ContentType type = ContentType.Mod,
        (string Text, int Revision)? gameMin = null,
        (string Text, int Revision)? gameMax = null,
        bool yanked = false,
        bool gone = false,
        LoaderRequirement? loader = null,
        IReadOnlyList<ModDependency>? dependencies = null) => new(
            1,
            id,
            ModVersion.Parse(version),
            ReleaseStatus.Stable,
            DateTimeOffset.UnixEpoch,
            gameMin?.Text ?? "2026.1.1.1000",
            gameMin?.Revision ?? 1000,
            new DownloadInfo($"https://example.com/{id}.zip", new string('A', 64), 1, "application/zip", unavailableSince: gone ? Now : null),
            1,
            dependencies ?? [],
            type,
            gameMax: gameMax?.Text,
            gameMaxRevision: gameMax?.Revision,
            loader: loader,
            yanked: yanked);

    private static ContentIndexPack Pack(string id, string version) => new(id, [PackVersion(id, version, images: Icon)], null);

    private static ContentIndexPackVersion PackVersion(string id, string version, bool retracted = false, IReadOnlyList<ModPackEntry>? pins = null, ContentImages? images = null) => new(
        new ModPackMetadata(
            1,
            id,
            "index",
            id,
            [AuthorName],
            "Abstract.",
            "MIT",
            new Dictionary<string, string> { ["forums"] = Forums },
            "2026.1",
            ModVersion.Parse(version),
            DateTimeOffset.UnixEpoch,
            pins ?? [new ModPackEntry("Member", ModVersion.Parse("1.0.0"))]),
        retracted ? new IndexStatus(IndexStatusState.Retracted, "retracted") : null,
        images);
}
