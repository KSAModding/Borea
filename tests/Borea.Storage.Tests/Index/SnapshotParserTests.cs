using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Storage.Index;
using System.Runtime.ExceptionServices;
using System.Text.Json;

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
    public void Parse_CancellationDuringListingTraversal_Stops()
    {
        // The invalid releases value makes the parser catch a JsonException for each listing, and the handler cancels on the first one.
        const int listingCount = 100;
        var json = Snapshot(string.Join(",", Enumerable.Range(0, listingCount).Select(index => $$"""{ "id": "mod-{{index}}", "releases": 42 }""")), "");
        using var cancellation = new CancellationTokenSource();
        var parseThreadId = Environment.CurrentManagedThreadId;
        var jsonExceptions = 0;

        void CancelOnJsonException(object? sender, FirstChanceExceptionEventArgs args)
        {
            if (Environment.CurrentManagedThreadId != parseThreadId || args.Exception is not JsonException)
                return;

            jsonExceptions++;
            cancellation.Cancel();
        }

        AppDomain.CurrentDomain.FirstChanceException += CancelOnJsonException;
        try
        {
            Assert.Throws<OperationCanceledException>(() => SnapshotParser.Parse(json, cancellationToken: cancellation.Token));
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= CancelOnJsonException;
        }

        Assert.InRange(jsonExceptions, 1, listingCount - 1);
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
    public void Parse_ReleaseWithUnavailableSince_ReadsWhenItsDownloadWentFromItsHost()
    {
        var gone = ValidReleaseJson.Replace("\"content_type\": \"application/zip\" }", "\"content_type\": \"application/zip\", \"unavailable_since\": \"2026-09-23T10:24:00Z\" }");
        var listing = $$"""{ "id": "test-mod", "authored": {{ValidAuthoredJson}}, "releases": [{{gone}}] }""";

        var result = SnapshotParser.Parse(Snapshot(listing, ""));

        var release = Assert.Single(Assert.Single(result.ValidListings).ValidReleases);
        Assert.Equal(new DateTimeOffset(2026, 9, 23, 10, 24, 0, TimeSpan.Zero), release.Download.UnavailableSince);
        Assert.False(release.IsOffered);
    }

    [Fact]
    public void Parse_AuthoredListingWithShortBounds_LoadsWithTheBoundsFilled()
    {
        var authored = ValidAuthoredJson.Replace(
            "\"links\":",
            "\"loader\": { \"id\": \"test-loader\", \"min\": \"0.4\" }, \"dependencies\": [{ \"id\": \"other-mod\", \"kind\": \"required\", \"min\": \"1\" }], \"links\":");
        var listing = $$"""{ "id": "test-mod", "authored": {{authored}}, "releases": [] }""";

        var result = SnapshotParser.Parse(Snapshot(listing, ""));

        var parsed = Assert.Single(result.ValidListings);
        Assert.Equal("0.4.0", parsed.Authored!.Loader!.MinVersion.ToString());
        Assert.Equal("1.0.0", Assert.Single(parsed.Authored.Dependencies).MinVersion.ToString());
    }

    [Fact]
    public void Parse_AuthoredListingWithGitHubAndSince_HasOneHostAndReadsSinceAsAVersion()
    {
        var authored = ValidAuthoredJson.Replace("\"links\":", "\"releases\": { \"github\": \"owner/repo\", \"since\": \"1.2\" }, \"links\":");
        var listing = $$"""{ "id": "test-mod", "authored": {{authored}}, "releases": [] }""";

        var result = SnapshotParser.Parse(Snapshot(listing, ""));

        var releases = Assert.Single(result.ValidListings).Authored!.Releases!;
        Assert.Equal("github", Assert.Single(releases.Hosts).Host);
        Assert.Equal("1.2.0", releases.Since.ToString());
    }

    [Fact]
    public void Parse_AuthoredListingWithGitHubAndTagPrefix_HasOneHostAndReadsThePrefix()
    {
        var authored = ValidAuthoredJson.Replace("\"links\":", "\"releases\": { \"github\": \"owner/repo\", \"tag_prefix\": \"release/v\" }, \"links\":");
        var listing = $$"""{ "id": "test-mod", "authored": {{authored}}, "releases": [] }""";

        var result = SnapshotParser.Parse(Snapshot(listing, ""));

        var releases = Assert.Single(result.ValidListings).Authored!.Releases!;
        Assert.Equal("owner/repo", Assert.Single(releases.Hosts).Reference);
        Assert.Equal("release/v", releases.TagPrefix);
    }

    [Fact]
    public void Parse_AuthoredListingWithAnUnknownKeyUnderReleases_KeepsItsHosts()
    {
        var authored = ValidAuthoredJson.Replace("\"links\":", "\"releases\": { \"github\": \"owner/repo\", \"until\": \"2.0\", \"notify\": true }, \"links\":");
        var listing = $$"""{ "id": "test-mod", "authored": {{authored}}, "releases": [] }""";

        var result = SnapshotParser.Parse(Snapshot(listing, ""));

        var releases = Assert.Single(result.ValidListings).Authored!.Releases!;
        Assert.Equal("owner/repo", Assert.Single(releases.Hosts).Reference);
        Assert.Null(releases.Since);
    }

    [Fact]
    public void Parse_AuthoredReleasesValuesThatAreNotUnicodeText_AreNotRead()
    {
        var authored = ValidAuthoredJson.Replace("\"links\":", "\"releases\": { \"github\": \"owner/repo\", \"spacedock\": \"\\ud800/x\", \"since\": \"\\ud800\", \"tag_prefix\": \"\\ud800\" }, \"links\":");
        var listing = $$"""{ "id": "test-mod", "authored": {{authored}}, "releases": [] }""";

        var result = SnapshotParser.Parse(Snapshot(listing, ""));

        var releases = Assert.Single(result.ValidListings).Authored!.Releases!;
        Assert.Equal("owner/repo", Assert.Single(releases.Hosts).Reference);
        Assert.Null(releases.Since);
        Assert.Null(releases.TagPrefix);
    }

    [Fact]
    public void Parse_OtherLinkThatIsNotUnicodeTextBesideValidListing_KeepsValidSibling()
    {
        var invalidAuthored = ValidAuthoredJson.Replace(
            "\"forums\": \"https://forums.example/thread/1\"",
            "\"forums\": \"https://forums.example/thread/1\", \"wiki\": \"\\ud800\"");
        var invalid = $$"""{ "id": "bad-links", "authored": {{invalidAuthored.Replace("test-mod", "bad-links")}} }""";
        var valid = $$"""{ "id": "test-mod", "authored": {{ValidAuthoredJson}} }""";

        var result = SnapshotParser.Parse(Snapshot($"{invalid}, {valid}", ""));

        Assert.Single(result.ValidListings);
        Assert.Contains("not valid Unicode text", Assert.Single(result.MalformedListings).Reason);
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

    [Theory]
    [InlineData("\"dependencies\": [{ \"id\": \"mod-a\", \"kind\": \"required\", \"any_of\": [{ \"id\": \"mod-b\" }] }]", "both id and any_of")]
    [InlineData("\"dependencies\": [null]", "null element")]
    [InlineData("\"dependencies\": [], \"listing\": { \"name\": \"Frozen name\", \"authors\": 42 }", "authors")]
    public void Parse_ReleaseThatDoesNotMapBesideValidRelease_KeepsValidSibling(string dependencies, string reason)
    {
        var invalidRelease = ValidReleaseJson
            .Replace("\"version\": \"1.0.0\"", "\"version\": \"2.0.0\"")
            .Replace("\"dependencies\": []", dependencies);
        var listing = $$"""{ "id": "test-mod", "authored": {{ValidAuthoredJson}}, "releases": [{{invalidRelease}}, {{ValidReleaseJson}}] }""";

        var result = SnapshotParser.Parse(Snapshot(listing, ""));

        var parsed = Assert.Single(result.ValidListings);
        Assert.Single(parsed.ValidReleases);
        var rejected = Assert.Single(parsed.RejectedReleases);
        Assert.Equal("2.0.0", rejected.Version);
        Assert.Contains(reason, rejected.Reason);
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
    public void Parse_InstanceTable_IsReadIntoTheLoaderProvides()
    {
        var loader = $$"""{ "id": "test-loader", "authored": {{LoaderAuthoredJson("""{ "flag": "-InstancePath", "variable": "STARMAP_INSTANCE_PATH" }""")}} }""";

        var result = SnapshotParser.Parse(Snapshot(loader, ""));

        var instance = Assert.Single(result.ValidListings).Authored!.Provides!.Instance;
        Assert.Equal("-InstancePath", instance!.Flag);
        Assert.Equal("STARMAP_INSTANCE_PATH", instance.Variable);
    }

    [Fact]
    public void Parse_NullKeyBesideANamedKey_IsReadAsAbsent()
    {
        // TOML has no null, so a published snapshot cannot carry one, and the
        // reader treats a null key like a key that is not there.
        var loader = $$"""{ "id": "test-loader", "authored": {{LoaderAuthoredJson("""{ "flag": null, "variable": "LOADER_INSTANCE" }""")}} }""";

        var result = SnapshotParser.Parse(Snapshot(loader, ""));

        var instance = Assert.Single(result.ValidListings).Authored!.Provides!.Instance;
        Assert.Null(instance!.Flag);
        Assert.Equal("LOADER_INSTANCE", instance.Variable);
        Assert.Empty(result.MalformedListings);
    }

    [Fact]
    public void Parse_NullInstanceTable_IsReadAsAbsent()
    {
        var loader = $$"""{ "id": "test-loader", "authored": {{LoaderAuthoredJson("null")}} }""";

        var result = SnapshotParser.Parse(Snapshot(loader, ""));

        Assert.Null(Assert.Single(result.ValidListings).Authored!.Provides!.Instance);
        Assert.Empty(result.MalformedListings);
    }

    [Theory]
    [InlineData("{}", "flag or a variable")]
    [InlineData("""{ "flag": null, "variable": null }""", "flag or a variable")]
    [InlineData("""{ "flag": "" }""", "without whitespace")]
    [InlineData("""{ "flag": "-Instance Path" }""", "without whitespace")]
    [InlineData("""{ "variable": "LOADER INSTANCE" }""", "without whitespace")]
    [InlineData("""{ "flag": "-InstancePath", "future-key": "x" }""", "future-key")]
    [InlineData("""{ "flag": 42 }""", "flag")]
    public void Parse_InvalidInstanceTableBesideValidListing_KeepsValidSibling(string instance, string reason)
    {
        var invalid = $$"""{ "id": "test-loader", "authored": {{LoaderAuthoredJson(instance)}} }""";
        var valid = $$"""{ "id": "test-mod", "authored": {{ValidAuthoredJson}} }""";

        var result = SnapshotParser.Parse(Snapshot($"{invalid}, {valid}", ""));

        Assert.Equal("test-mod", Assert.Single(result.ValidListings).Id);
        var rejected = Assert.Single(result.MalformedListings);
        Assert.Equal("test-loader", rejected.Id);
        Assert.Contains(reason, rejected.Reason);
    }

    private static string LoaderAuthoredJson(string instance, string platform = "null") => $$"""
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
            "provides": { "launch": "loader.exe", "instance": {{instance}}, "platform": {{platform}} }
        }
        """;

    private static string PlatformLoader(string platform) =>
        $$"""{ "id": "test-loader", "authored": {{LoaderAuthoredJson("""{ "flag": "-InstancePath" }""", platform)}} }""";

    [Fact]
    public void Parse_PlatformTable_IsReadIntoTheLoaderProvides()
    {
        var loader = PlatformLoader("""{ "linux": { "runtime": "dotnet", "launch": "bin/loader.dll" }, "macos": { "launch": "loader-mac" } }""");

        var result = SnapshotParser.Parse(Snapshot(loader, ""));

        var platforms = Assert.Single(result.ValidListings).Authored!.Provides!.Platforms;
        Assert.Equal(2, platforms.Count);
        Assert.Equal("bin/loader.dll", platforms[OsPlatform.Linux].Launch);
        Assert.Equal(LoaderRuntime.Dotnet, platforms[OsPlatform.Linux].Runtime);
        Assert.Equal("loader-mac", platforms[OsPlatform.MacOs].Launch);
        Assert.Null(platforms[OsPlatform.MacOs].Runtime);
        Assert.Empty(platforms[OsPlatform.MacOs].UnknownKeys);
    }

    [Fact]
    public void Parse_UnknownNamesInThePlatformTable_KeepTheListing()
    {
        var loader = PlatformLoader("""{ "linux": { "launch": "loader.dll", "runtime": "mono", "arch": "arm64" }, "freebsd": 42, "Windows": { "launch": 1 } }""");

        var result = SnapshotParser.Parse(Snapshot(loader, ""));

        Assert.Empty(result.MalformedListings);
        var entry = Assert.Single(Assert.Single(result.ValidListings).Authored!.Provides!.Platforms);
        Assert.Equal(OsPlatform.Linux, entry.Key);
        Assert.Equal(LoaderRuntime.Unknown, entry.Value.Runtime);
        Assert.Equal("mono", entry.Value.RuntimeName);
        Assert.Equal("arch", Assert.Single(entry.Value.UnknownKeys));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""{ "linux": null }""")]
    public void Parse_NullPlatformTableOrEntry_IsReadAsAbsent(string platform)
    {
        var result = SnapshotParser.Parse(Snapshot(PlatformLoader(platform), ""));

        Assert.Empty(result.MalformedListings);
        Assert.Empty(Assert.Single(result.ValidListings).Authored!.Provides!.Platforms);
    }

    [Theory]
    [InlineData("\"linux\"", "platform")]
    [InlineData("""{ "linux": "loader.dll" }""", "linux")]
    [InlineData("""{ "linux": {} }""", "linux")]
    [InlineData("""{ "linux": { "runtime": "dotnet" } }""", "linux")]
    [InlineData("""{ "linux": { "launch": 42 } }""", "linux")]
    [InlineData("""{ "linux": { "launch": "loader.dll", "runtime": 1 } }""", "linux")]
    [InlineData("""{ "linux": { "launch": "../loader.dll" } }""", "anchor")]
    public void Parse_InvalidPlatformTableBesideValidListing_KeepsValidSibling(string platform, string reason)
    {
        var valid = $$"""{ "id": "test-mod", "authored": {{ValidAuthoredJson}} }""";

        var result = SnapshotParser.Parse(Snapshot($"{PlatformLoader(platform)}, {valid}", ""));

        Assert.Equal("test-mod", Assert.Single(result.ValidListings).Id);
        var rejected = Assert.Single(result.MalformedListings);
        Assert.Equal("test-loader", rejected.Id);
        Assert.Contains(reason, rejected.Reason);
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
        Assert.Equal(IndexStatusState.Disputed, listing.IndexStatus!.State);
        Assert.Equal("disputed", listing.IndexStatus.RawState);
        Assert.Null(listing.IndexStatus.Since);
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
    public void Parse_NonStringModerationTimestamp_KeepsStateAndReportsTimestamp()
    {
        var listing = $$"""{ "id": "test-mod", "authored": {{ValidAuthoredJson}}, "index_status": { "state": "delisted", "since": 123 } }""";

        var result = SnapshotParser.Parse(Snapshot(listing, ""));

        var parsed = Assert.Single(result.ValidListings);
        Assert.Equal(IndexStatusState.Delisted, parsed.IndexStatus!.State);
        Assert.Null(parsed.IndexStatus.Since);
        Assert.Contains("must be a string", parsed.IndexStatusError!.Reason);
    }

    [Fact]
    public void Parse_ModerationReasonThatIsNotUnicodeText_DropsOnlyTheStatusAndKeepsTheSibling()
    {
        var invalid = $$"""{ "id": "bad-status", "authored": {{ValidAuthoredJson.Replace("test-mod", "bad-status")}}, "index_status": { "state": "disputed", "reason": "\ud800" } }""";
        var valid = $$"""{ "id": "test-mod", "authored": {{ValidAuthoredJson}} }""";

        var result = SnapshotParser.Parse(Snapshot($"{invalid}, {valid}", ""));

        Assert.Equal(2, result.ValidListings.Count);
        var listing = result.ValidListings.Single(item => item.Id == "bad-status");
        Assert.Null(listing.IndexStatus);
        Assert.Contains("not valid Unicode text", listing.IndexStatusError!.Reason);
    }

    [Fact]
    public void Parse_ModerationTimestampThatIsNotUnicodeText_KeepsStateAndReportsTimestamp()
    {
        var listing = $$"""{ "id": "test-mod", "authored": {{ValidAuthoredJson}}, "index_status": { "state": "delisted", "since": "\ud800" } }""";

        var result = SnapshotParser.Parse(Snapshot(listing, ""));

        var parsed = Assert.Single(result.ValidListings);
        Assert.Equal(IndexStatusState.Delisted, parsed.IndexStatus!.State);
        Assert.Null(parsed.IndexStatus.Since);
        Assert.Contains("not valid Unicode text", parsed.IndexStatusError!.Reason);
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
    private static IndexValidationResult ParseWithVehicles(string listings, string vehicles, string packs = "") =>
        SnapshotParser.Parse(
            Snapshot(listings, packs),
            [(ContentIndexTypeFiles.Vehicles, TypeFileJson.File("vehicles", vehicles))],
            "index");

    [Fact]
    public void Parse_NoTypeFiles_GivesTheSameResultAsTheMainDocumentAlone()
    {
        var json = Snapshot($$"""{ "id": "test-mod", "authored": {{ValidAuthoredJson}}, "releases": [{{ValidReleaseJson}}] }""", "");

        var withOverload = SnapshotParser.Parse(json, [], "index");
        var alone = SnapshotParser.Parse(json);

        Assert.Equal(alone.ValidListings.Select(listing => listing.Id), withOverload.ValidListings.Select(listing => listing.Id));
        Assert.Equal(alone.MalformedListings, withOverload.MalformedListings);
        Assert.Empty(withOverload.ValidVehicles);
        Assert.Empty(withOverload.ValidSaves);
    }

    [Fact]
    public void Parse_VehicleWithoutCompatibilityAndForums_IsReadWithItsReleaseFolderAndCounts()
    {
        var downloads = """, "downloads": { "total": 7, "hosts": { "github": 7 }, "releases": [{ "version": "1.0.0", "total": 7, "hosts": { "github": 7 } }] }""";

        var result = ParseWithVehicles("", TypeFileJson.Entry(extra: downloads));

        var vehicle = Assert.Single(result.ValidVehicles);
        Assert.Empty(result.ValidListings);
        Assert.Empty(result.MalformedListings);
        Assert.Equal(ContentType.Vehicle, vehicle.Authored!.Type);
        Assert.Null(vehicle.Authored.GameMin);
        Assert.Null(vehicle.Authored.ForumUrl);
        var release = Assert.Single(vehicle.ValidReleases);
        Assert.Empty(vehicle.RejectedReleases);
        Assert.Equal("Example Rover", release.Install!.Folder);
        Assert.Null(release.Install.Root);
        Assert.Equal(7, vehicle.Downloads!.Total);
    }

    [Fact]
    public void Parse_SaveFile_FillsTheSaves()
    {
        var result = SnapshotParser.Parse(
            Snapshot("", ""),
            [(ContentIndexTypeFiles.Saves, TypeFileJson.File("saves", TypeFileJson.Entry("ExampleSave", "save", TypeFileJson.Release("ExampleSave", "save"))))],
            "index");

        Assert.Equal(ContentType.Save, Assert.Single(result.ValidSaves).Authored!.Type);
        Assert.Empty(result.ValidVehicles);
    }

    [Theory]
    [InlineData("""{ "derived": true }""")]
    [InlineData("""{ "derived": true, "folder": "CON" }""")]
    [InlineData("""{ "derived": true, "folder": "Example  Rover" }""")]
    [InlineData("""{ "derived": true, "folder": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" }""")]
    [InlineData("""{ "derived": true, "folder": "Example Rover", "target": "user-data" }""")]
    [InlineData("""{ "derived": true, "folder": "Example Rover", "path": "Vehicles" }""")]
    public void Parse_VehicleReleaseWithoutAFolderTheGameKeepsOrWithATarget_IsRejectedAndTheListingStays(string install)
    {
        var releases = TypeFileJson.Release(install: install, version: "2.0.0") + "," + TypeFileJson.Release();

        var result = ParseWithVehicles("", TypeFileJson.Entry(releases: releases));

        var vehicle = Assert.Single(result.ValidVehicles);
        Assert.Equal("1.0.0", Assert.Single(vehicle.ValidReleases).Version.ToString());
        Assert.Equal("2.0.0", Assert.Single(vehicle.RejectedReleases).Version);
    }

    [Fact]
    public void Parse_VehicleInListings_IsRejectedAloneAndNamesItsFile()
    {
        var listings = TypeFileJson.Entry("WrongPlace") + "," + $$"""{ "id": "test-mod", "authored": {{ValidAuthoredJson}}, "releases": [{{ValidReleaseJson}}] }""";

        var result = ParseWithVehicles(listings, "");

        Assert.Equal("test-mod", Assert.Single(result.ValidListings).Id);
        var rejected = Assert.Single(result.MalformedListings);
        Assert.Equal("WrongPlace", rejected.Id);
        Assert.Contains("snapshot-vehicles.json", rejected.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("mod")]
    [InlineData("save")]
    [InlineData("something-else")]
    public void Parse_OtherTypeAmongTheVehicles_IsRejectedAlone(string type)
    {
        var vehicles = TypeFileJson.Entry("Stranger", type, TypeFileJson.Release("Stranger", type)) + "," + TypeFileJson.Entry();

        var result = ParseWithVehicles("", vehicles);

        Assert.Equal("ExampleRover", Assert.Single(result.ValidVehicles).Id);
        Assert.Equal("Stranger", Assert.Single(result.MalformedListings).Id);
    }

    [Fact]
    public void Parse_VehicleEntryWithReleasesButNoAuthored_IsRejected_AndATombstoneIsRead()
    {
        var vehicles = $$"""{ "id": "NoAuthored", "releases": [{{TypeFileJson.Release("NoAuthored")}}] }"""
            + "," + """{ "id": "Gone", "index_status": { "state": "delisted" } }""";

        var result = ParseWithVehicles("", vehicles);

        Assert.Equal("NoAuthored", Assert.Single(result.MalformedListings).Id);
        Assert.Equal("Gone", Assert.Single(result.ValidVehicles).Id);
    }

    [Fact]
    public void Parse_IdAmongTheModsAndAmongTheVehicles_IsRejectedInBothPlaces()
    {
        var mod = $$"""{ "id": "test-mod", "authored": {{ValidAuthoredJson}}, "releases": [{{ValidReleaseJson}}] }""";
        var vehicle = TypeFileJson.Entry("TEST-MOD", releases: TypeFileJson.Release("TEST-MOD")) + "," + TypeFileJson.Entry();

        var result = ParseWithVehicles(mod, vehicle);

        Assert.Empty(result.ValidListings);
        Assert.Equal("ExampleRover", Assert.Single(result.ValidVehicles).Id);
        Assert.Equal(2, result.MalformedListings.Count(entry => ModIds.Equals(entry.Id, "test-mod") && entry.Reason.Contains("more than once", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("""{ "snapshot_version": 1, "vehicles": {} }""")]
    [InlineData("""{ "snapshot_version": 1, "vehicles": [""")]
    [InlineData("""{ "snapshot_version": 2, "vehicles": [] }""")]
    [InlineData("""{ "snapshot_version": 1, "sources": [], "vehicles": [] }""")]
    public void Parse_UnusableVehicleFile_KeepsListingsAndPacksAndAddsOneDiagnostic(string vehicles)
    {
        var mod = $$"""{ "id": "test-mod", "authored": {{ValidAuthoredJson}}, "releases": [{{ValidReleaseJson}}] }""";
        var pack = $$"""{ "id": "test-pack", "versions": [{ "authored": {{PackAuthoredJson}} }] }""";

        var result = SnapshotParser.Parse(Snapshot(mod, pack), [(ContentIndexTypeFiles.Vehicles, vehicles)], "index");

        Assert.Equal("test-mod", Assert.Single(result.ValidListings).Id);
        Assert.Equal("test-pack", Assert.Single(result.ValidPacks).Id);
        Assert.Empty(result.ValidVehicles);
        var rejected = Assert.Single(result.MalformedListings);
        Assert.Null(rejected.Id);
        Assert.Contains("snapshot-vehicles.json", rejected.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ModListingWithoutForums_IsStillRejected()
    {
        var authored = ValidAuthoredJson.Replace("""{ "forums": "https://forums.example/thread/1" }""", "{}", StringComparison.Ordinal);
        Assert.NotEqual(ValidAuthoredJson, authored);

        var result = SnapshotParser.Parse(Snapshot($$"""{ "id": "test-mod", "authored": {{authored}} }""", ""));

        Assert.Empty(result.ValidListings);
        Assert.Contains("forums", Assert.Single(result.MalformedListings).Reason, StringComparison.Ordinal);
    }
}
