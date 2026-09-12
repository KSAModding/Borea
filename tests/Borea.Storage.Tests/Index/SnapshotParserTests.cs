using Borea.Storage.Index;

namespace Borea.Storage.Tests.Index;

public sealed class SnapshotParserTests
{
    private const string GameVersionsJson =
        """
        "game_versions": { "spec_version": 1, "source": "master-server", "versions": ["2026.9.7.5402"] }
        """;

    private const string ValidAuthoredJson = """
        {
            "spec_version": 1,
            "id": "test-mod",
            "type": "mod",
            "name": "Test Mod",
            "authors": ["Test Author"],
            "abstract": "A mod used for testing.",
            "license": "MIT",
            "compatibility": { "game_min": "2026.7.4.2131" },
            "links": { "forums": "https://forums.example/thread/1" }
        }
        """;

    private const string ValidReleaseJson = """
        {
            "spec_version": 1,
            "id": "test-mod",
            "type": "mod",
            "version": "1.0.0",
            "version_scheme": "semver",
            "release_status": "stable",
            "release_date": "2026-08-08T12:00:00Z",
            "game_min": "2026.7.4.2131",
            "game_min_revision": 2131,
            "download": { "url": "https://example.com/mod.zip", "sha256": "AAAA", "size": 1024, "content_type": "application/zip" },
            "install_size": 2048,
            "dependencies": []
        }
        """;

    private const string BrokenReleaseJson = """
        {
            "spec_version": 1,
            "id": "test-mod",
            "type": "mod",
            "version": "2.0.0",
            "version_scheme": "semver",
            "release_status": "stable",
            "release_date": "2026-08-08T12:00:00Z",
            "game_min": "2026.7.4.2131",
            "game_min_revision": 2131,
            "install_size": 2048,
            "dependencies": []
        }
        """;

    private const string PackAuthoredJson = """
        {
            "spec_version": 1,
            "id": "test-pack",
            "type": "modpack",
            "name": "Test Pack",
            "authors": ["Author"],
            "abstract": "Abstract.",
            "license": "CC0-1.0",
            "version": "1.0.0",
            "released_at": "2026-08-08T12:00:00Z",
            "links": { "forums": "https://forums.example/thread/1" },
            "compatibility": { "game_min": "2026.7" },
            "mods": [ { "id": "some-mod", "version": "1.0.0" } ]
        }
        """;

    private static string Snapshot(string listings, string packs, int snapshotVersion = 1) => $$"""
        {
            "snapshot_version": {{snapshotVersion}},
            "listings": [{{listings}}],
            "packs": [{{packs}}],
            {{GameVersionsJson}}
        }
        """;

    [Fact]
    public void Parse_ValidListingWithOneGoodAndOneBadRelease_SplitsThem()
    {
        var listing = $$"""
            {
                "id": "test-mod",
                "authored": {{ValidAuthoredJson}},
                "releases": [{{ValidReleaseJson}}, {{BrokenReleaseJson}}]
            }
            """;

        var result = SnapshotParser.Parse(Snapshot(listing, ""));

        var parsed = Assert.Single(result.ValidListings);
        Assert.Equal("test-mod", parsed.Id);
        Assert.Single(parsed.ValidReleases);
        Assert.Single(parsed.RejectedReleases);
        Assert.Empty(result.MalformedListings);
    }

    [Fact]
    public void Parse_TombstoneListing_IsValidWithNoAuthoredData()
    {
        var listing = """{ "id": "removed-mod", "index_status": { "state": "delisted" } }""";

        var result = SnapshotParser.Parse(Snapshot(listing, ""));

        var parsed = Assert.Single(result.ValidListings);
        Assert.Null(parsed.Authored);
        Assert.NotNull(parsed.IndexStatus);
    }

    [Fact]
    public void Parse_ListingWithNoAuthoredReleasesOrIndexStatus_IsMalformed()
    {
        var listing = """{ "id": "half-baked-mod" }""";

        var result = SnapshotParser.Parse(Snapshot(listing, ""));

        Assert.Empty(result.ValidListings);
        var rejected = Assert.Single(result.MalformedListings);
        Assert.Equal("half-baked-mod", rejected.Id);
    }

    [Fact]
    public void Parse_ListingWithEmptyReleasesArray_IsStillValid()
    {
        // A freshly authored listing with no releases published yet is a
        // valid state, not a rejection.
        var listing = $$"""{ "id": "test-mod", "authored": {{ValidAuthoredJson}}, "releases": [] }""";

        var result = SnapshotParser.Parse(Snapshot(listing, ""));

        var parsed = Assert.Single(result.ValidListings);
        Assert.Empty(parsed.ValidReleases);
    }

    [Fact]
    public void Parse_ListingWithNewerSpecVersion_IsUnknownNotMalformed()
    {
        var authoredWithNewerSpec = ValidAuthoredJson.Replace("\"spec_version\": 1", "\"spec_version\": 2");
        var listing = $$"""{ "id": "future-mod", "authored": {{authoredWithNewerSpec}} }""";

        var result = SnapshotParser.Parse(Snapshot(listing, ""));

        Assert.Empty(result.ValidListings);
        Assert.Empty(result.MalformedListings);
        var unknown = Assert.Single(result.UnknownListings);
        Assert.Equal("future-mod", unknown.Id);
        Assert.Equal(2, unknown.SpecVersion);
    }

