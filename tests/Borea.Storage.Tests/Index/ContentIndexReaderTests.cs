using Borea.Core.Index;
using Borea.Storage.Index;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Index;

public sealed class ContentIndexReaderTests : IDisposable
{
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
            "id": "future-mod",
            "type": "mod",
            "version": "1.0.0",
            "version_scheme": "semver",
            "release_status": "stable",
            "release_date": "2026-09-01T12:00:00Z",
            "game_min": "2026.9.7.5402",
            "game_min_revision": 5402,
            "download": {
                "url": "https://example.test/mod.zip",
                "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                "size": 1024,
                "content_type": "application/zip"
            },
            "install_size": 2048,
            "dependencies": []
        }
        """;

    private const string ValidPackVersionJson = """
        {
            "spec_version": 1,
            "id": "test-pack",
            "type": "modpack",
            "name": "Test Pack",
            "authors": ["Test Author"],
            "abstract": "A pack used for testing.",
            "license": "CC0-1.0",
            "version": "1.0.0",
            "released_at": "2026-09-02T09:48:03Z",
            "links": { "forums": "https://forums.example/thread/2" },
            "compatibility": { "game_min": "2026.9.7.5402" },
            "mods": [{ "id": "test-mod", "version": "1.0.0" }],
            "vehicles": [{ "id": "test-vehicle", "version": "2.0.0" }],
            "saves": [{ "id": "test-save", "version": "3.0.0" }]
        }
        """;

    /// <summary>The share host example of RFC 0094 as the snapshot carries it.</summary>
    private const string StorageHost = """
        {
            "kind": "share",
            "name": "ksamods.gg",
            "storage": "ksamods-content/share",
            "bot": 98765432,
            "api": "https://ksamods.gg/api/share/v1/",
            "pages": "https://ksamods-content.github.io/share/",
            "types": ["vehicle", "save"],
            "max_bytes": { "vehicle": 10485760, "save": 104857600 },
            "new_listings_per_day": 5,
            "policy": "https://ksamods.gg/policy"
        }
        """;

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly ContentIndexReader _reader;

    public ContentIndexReaderTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _reader = new ContentIndexReader(_paths);
    }

    private string IndexPath => _paths.GetIndexPath();

    [Fact]
    public void Constructor_NullPathProviderOrEmptySource_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ContentIndexReader(null!));
        Assert.Throws<ArgumentException>(() => new ContentIndexReader(_paths, ""));
    }

    [Fact]
    public async Task ReadAsync_MissingFile_ThrowsNamingThePath()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _reader.ReadAsync());

        Assert.Contains(IndexPath, exception.Message);
    }

    [Fact]
    public async Task ReadAsync_EmptyFile_ThrowsSayingItIsEmpty()
    {
        await WriteIndexAsync(string.Empty);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _reader.ReadAsync());

        Assert.Contains("empty", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_ValidSnapshot_ReturnsMappedContent()
    {
        await WriteIndexAsync(Snapshot(Listing("test-mod", ValidAuthoredJson)));

        var result = await _reader.ReadAsync();

        Assert.Equal(1, result.SnapshotVersion);
        Assert.Equal("test-mod", Assert.Single(result.Listings).Id);
        Assert.Empty(result.Packs);
        Assert.Equal("master-server", result.GameVersions!.Source);
        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Tags.ModTags);
    }

    [Fact]
    public async Task ReadAsync_CuratedTags_ReturnsMappedVocabulary()
    {
        var tags = """
            "tags": {
                "spec_version": 1,
                "mod": [
                    { "tag": "parts", "name": "Parts", "meaning": "New parts.", "forum_prefix": "Parts" },
                    { "tag": "library", "name": "Library", "meaning": "Shared code." }
                ]
            },
            """;
        await WriteIndexAsync(Snapshot(Listing("test-mod", ValidAuthoredJson), prefix: tags));

        var result = await _reader.ReadAsync();

        Assert.Equal(1, result.Tags.SpecVersion);
        Assert.Equal(["parts", "library"], result.Tags.ModTags.Select(item => item.Tag));
        Assert.Equal("Parts", result.Tags.ModTags[0].ForumPrefix);
        Assert.Null(result.Tags.ModTags[0].ForumPrefixId);
        Assert.Null(result.Tags.ModTags[1].ForumPrefix);
    }

    [Fact]
    public async Task ReadAsync_CuratedTagsWithPrefixIds_ReadsTheIds()
    {
        var tags = """
            "tags": {
                "spec_version": 1,
                "mod": [
                    { "tag": "parts", "name": "Parts", "meaning": "New parts.", "forum_prefix": "Parts", "forum_prefix_id": 4 },
                    { "tag": "library", "name": "Library", "meaning": "Shared code." }
                ]
            },
            """;
        await WriteIndexAsync(Snapshot(Listing("test-mod", ValidAuthoredJson), prefix: tags));

        var result = await _reader.ReadAsync();

        Assert.Empty(result.Diagnostics);
        Assert.Equal([4, null], result.Tags.ModTags.Select(item => item.ForumPrefixId));
    }

    [Fact]
    public async Task ReadAsync_UnsupportedCuratedTagsVersion_KeepsListingAndAddsDiagnostic()
    {
        var tags = """
            "tags": { "spec_version": 2, "mod": [] },
            """;
        await WriteIndexAsync(Snapshot(Listing("test-mod", ValidAuthoredJson), prefix: tags));

        var result = await _reader.ReadAsync();

        Assert.Single(result.Listings);
        Assert.Empty(result.Tags.ModTags);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ContentIndexDiagnosticKind.UnsupportedVersion, diagnostic.Kind);
        Assert.Equal(ContentIndexDiagnosticScope.Tags, diagnostic.Scope);
        Assert.Equal(2, diagnostic.SpecVersion);
    }

    [Fact]
    public async Task ReadAsync_MalformedCuratedTags_KeepsListingAndAddsDiagnostic()
    {
        var tags = """
            "tags": [],
            """;
        await WriteIndexAsync(Snapshot(Listing("test-mod", ValidAuthoredJson), prefix: tags));

        var result = await _reader.ReadAsync();

        Assert.Single(result.Listings);
        Assert.Empty(result.Tags.ModTags);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ContentIndexDiagnosticKind.Malformed, diagnostic.Kind);
        Assert.Equal(ContentIndexDiagnosticScope.Tags, diagnostic.Scope);
    }

    [Fact]
    public async Task ReadAsync_Hosts_ReadsAStorageHostAndAHostWithoutStorage()
    {
        await WriteIndexAsync(Snapshot(Listing("test-mod", ValidAuthoredJson), prefix: $$"""
            "hosts": {
                "ksamods": {{StorageHost}},
                "ownhost": {
                    "kind": "share",
                    "api": "https://own.example/api",
                    "data": "https://data.own.example/share/",
                    "downloads": ["https://cdn.own.example/"],
                    "key": "MCowBQYDK2VwAyEAGb9ECWmEzf6FQbrBZ9w7lshQhqowtrbLDFw4rXAxZuE=",
                    "retired_keys": [],
                    "types": ["vehicle"]
                }
            },
            """));

        var result = await _reader.ReadAsync();

        Assert.Single(result.Listings);
        Assert.Equal(["ksamods", "ownhost"], result.ShareHosts.Select(host => host.Key));
        var storage = result.ShareHosts[0];
        Assert.Equal(("ksamods.gg", "https://ksamods.gg/api/share/v1/"), (storage.Name, storage.Api.AbsoluteUri));
        Assert.Equal(["vehicle", "save"], storage.Types);
        Assert.Equal(10485760, storage.MaxBytesFor("vehicle"));
        Assert.Equal("https://ksamods.gg/", storage.Page.AbsoluteUri);
        var own = result.ShareHosts[1];
        Assert.Equal(("ownhost", "https://own.example/api/"), (own.Name, own.Api.AbsoluteUri));
        Assert.Equal(["vehicle"], own.Types);
        Assert.Null(own.MaxBytesFor("vehicle"));
        Assert.Equal("https://own.example/", own.Page.AbsoluteUri);
    }

    [Theory]
    [InlineData("\"ksamods\":", "\"github\":")]
    [InlineData("\"ksamods\":", "\"KSAmods\":")]
    [InlineData("\"kind\": \"share\"", "\"kind\": \"mirror\"")]
    [InlineData("https://ksamods.gg/api/share/v1/", "http://ksamods.gg/api/share/v1/")]
    [InlineData("\"types\": [\"vehicle\", \"save\"]", "\"types\": \"vehicle\"")]
    [InlineData("\"vehicle\": 10485760", "\"vehicle\": 0")]
    [InlineData("\"vehicle\": 10485760", "\"vehicle\": 10485760.5")]
    [InlineData("\"ksamods\":", "\"\\ud800\":")]
    [InlineData("\"ksamods.gg\"", "\"\\ud800\"")]
    [InlineData("\"save\"]", "\"\\ud800\"]")]
    [InlineData("\"vehicle\": 10485760", "\"\\ud800\": 10485760")]
    [InlineData("\"https://ksamods.gg/policy\"", "\"\\ud800\"")]
    public async Task ReadAsync_HostTableBoreaCannotUse_IsSkippedAndTheOthersStay(string from, string to)
    {
        var hosts = $$"""
            "hosts": {
                "ksamods": {{StorageHost}},
                "other": {{StorageHost}}
            },
            """;
        // Only the first table changes, so the other one stays usable.
        var at = hosts.IndexOf(from, StringComparison.Ordinal);
        await WriteIndexAsync(Snapshot(Listing("test-mod", ValidAuthoredJson), prefix: hosts[..at] + to + hosts[(at + from.Length)..]));

        var result = await _reader.ReadAsync();

        Assert.Equal("other", Assert.Single(result.ShareHosts).Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"hosts\": {},")]
    [InlineData("\"hosts\": \"ksamods\",")]
    public async Task ReadAsync_NoHostTables_GivesNoHostsAndKeepsTheRest(string hosts)
    {
        await WriteIndexAsync(Snapshot(Listing("test-mod", ValidAuthoredJson), prefix: hosts));

        var result = await _reader.ReadAsync();

        Assert.Empty(result.ShareHosts);
        Assert.Single(result.Listings);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task ReadAsync_InvalidGameVersions_KeepsUsableListingAndAddsDiagnostic()
    {
        var gameVersions = """{ "spec_version": 1, "source": "master-server", "versions": ["invalid"] }""";
        await WriteIndexAsync(Snapshot(Listing("test-mod", ValidAuthoredJson), gameVersions));

        var result = await _reader.ReadAsync();

        Assert.Single(result.Listings);
        Assert.Null(result.GameVersions);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ContentIndexDiagnosticScope.GameVersions, diagnostic.Scope);
        Assert.Equal(ContentIndexDiagnosticKind.Malformed, diagnostic.Kind);
    }

    [Fact]
    public async Task ReadAsync_UnorderedGameVersions_KeepsUsableListingAndAddsDiagnostic()
    {
        var gameVersions = """{ "spec_version": 1, "source": "master-server", "versions": ["2026.9.7.5402", "2026.8.19.5261"] }""";
        await WriteIndexAsync(Snapshot(Listing("test-mod", ValidAuthoredJson), gameVersions));

        var result = await _reader.ReadAsync();

        Assert.Single(result.Listings);
        Assert.Null(result.GameVersions);
        Assert.Equal(ContentIndexDiagnosticScope.GameVersions, Assert.Single(result.Diagnostics).Scope);
    }

    [Theory]
    [InlineData("""{ "state": "disputed", "since": "not-a-date" }""", IndexStatusState.Disputed, "disputed", null, "timestamp")]
    [InlineData("""{ "state": "delisted", "since": 123, "reason": "The entry was removed." }""", IndexStatusState.Delisted, "delisted", "The entry was removed.", "must be a string")]
    public async Task ReadAsync_IndexStatusTimestampThatDoesNotParse_KeepsModerationStateAndAddsDiagnostic(
        string indexStatus, IndexStatusState state, string rawState, string? statusReason, string diagnosticReason)
    {
        await WriteIndexAsync(Snapshot(Listing("test-mod", ValidAuthoredJson, ", \"index_status\": " + indexStatus)));

        var result = await _reader.ReadAsync();

        var status = Assert.Single(result.Listings).IndexStatus!;
        Assert.Equal(state, status.State);
        Assert.Equal(rawState, status.RawState);
        Assert.Null(status.Since);
        Assert.Equal(statusReason, status.Reason);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ContentIndexDiagnosticKind.Malformed, diagnostic.Kind);
        Assert.Equal(ContentIndexDiagnosticScope.IndexStatus, diagnostic.Scope);
        Assert.Equal("test-mod", diagnostic.Id);
        Assert.Contains(diagnosticReason, diagnostic.Reason);
    }

    [Fact]
    public async Task ReadAsync_UnknownIndexStatus_KeepsRawStateAndAddsWarning()
    {
        var listing = Listing(
            "test-mod",
            ValidAuthoredJson,
            """, "index_status": { "state": "future-state" }""");
        await WriteIndexAsync(Snapshot(listing));

        var result = await _reader.ReadAsync();

        var status = Assert.Single(result.Listings).IndexStatus!;
        Assert.Equal(IndexStatusState.Unknown, status.State);
        Assert.Equal("future-state", status.RawState);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ContentIndexDiagnosticKind.UnsupportedValue, diagnostic.Kind);
        Assert.Contains("future-state", diagnostic.Reason);
    }

    [Fact]
    public async Task ReadAsync_UnsupportedAuthoredVersion_IsDiagnosticAndNotUsable()
    {
        var futureAuthored = ValidAuthoredJson
            .Replace("\"spec_version\": 1", "\"spec_version\": 2")
            .Replace("\"id\": \"test-mod\"", "\"id\": \"future-mod\"");
        var listing = Listing(
            "future-mod",
            futureAuthored,
            $$""", "releases": [{{ValidReleaseJson}}]""");
        await WriteIndexAsync(Snapshot(listing));

        var result = await _reader.ReadAsync();

        Assert.Empty(result.Listings);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ContentIndexDiagnosticKind.UnsupportedVersion, diagnostic.Kind);
        Assert.Equal("future-mod", diagnostic.Id);
        Assert.Equal(2, diagnostic.SpecVersion);
    }

    [Fact]
    public async Task ReadAsync_CurrentSnapshotFixture_ReturnsMappedCoreContent()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Index", "Fixtures", "current-snapshot.json");
        await WriteIndexAsync(await File.ReadAllTextAsync(fixturePath));

        var result = await _reader.ReadAsync();

        Assert.Equal(4, result.Listings.Count);
        Assert.Equal(10, result.Listings.Sum(listing => listing.Releases.Count));
        Assert.Empty(result.Packs);
        Assert.Equal(159, result.GameVersions!.Versions.Count);
        Assert.Empty(result.Diagnostics);
        var loader = result.Listings.Single(listing => listing.Id == "StarMap").Authored!;
        Assert.Equal(Borea.Core.Mods.ContentType.ModLoader, loader.Type);
        var release = result.Listings.Single(listing => listing.Id == "AdvancedFlightComputer").Releases[0];
        Assert.Equal(new DateTimeOffset(2026, 9, 2, 9, 48, 3, TimeSpan.Zero), release.ReleaseDate);
    }

    [Fact]
    public async Task ReadAsync_PackVersion_ReturnsEveryPackContentCategory()
    {
        var pack = $$"""
            {
                "id": "test-pack",
                "versions": [
                    {
                        "authored": {{ValidPackVersionJson}},
                        "index_status": {
                            "state": "retracted",
                            "since": "2026-09-03T10:00:00Z",
                            "reason": "The archive was replaced."
                        }
                    }
                ]
            }
            """;
        await WriteIndexAsync(SnapshotWithPacks(pack));

        var result = await _reader.ReadAsync();

        var version = Assert.Single(Assert.Single(result.Packs).Versions);
        Assert.Equal("test-pack", version.Metadata.ModPackId);
        Assert.Equal(new DateTimeOffset(2026, 9, 2, 9, 48, 3, TimeSpan.Zero), version.Metadata.ReleasedAt);
        Assert.Equal("test-mod", Assert.Single(version.Metadata.Mods).ContentId);
        Assert.Equal("test-vehicle", Assert.Single(version.Metadata.Vehicles).ContentId);
        Assert.Equal("test-save", Assert.Single(version.Metadata.Saves).ContentId);
        Assert.Equal(IndexStatusState.Retracted, version.IndexStatus!.State);
        Assert.Equal(new DateTimeOffset(2026, 9, 3, 10, 0, 0, TimeSpan.Zero), version.IndexStatus.Since);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task ReadAsync_PackVersionWithBuildMetadata_KeepsTheVersionAsTheDocumentWritesIt()
    {
        var authored = ValidPackVersionJson.Replace("\"version\": \"1.0.0\",", "\"version\": \"1.0.0+b.2\",", StringComparison.Ordinal);
        await WriteIndexAsync(SnapshotWithPacks($$"""{ "id": "test-pack", "versions": [{ "authored": {{authored}} }] }"""));

        var result = await _reader.ReadAsync();

        var version = Assert.Single(Assert.Single(result.Packs).Versions);
        Assert.Equal("1.0.0", version.Metadata.Version.ToString());
        Assert.Equal("1.0.0+b.2", version.VersionText);
    }

    [Fact]
    public async Task ReadAsync_EmptyInstanceTable_RejectsOnlyThatListing()
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
                "provides": { "launch": "loader.exe", "instance": {} }
            }
            """;
        await WriteIndexAsync(Snapshot($"{Listing("test-loader", loaderAuthored)}, {Listing("test-mod", ValidAuthoredJson)}"));

        var result = await _reader.ReadAsync();

        Assert.Equal("test-mod", Assert.Single(result.Listings).Id);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ContentIndexDiagnosticKind.Malformed, diagnostic.Kind);
        Assert.Equal(ContentIndexDiagnosticScope.Listing, diagnostic.Scope);
        Assert.Equal("test-loader", diagnostic.Id);
        Assert.Contains("flag or a variable", diagnostic.Reason);
    }

    [Fact]
    public async Task ReadAsync_CanceledToken_StopsTheRead()
    {
        await WriteIndexAsync(Snapshot(Listing("test-mod", ValidAuthoredJson)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _reader.ReadAsync(cancellation.Token));
    }

    [Fact]
    public async Task ValidateAsync_ReadsTheCandidateBeforeItBecomesTheCache()
    {
        var candidatePath = Path.Combine(_tempRoot, "index.json.tmp");
        Directory.CreateDirectory(_tempRoot);
        await File.WriteAllTextAsync(candidatePath, Snapshot(Listing("test-mod", ValidAuthoredJson)));

        await _reader.ValidateAsync(candidatePath);

        Assert.False(File.Exists(IndexPath));
    }

    private static string Listing(string id, string authored, string suffix = "") =>
        $$"""{ "id": "{{id}}", "authored": {{authored}}{{suffix}} }""";

    private static string Snapshot(
        string listings,
        string gameVersions = """{ "spec_version": 1, "source": "master-server", "versions": ["2026.9.7.5402"] }""",
        string prefix = "") =>
        $$"""
        {
            "snapshot_version": 1,
            {{prefix}}
            "listings": [{{listings}}],
            "packs": [],
            "game_versions": {{gameVersions}}
        }
        """;

    private static string SnapshotWithPacks(string packs) => $$"""
        {
            "snapshot_version": 1,
            "listings": [],
            "packs": [{{packs}}],
            "game_versions": { "spec_version": 1, "source": "master-server", "versions": ["2026.9.7.5402"] }
        }
        """;

    private async Task WriteIndexAsync(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(IndexPath)!);
        await File.WriteAllTextAsync(IndexPath, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
    private async Task WriteTypeFileAsync(ContentIndexTypeFile file, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(IndexPath)!);
        await File.WriteAllTextAsync(file.CachePathFrom(IndexPath), content);
    }

    [Fact]
    public async Task ReadAsync_NoTypeFiles_GivesTheMainDocumentWithNoVehiclesOrSaves()
    {
        await WriteIndexAsync(Snapshot(Listing("test-mod", ValidAuthoredJson)));

        var result = await _reader.ReadAsync();

        Assert.Equal("test-mod", Assert.Single(result.Listings).Id);
        Assert.Empty(result.Vehicles);
        Assert.Empty(result.Saves);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task ReadAsync_TypeFilesNextToTheIndex_FillVehiclesAndSaves()
    {
        await WriteIndexAsync(Snapshot(Listing("test-mod", ValidAuthoredJson)));
        await WriteTypeFileAsync(ContentIndexTypeFiles.Vehicles, TypeFileJson.File("vehicles", TypeFileJson.Entry()));
        await WriteTypeFileAsync(ContentIndexTypeFiles.Saves, TypeFileJson.File("saves", TypeFileJson.Entry("ExampleSave", "save", TypeFileJson.Release("ExampleSave", "save"))));

        var result = await _reader.ReadAsync();

        Assert.Equal("test-mod", Assert.Single(result.Listings).Id);
        Assert.Equal("Example Rover", Assert.Single(Assert.Single(result.Vehicles).Releases).Install!.Folder);
        Assert.Equal("ExampleSave", Assert.Single(result.Saves).Id);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("""{ "snapshot_version": 1, "vehicles": {} }""")]
    [InlineData("not json")]
    [InlineData("""{ "snapshot_version": 2, "vehicles": [] }""")]
    public async Task ReadAsync_UnusableVehicleFile_KeepsListingsAndPacksAndAddsOneDiagnostic(string vehicles)
    {
        await WriteIndexAsync(Snapshot(Listing("test-mod", ValidAuthoredJson)));
        await WriteTypeFileAsync(ContentIndexTypeFiles.Vehicles, vehicles);
        await WriteTypeFileAsync(ContentIndexTypeFiles.Saves, TypeFileJson.File("saves", TypeFileJson.Entry("ExampleSave", "save", TypeFileJson.Release("ExampleSave", "save"))));

        var result = await _reader.ReadAsync();

        Assert.Equal("test-mod", Assert.Single(result.Listings).Id);
        Assert.Empty(result.Vehicles);
        Assert.Equal("ExampleSave", Assert.Single(result.Saves).Id);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ContentIndexDiagnosticScope.Listing, diagnostic.Scope);
        Assert.Contains("snapshot-vehicles.json", diagnostic.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_RejectedVehicleRelease_IsAReleaseDiagnostic()
    {
        await WriteIndexAsync(Snapshot(""));
        var releases = TypeFileJson.Release(install: """{ "derived": true, "folder": "CON" }""", version: "2.0.0") + "," + TypeFileJson.Release();
        await WriteTypeFileAsync(ContentIndexTypeFiles.Vehicles, TypeFileJson.File("vehicles", TypeFileJson.Entry(releases: releases)));

        var result = await _reader.ReadAsync();

        Assert.Single(Assert.Single(result.Vehicles).Releases);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ContentIndexDiagnosticScope.Release, diagnostic.Scope);
        Assert.Equal("2.0.0", diagnostic.Version);
    }

    [Fact]
    public async Task ValidateAsync_ReadsOnlyTheCandidate()
    {
        var candidatePath = Path.Combine(_tempRoot, "index.json.tmp");
        Directory.CreateDirectory(_tempRoot);
        await File.WriteAllTextAsync(candidatePath, Snapshot(Listing("test-mod", ValidAuthoredJson)));
        await WriteTypeFileAsync(ContentIndexTypeFiles.Vehicles, "not json");

        await _reader.ValidateAsync(candidatePath);
    }
}
