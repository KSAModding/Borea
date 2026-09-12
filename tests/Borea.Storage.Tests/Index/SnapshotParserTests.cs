using Borea.Core.Index;
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
            "download": { "url": "https://example.com/mod.zip", "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "size": 1024, "content_type": "application/zip" },
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
    public void Parse_CanceledToken_StopsBeforeEntryParsing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            SnapshotParser.Parse(Snapshot("", ""), cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task Parse_CancellationDuringLargeTraversal_Stops()
    {
        var listings = string.Join(",", Enumerable.Range(0, 200_000).Select(index => $$"""{ "id": "mod-{{index}}" }"""));
        using var cancellation = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        var parseTask = Task.Run(() =>
        {
            started.Set();
            return SnapshotParser.Parse(Snapshot(listings, ""), cancellationToken: cancellation.Token);
        });

        started.Wait();
        await Task.Delay(10);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await parseTask);
    }

    [Fact]
    public void Parse_NullSource_PropagatesCallerError()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => SnapshotParser.Parse(Snapshot("", ""), null!));

        Assert.Equal("source", exception.ParamName);
    }

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

        Assert.Equal("master-server", result.GameVersions!.Source);
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

    [Fact]
    public void Parse_InvalidOuterIdBesideValidListing_KeepsValidSibling()
    {
        var invalid = $$"""{ "id": "bad id", "authored": {{ValidAuthoredJson}} }""";
        var validAuthored = ValidAuthoredJson.Replace("test-mod", "good-mod");
        var valid = $$"""{ "id": "good-mod", "authored": {{validAuthored}} }""";

        var result = SnapshotParser.Parse(Snapshot($"{invalid}, {valid}", ""));

        Assert.Equal("good-mod", Assert.Single(result.ValidListings).Id);
        var rejected = Assert.Single(result.MalformedListings);
        Assert.Equal("bad id", rejected.Id);
        Assert.Contains("valid content id", rejected.Reason);
    }

    [Fact]
    public void Parse_AuthoredIdMismatchBesideValidListing_KeepsValidSibling()
    {
        var invalid = $$"""{ "id": "other-mod", "authored": {{ValidAuthoredJson}} }""";
        var valid = $$"""{ "id": "test-mod", "authored": {{ValidAuthoredJson}} }""";

        var result = SnapshotParser.Parse(Snapshot($"{invalid}, {valid}", ""));

        Assert.Single(result.ValidListings);
        var rejected = Assert.Single(result.MalformedListings);
        Assert.Equal("other-mod", rejected.Id);
        Assert.Contains("does not agree", rejected.Reason);
    }

    [Fact]
    public void Parse_InvalidMappedReleaseBesideValidRelease_KeepsValidSiblingAndIdentity()
    {
        var invalidRelease = ValidReleaseJson
            .Replace("\"version\": \"1.0.0\"", "\"version\": \"2.0.0\"")
            .Replace("\"install_size\": 2048", "\"install_size\": -1");
        var listing = $$"""
            {
                "id": "test-mod",
                "authored": {{ValidAuthoredJson}},
                "releases": [{{invalidRelease}}, {{ValidReleaseJson}}]
            }
            """;

        var result = SnapshotParser.Parse(Snapshot(listing, ""));

        var parsed = Assert.Single(result.ValidListings);
        Assert.Equal("1.0.0", Assert.Single(parsed.ValidReleases).Version.ToString());
        var rejected = Assert.Single(parsed.RejectedReleases);
        Assert.Equal("test-mod", rejected.Id);
        Assert.Equal("2.0.0", rejected.Version);
        Assert.Contains("negative", rejected.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_ReleaseIdMismatchBesideValidRelease_KeepsValidSibling()
    {
        var invalidRelease = ValidReleaseJson
            .Replace("\"id\": \"test-mod\"", "\"id\": \"other-mod\"")
            .Replace("\"version\": \"1.0.0\"", "\"version\": \"2.0.0\"");
        var listing = $$"""
            {
                "id": "test-mod",
                "authored": {{ValidAuthoredJson}},
                "releases": [{{invalidRelease}}, {{ValidReleaseJson}}]
            }
            """;

        var result = SnapshotParser.Parse(Snapshot(listing, ""));

        var parsed = Assert.Single(result.ValidListings);
        Assert.Single(parsed.ValidReleases);
        var rejected = Assert.Single(parsed.RejectedReleases);
        Assert.Equal("other-mod", rejected.Id);
        Assert.Equal("2.0.0", rejected.Version);
    }

    [Fact]
    public void Parse_DuplicateReleaseVersions_RejectsEveryConflictingArchive()
    {
        var otherRelease = ValidReleaseJson.Replace("\"version\": \"1.0.0\"", "\"version\": \"2.0.0\"");
        var listing = $$"""
            {
                "id": "test-mod",
                "authored": {{ValidAuthoredJson}},
                "releases": [{{ValidReleaseJson}}, {{ValidReleaseJson}}, {{otherRelease}}]
            }
            """;

        var result = SnapshotParser.Parse(Snapshot(listing, ""));

        var parsed = Assert.Single(result.ValidListings);
        Assert.Equal("2.0.0", Assert.Single(parsed.ValidReleases).Version.ToString());
        Assert.Equal(2, parsed.RejectedReleases.Count);
        Assert.All(parsed.RejectedReleases, rejected => Assert.Equal("1.0.0", rejected.Version));
    }

    [Fact]
    public void Parse_DuplicateIdsAcrossListingsAndPacks_RejectsEveryConflictingEntry()
    {
        var listing = $$"""{ "id": "SharedId", "authored": {{ValidAuthoredJson}} }""";
        var pack = $$"""{ "id": "sharedid", "versions": [ { "authored": {{PackAuthoredJson}} } ] }""";

        var result = SnapshotParser.Parse(Snapshot(listing, pack));

        Assert.Empty(result.ValidListings);
        Assert.Empty(result.ValidPacks);
        Assert.Equal("SharedId", Assert.Single(result.MalformedListings).Id);
        Assert.Equal("sharedid", Assert.Single(result.MalformedPacks).Id);
    }

    [Fact]
    public void Parse_PackVersionIdMismatchBesideValidVersion_KeepsValidSibling()
    {
        var invalidAuthored = PackAuthoredJson
            .Replace("\"id\": \"test-pack\"", "\"id\": \"other-pack\"")
            .Replace("\"version\": \"1.0.0\"", "\"version\": \"2.0.0\"");
        var pack = $$"""
            {
                "id": "test-pack",
                "versions": [
                    { "authored": {{invalidAuthored}} },
                    { "authored": {{PackAuthoredJson}} }
                ]
            }
            """;

        var result = SnapshotParser.Parse(Snapshot("", pack));

        var parsed = Assert.Single(result.ValidPacks);
        Assert.Single(parsed.ValidVersions);
        var rejected = Assert.Single(parsed.RejectedVersions);
        Assert.Equal("other-pack", rejected.Id);
        Assert.Equal("2.0.0", rejected.Version);
    }

    [Fact]
    public void Parse_DuplicatePackVersions_RejectsEveryConflictingVersion()
    {
        var otherAuthored = PackAuthoredJson.Replace("\"version\": \"1.0.0\"", "\"version\": \"2.0.0\"");
        var pack = $$"""
            {
                "id": "test-pack",
                "versions": [
                    { "authored": {{PackAuthoredJson}} },
                    { "authored": {{PackAuthoredJson}} },
                    { "authored": {{otherAuthored}} }
                ]
            }
            """;

        var result = SnapshotParser.Parse(Snapshot("", pack));

        var parsed = Assert.Single(result.ValidPacks);
        Assert.Equal("2.0.0", Assert.Single(parsed.ValidVersions).Metadata.Version.ToString());
        Assert.Equal(2, parsed.RejectedVersions.Count);
        Assert.All(parsed.RejectedVersions, rejected => Assert.Equal("1.0.0", rejected.Version));
    }

    [Fact]
    public void Parse_AmbiguousDependencyBesideValidRelease_KeepsValidSibling()
    {
        var invalidRelease = ValidReleaseJson
            .Replace("\"version\": \"1.0.0\"", "\"version\": \"2.0.0\"")
            .Replace("\"dependencies\": []", "\"dependencies\": [{ \"id\": \"mod-a\", \"kind\": \"required\", \"any_of\": [{ \"id\": \"mod-b\" }] }]");
        var listing = $$"""{ "id": "test-mod", "authored": {{ValidAuthoredJson}}, "releases": [{{invalidRelease}}, {{ValidReleaseJson}}] }""";

        var result = SnapshotParser.Parse(Snapshot(listing, ""));

        var parsed = Assert.Single(result.ValidListings);
        Assert.Single(parsed.ValidReleases);
        Assert.Contains("both id and any_of", Assert.Single(parsed.RejectedReleases).Reason);
    }

    [Fact]
    public void Parse_NullDependencyBesideValidRelease_KeepsValidSibling()
    {
        var invalidRelease = ValidReleaseJson
            .Replace("\"version\": \"1.0.0\"", "\"version\": \"2.0.0\"")
            .Replace("\"dependencies\": []", "\"dependencies\": [null]");
        var listing = $$"""{ "id": "test-mod", "authored": {{ValidAuthoredJson}}, "releases": [{{invalidRelease}}, {{ValidReleaseJson}}] }""";

        var result = SnapshotParser.Parse(Snapshot(listing, ""));

        var parsed = Assert.Single(result.ValidListings);
        Assert.Single(parsed.ValidReleases);
        Assert.Contains("null element", Assert.Single(parsed.RejectedReleases).Reason);
    }

    [Fact]
    public void Parse_UnknownConfigureMemberBesideValidListing_KeepsValidSibling()
    {
        var loaderAuthored = """
            {
                "spec_version": 1,
                "id": "test-loader",
                "type": "mod-loader",
                "name": "Test Loader",
                "authors": ["Test Author"],
                "abstract": "A loader used for testing.",
                "license": "MIT",
                "compatibility": { "game_min": "2026.7.4.2131" },
                "links": { "forums": "https://forums.example/thread/2" },
                "install": { "target": "standalone" },
                "provides": {
                    "launch": "loader.exe",
                    "configure": { "file": "config.json", "format": "json", "game-path": "GameLocation", "future-value": "x" }
                }
            }
            """;
        var invalid = $$"""{ "id": "test-loader", "authored": {{loaderAuthored}} }""";
        var valid = $$"""{ "id": "test-mod", "authored": {{ValidAuthoredJson}} }""";

        var result = SnapshotParser.Parse(Snapshot($"{invalid}, {valid}", ""));

        Assert.Equal("test-mod", Assert.Single(result.ValidListings).Id);
        Assert.Contains("future-value", Assert.Single(result.MalformedListings).Reason);
    }

    [Fact]
    public void Parse_MalformedFrozenFieldBesideValidRelease_KeepsValidSibling()
    {
        var invalidRelease = ValidReleaseJson
            .Replace("\"version\": \"1.0.0\"", "\"version\": \"2.0.0\"")
            .Replace("\"dependencies\": []", "\"dependencies\": [], \"listing\": { \"name\": \"Frozen name\", \"authors\": 42 }");
        var listing = $$"""{ "id": "test-mod", "authored": {{ValidAuthoredJson}}, "releases": [{{invalidRelease}}, {{ValidReleaseJson}}] }""";

        var result = SnapshotParser.Parse(Snapshot(listing, ""));

        var parsed = Assert.Single(result.ValidListings);
        Assert.Single(parsed.ValidReleases);
        var rejected = Assert.Single(parsed.RejectedReleases);
        Assert.Equal("2.0.0", rejected.Version);
        Assert.Contains("authors", rejected.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_AbsentFrozenFields_FallBackOneFieldAtATime()
    {
        var release = ValidReleaseJson.Replace("\"dependencies\": []", "\"dependencies\": [], \"listing\": { \"name\": \"Frozen name\" }");
        var listing = $$"""{ "id": "test-mod", "authored": {{ValidAuthoredJson}}, "releases": [{{release}}] }""";

        var result = SnapshotParser.Parse(Snapshot(listing, ""));

        var snapshot = Assert.Single(Assert.Single(result.ValidListings).ValidReleases).Listing!;
        Assert.Equal("Frozen name", snapshot.Name);
        Assert.Equal(new[] { "Test Author" }, snapshot.Authors);
        Assert.Equal("A mod used for testing.", snapshot.Abstract);
    }

    [Fact]
    public void Parse_FutureAuthoredShape_IsUnknownBeforeV1Deserialization()
    {
        var authored = ValidAuthoredJson
            .Replace("\"spec_version\": 1", "\"spec_version\": 2")
            .Replace("\"id\": \"test-mod\"", "\"id\": \"future-mod\"")
            .Replace("\"name\": \"Test Mod\"", "\"name\": { \"localized\": \"Future Mod\" }");
        var listing = $$"""{ "id": "future-mod", "authored": {{authored}} }""";

        var result = SnapshotParser.Parse(Snapshot(listing, ""));

        var unknown = Assert.Single(result.UnknownListings);
        Assert.Equal("future-mod", unknown.Id);
        Assert.Null(unknown.Version);
        Assert.Equal(2, unknown.SpecVersion);
    }

    [Fact]
    public void Parse_CaseInsensitiveLinkCollisionBesideValidListing_KeepsValidSibling()
    {
        var invalidAuthored = ValidAuthoredJson.Replace(
            "\"forums\": \"https://forums.example/thread/1\"",
            "\"forums\": \"https://forums.example/thread/1\", \"Forums\": \"https://other.example/thread\"");
        var invalid = $$"""{ "id": "bad-links", "authored": {{invalidAuthored.Replace("test-mod", "bad-links")}} }""";
        var valid = $$"""{ "id": "test-mod", "authored": {{ValidAuthoredJson}} }""";

        var result = SnapshotParser.Parse(Snapshot($"{invalid}, {valid}", ""));

        Assert.Single(result.ValidListings);
        Assert.Contains("collides", Assert.Single(result.MalformedListings).Reason);
    }

    [Fact]
    public void Parse_MalformedModerationDataBesideValidListing_KeepsValidSibling()
    {
        var invalid = $$"""{ "id": "bad-status", "authored": {{ValidAuthoredJson.Replace("test-mod", "bad-status")}}, "index_status": { "state": "disputed", "since": "not-a-date" } }""";
        var valid = $$"""{ "id": "test-mod", "authored": {{ValidAuthoredJson}} }""";

        var result = SnapshotParser.Parse(Snapshot($"{invalid}, {valid}", ""));

        Assert.Equal(2, result.ValidListings.Count);
        Assert.Empty(result.MalformedListings);
        var listing = result.ValidListings.Single(item => item.Id == "bad-status");
        Assert.Null(listing.IndexStatus);
        Assert.Contains("UTC timestamp", listing.IndexStatusError!.Reason);
    }

    [Fact]
    public void Parse_UnknownModerationState_KeepsRawState()
    {
        var listing = $$"""{ "id": "test-mod", "authored": {{ValidAuthoredJson}}, "index_status": { "state": "future-state" } }""";

        var result = SnapshotParser.Parse(Snapshot(listing, ""));

        var status = Assert.Single(result.ValidListings).IndexStatus!;
        Assert.Equal(IndexStatusState.Unknown, status.State);
        Assert.Equal("future-state", status.RawState);
    }

    [Fact]
    public void Parse_EmptyInstallSteps_StayDistinctFromAbsentSteps()
    {
        var emptyStepsAuthored = ValidAuthoredJson
            .Replace("\"id\": \"test-mod\"", "\"id\": \"empty-steps\"")
            .Replace("\"links\":", "\"install\": { \"steps\": [] }, \"links\":");
        var absentStepsAuthored = ValidAuthoredJson
            .Replace("\"id\": \"test-mod\"", "\"id\": \"absent-steps\"")
            .Replace("\"links\":", "\"install\": {}, \"links\":");
        var empty = $$"""{ "id": "empty-steps", "authored": {{emptyStepsAuthored}} }""";
        var absent = $$"""{ "id": "absent-steps", "authored": {{absentStepsAuthored}} }""";

        var result = SnapshotParser.Parse(Snapshot($"{empty}, {absent}", ""));

        var emptySteps = result.ValidListings.Single(listing => listing.Id == "empty-steps").Authored!.Install!.Steps;
        var absentSteps = result.ValidListings.Single(listing => listing.Id == "absent-steps").Authored!.Install!.Steps;
        Assert.Empty(emptySteps!);
        Assert.Null(absentSteps);
    }

    [Fact]
    public void Parse_CurrentSnapshotFixture_MapsEverySupportedEntry()
    {
        var fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Index", "Fixtures", "current-snapshot.json"));

        var result = SnapshotParser.Parse(fixture);

        Assert.Equal(4, result.ValidListings.Count);
        Assert.Equal(10, result.ValidListings.Sum(listing => listing.ValidReleases.Count));
        Assert.Empty(result.UnknownListings);
        Assert.Empty(result.MalformedListings);
        Assert.All(result.ValidListings, listing => Assert.Empty(listing.RejectedReleases));
    }
}