    [Fact]
    public void Parse_ReleaseWithNewerSpecVersion_IsUnknownNotMalformed()
    {
        var futureRelease = ValidReleaseJson.Replace("\"spec_version\": 1", "\"spec_version\": 2");
        var listing = $$"""
            {
                "id": "test-mod",
                "authored": {{ValidAuthoredJson}},
                "releases": [{{futureRelease}}]
            }
            """;

        var result = SnapshotParser.Parse(Snapshot(listing, ""));

        var parsed = Assert.Single(result.ValidListings);
        Assert.Empty(parsed.ValidReleases);
        Assert.Empty(parsed.RejectedReleases);
        var unknown = Assert.Single(parsed.UnknownReleases);
        Assert.Equal(2, unknown.SpecVersion);
    }

    [Fact]
    public void Parse_ListingThatIsNotAnObject_IsMalformedWithNoId()
    {
        var result = SnapshotParser.Parse(Snapshot("42", ""));

        var rejected = Assert.Single(result.MalformedListings);
        Assert.Null(rejected.Id);
    }

    [Fact]
    public void Parse_ValidPack_IsSurfaced()
    {
        var pack = $$"""
            {
                "id": "test-pack",
                "versions": [ { "authored": {{PackAuthoredJson}} } ]
            }
            """;

        var result = SnapshotParser.Parse(Snapshot("", pack));

        var parsed = Assert.Single(result.ValidPacks);
        Assert.Equal("test-pack", parsed.Id);
        Assert.Single(parsed.ValidVersions);
    }

    [Fact]
    public void Parse_TombstonePack_IsValidWithNoVersions()
    {
        var pack = """{ "id": "removed-pack", "index_status": { "state": "delisted" } }""";

        var result = SnapshotParser.Parse(Snapshot("", pack));

        var parsed = Assert.Single(result.ValidPacks);
        Assert.Empty(parsed.ValidVersions);
        Assert.NotNull(parsed.IndexStatus);
    }

    [Fact]
    public void Parse_PackWithNoVersionsAndNoIndexStatus_IsMalformed()
    {
        var pack = """{ "id": "empty-pack" }""";

        var result = SnapshotParser.Parse(Snapshot("", pack));

        var rejected = Assert.Single(result.MalformedPacks);
        Assert.Equal("empty-pack", rejected.Id);
    }

    [Fact]
    public void Parse_PackVersionWithNewerSpecVersion_IsUnknownNotMalformed()
    {
        var futureAuthored = PackAuthoredJson.Replace("\"spec_version\": 1", "\"spec_version\": 2");
        var pack = $$"""
            {
                "id": "test-pack",
                "versions": [ { "authored": {{futureAuthored}} } ]
            }
            """;

        var result = SnapshotParser.Parse(Snapshot("", pack));

        var parsed = Assert.Single(result.ValidPacks);
        Assert.Empty(parsed.ValidVersions);
        var unknown = Assert.Single(parsed.UnknownVersions);
        Assert.Equal(2, unknown.SpecVersion);
    }

    [Fact]
    public void Parse_SnapshotVersionAboveHighest_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => SnapshotParser.Parse(Snapshot("", "", snapshotVersion: 2)));
    }

    [Fact]
    public void Parse_MissingGameVersions_Throws()
    {
        var json = """{ "snapshot_version": 1, "listings": [], "packs": [] }""";

        Assert.Throws<InvalidOperationException>(() => SnapshotParser.Parse(json));
    }

    [Fact]
    public void Parse_MalformedJson_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => SnapshotParser.Parse("{ not valid json"));
    }

    [Fact]
    public void Parse_GameVersionsAreCarriedThrough()
    {
        var result = SnapshotParser.Parse(Snapshot("", ""));

        Assert.Equal("master-server", result.GameVersions.Source);
        Assert.Equal(new[] { "2026.9.7.5402" }, result.GameVersions.Versions);
    }

    [Fact]
    public void Parse_NoSourcesField_LeavesSourcesNull()
    {
        var result = SnapshotParser.Parse(Snapshot("", ""));

        Assert.Null(result.Sources);
    }

    [Fact]
    public void Parse_MultipleListingsAndPacks_KeepsEachIndependent()
    {
        var goodListing = $$"""{ "id": "good-mod", "authored": {{ValidAuthoredJson.Replace("\"test-mod\"", "\"good-mod\"")}} }""";
        var badListing = """{ "id": "bad-mod" }""";
        var goodPack = $$"""{ "id": "good-pack", "versions": [ { "authored": {{PackAuthoredJson}} } ] }""";
        var badPack = """{ "id": "bad-pack" }""";

        var result = SnapshotParser.Parse(Snapshot($"{goodListing}, {badListing}", $"{goodPack}, {badPack}"));

        Assert.Single(result.ValidListings);
        Assert.Single(result.MalformedListings);
        Assert.Single(result.ValidPacks);
        Assert.Single(result.MalformedPacks);
    }
}
