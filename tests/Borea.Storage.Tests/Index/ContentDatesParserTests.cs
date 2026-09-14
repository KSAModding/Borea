using Borea.Core.Index;
using Borea.Storage.Index;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Index;

public sealed class ContentDatesParserTests : IDisposable
{
    private const string BothDates = """
        "published_at": "2026-08-02T13:18:41Z", "updated_at": "2026-09-02T10:14:05Z"
        """;

    private static readonly DateTimeOffset Published = new(2026, 8, 2, 13, 18, 41, TimeSpan.Zero);
    private static readonly DateTimeOffset Updated = new(2026, 9, 2, 10, 14, 5, TimeSpan.Zero);

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;

    public ContentDatesParserTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
    }

    [Fact]
    public void Parse_BothDates_ReadsThemOnTheListing()
    {
        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", BothDates)));

        var listing = Assert.Single(result.ValidListings);
        Assert.Equal(Published, listing.PublishedAt);
        Assert.Equal(TimeSpan.Zero, listing.PublishedAt!.Value.Offset);
        Assert.Equal(Updated, listing.UpdatedAt);
        Assert.Empty(listing.DatesErrors);
    }

    [Fact]
    public void Parse_NoDates_LeavesBothNull()
    {
        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", null)));

        var listing = Assert.Single(result.ValidListings);
        Assert.Null(listing.PublishedAt);
        Assert.Null(listing.UpdatedAt);
        Assert.Empty(listing.DatesErrors);
    }

    [Fact]
    public void Parse_OnlyPublishedAt_LeavesUpdatedAtNull()
    {
        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", "\"published_at\": \"2026-08-02T13:18:41Z\"")));

        var listing = Assert.Single(result.ValidListings);
        Assert.Equal(Published, listing.PublishedAt);
        Assert.Null(listing.UpdatedAt);
        Assert.Empty(listing.DatesErrors);
    }

    [Fact]
    public void Parse_FractionalSeconds_AreKept()
    {
        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", "\"published_at\": \"2026-08-02T13:18:41.250Z\"")));

        Assert.Equal(Published.AddMilliseconds(250), Assert.Single(result.ValidListings).PublishedAt);
    }

    [Theory]
    [InlineData("\"2026-09-02\"", "'2026-09-02' is not an ISO 8601 UTC timestamp")]
    [InlineData("\"2026-09-02T10:14:05+02:00\"", "is not an ISO 8601 UTC timestamp")]
    [InlineData("\"2026-09-02T10:14:05\"", "is not an ISO 8601 UTC timestamp")]
    [InlineData("\"yesterday\"", "is not an ISO 8601 UTC timestamp")]
    [InlineData("12", "must be a string, but was Number")]
    [InlineData("null", "must be a string, but was Null")]
    public void Parse_InvalidUpdatedAt_DropsOnlyThatDate(string rawValue, string reason)
    {
        var dates = $"\"published_at\": \"2026-08-02T13:18:41Z\", \"updated_at\": {rawValue}";

        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", dates)));

        var listing = Assert.Single(result.ValidListings);
        Assert.NotNull(listing.Authored);
        Assert.Single(listing.ValidReleases);
        Assert.Equal(Published, listing.PublishedAt);
        Assert.Null(listing.UpdatedAt);
        var error = Assert.Single(listing.DatesErrors);
        Assert.Equal("test-mod", error.Id);
        Assert.Null(error.Version);
        Assert.Contains("The updated_at value is unreadable.", error.Reason);
        Assert.Contains(reason, error.Reason);
    }

    [Fact]
    public void Parse_InvalidPublishedAt_KeepsUpdatedAt()
    {
        var dates = "\"published_at\": \"not a date\", \"updated_at\": \"2026-09-02T10:14:05Z\"";

        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", dates)));

        var listing = Assert.Single(result.ValidListings);
        Assert.Null(listing.PublishedAt);
        Assert.Equal(Updated, listing.UpdatedAt);
        Assert.Contains("The published_at value is unreadable.", Assert.Single(listing.DatesErrors).Reason);
    }

    [Fact]
    public void Parse_UpdatedBeforePublished_DropsBothDates()
    {
        var dates = "\"published_at\": \"2026-09-02T10:14:05Z\", \"updated_at\": \"2026-08-02T13:18:41Z\"";

        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", dates)));

        var listing = Assert.Single(result.ValidListings);
        Assert.Null(listing.PublishedAt);
        Assert.Null(listing.UpdatedAt);
        Assert.Contains("is before the published_at value", Assert.Single(listing.DatesErrors).Reason);
    }

    [Fact]
    public void Parse_MalformedDatesBesideAValidSibling_KeepTheSiblingDates()
    {
        var listings = Listing("broken-dates", "\"published_at\": 5") + "," + Listing("test-mod", BothDates);

        var result = SnapshotParser.Parse(Snapshot(listings));

        Assert.Empty(result.MalformedListings);
        var broken = result.ValidListings.Single(listing => listing.Id == "broken-dates");
        Assert.Null(broken.PublishedAt);
        Assert.Single(broken.DatesErrors);
        var sibling = result.ValidListings.Single(listing => listing.Id == "test-mod");
        Assert.Equal(Updated, sibling.UpdatedAt);
        Assert.Empty(sibling.DatesErrors);
    }

    [Fact]
    public void Parse_Tombstones_CarryNoDates()
    {
        var listing = $$"""{ "id": "removed-mod", "index_status": { "state": "delisted" }, {{BothDates}} }""";
        var pack = $$"""{ "id": "removed-pack", "index_status": { "state": "delisted" }, {{BothDates}} }""";

        var result = SnapshotParser.Parse(Snapshot(listing, pack));

        var tombstone = Assert.Single(result.ValidListings);
        Assert.Null(tombstone.PublishedAt);
        Assert.Null(tombstone.UpdatedAt);
        Assert.Empty(tombstone.DatesErrors);
        var packTombstone = Assert.Single(result.ValidPacks);
        Assert.Null(packTombstone.PublishedAt);
        Assert.Null(packTombstone.UpdatedAt);
        Assert.Empty(packTombstone.DatesErrors);
    }

    [Fact]
    public void Parse_PackDates_ReadThemOnThePack()
    {
        var result = SnapshotParser.Parse(Snapshot("", Pack(BothDates)));

        var pack = Assert.Single(result.ValidPacks);
        Assert.Single(pack.ValidVersions);
        Assert.Equal(Published, pack.PublishedAt);
        Assert.Equal(Updated, pack.UpdatedAt);
        Assert.Empty(pack.DatesErrors);
    }

    [Fact]
    public void Parse_PackWithOnlyPublishedAtAndABadUpdatedAt_KeepsThePack()
    {
        var result = SnapshotParser.Parse(Snapshot("", Pack("\"published_at\": \"2026-08-02T13:18:41Z\", \"updated_at\": []")));

        var pack = Assert.Single(result.ValidPacks);
        Assert.Single(pack.ValidVersions);
        Assert.Equal(Published, pack.PublishedAt);
        Assert.Null(pack.UpdatedAt);
        Assert.Equal("test-pack", Assert.Single(pack.DatesErrors).Id);
    }

    [Fact]
    public async Task ReadAsync_Dates_AttachToTheListingAndThePack()
    {
        await WriteIndexAsync(Snapshot(Listing("test-mod", BothDates), Pack("\"published_at\": \"2026-08-02T13:18:41Z\"")));

        var snapshot = await new ContentIndexReader(_paths).ReadAsync();

        var listing = Assert.Single(snapshot.Listings);
        Assert.Equal(Published, listing.PublishedAt);
        Assert.Equal(Updated, listing.UpdatedAt);
        var pack = Assert.Single(snapshot.Packs);
        Assert.Equal(Published, pack.PublishedAt);
        Assert.Null(pack.UpdatedAt);
        Assert.Empty(snapshot.Diagnostics);
    }

    [Fact]
    public async Task ReadAsync_MalformedDates_KeepTheContentAndAddDiagnostics()
    {
        await WriteIndexAsync(Snapshot(Listing("test-mod", "\"updated_at\": \"2026-09-02\""), Pack("\"published_at\": false")));

        var snapshot = await new ContentIndexReader(_paths).ReadAsync();

        Assert.Single(Assert.Single(snapshot.Listings).Releases);
        Assert.Single(Assert.Single(snapshot.Packs).Versions);
        Assert.Equal(2, snapshot.Diagnostics.Count);
        Assert.All(snapshot.Diagnostics, diagnostic =>
        {
            Assert.Equal(ContentIndexDiagnosticKind.Malformed, diagnostic.Kind);
            Assert.Equal(ContentIndexDiagnosticScope.Dates, diagnostic.Scope);
            Assert.Null(diagnostic.Version);
        });
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Id == "test-mod");
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Id == "test-pack");
    }

    private static string Listing(string id, string? dates)
    {
        var datesJson = dates is null ? string.Empty : dates + ",";
        return $$"""
            {
                "id": "{{id}}",
                {{datesJson}}
                "authored": {
                    "spec_version": 1,
                    "id": "{{id}}",
                    "type": "mod",
                    "name": "Test Mod",
                    "authors": ["Test Author"],
                    "abstract": "A mod used for testing.",
                    "license": "MIT",
                    "compatibility": { "game_min": "2026.7.4.2131" },
                    "links": { "forums": "https://forums.example/thread/1" }
                },
                "releases": [{
                    "spec_version": 1,
                    "id": "{{id}}",
                    "type": "mod",
                    "version": "1.0.0",
                    "version_scheme": "semver",
                    "release_status": "stable",
                    "release_date": "2026-08-08T12:00:00Z",
                    "game_min": "2026.7.4.2131",
                    "game_min_revision": 2131,
                    "download": { "url": "https://example.com/mod.zip", "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "size": 1024, "content_type": "application/zip" },
                    "install_size": 2048,
                    "dependencies": []
                }]
            }
            """;
    }

    private static string Pack(string dates) => $$"""
        {
            "id": "test-pack",
            {{dates}},
            "versions": [{
                "authored": {
                    "spec_version": 1,
                    "id": "test-pack",
                    "type": "modpack",
                    "name": "Test Pack",
                    "authors": ["Author"],
                    "abstract": "Abstract.",
                    "license": "CC0-1.0",
                    "version": "1.0.0",
                    "released_at": "2026-08-02T13:18:41Z",
                    "links": { "forums": "https://forums.example/thread/1" },
                    "compatibility": { "game_min": "2026.7" },
                    "mods": [ { "id": "some-mod", "version": "1.0.0" } ]
                }
            }]
        }
        """;

    private static string Snapshot(string listings, string packs = "") => $$"""
        {
            "snapshot_version": 1,
            "listings": [{{listings}}],
            "packs": [{{packs}}],
            "game_versions": { "spec_version": 1, "source": "master-server", "versions": ["2026.9.7.5402"] }
        }
        """;

    private async Task WriteIndexAsync(string content)
    {
        var path = _paths.GetIndexPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
