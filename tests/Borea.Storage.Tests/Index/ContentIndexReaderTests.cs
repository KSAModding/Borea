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
    public void Constructor_NullPathProvider_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ContentIndexReader(null!));
    }

    [Fact]
    public void Constructor_EmptySource_ThrowsArgumentException()
    {
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

    [Fact]
    public async Task ReadAsync_MalformedIndexStatusTimestamp_KeepsModerationStateAndAddsDiagnostic()
    {
        var listing = Listing(
            "test-mod",
            ValidAuthoredJson,
            """, "index_status": { "state": "disputed", "since": "not-a-date" }""");
        await WriteIndexAsync(Snapshot(listing));

        var result = await _reader.ReadAsync();

        var status = Assert.Single(result.Listings).IndexStatus!;
        Assert.Equal(IndexStatusState.Disputed, status.State);
        Assert.Equal("disputed", status.RawState);
        Assert.Null(status.Since);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ContentIndexDiagnosticKind.Malformed, diagnostic.Kind);
        Assert.Equal(ContentIndexDiagnosticScope.IndexStatus, diagnostic.Scope);
        Assert.Equal("test-mod", diagnostic.Id);
        Assert.Contains("timestamp", diagnostic.Reason);
    }

    [Fact]
    public async Task ReadAsync_NonStringIndexStatusTimestamp_KeepsModerationStateAndAddsDiagnostic()
    {
        var listing = Listing(
            "test-mod",
            ValidAuthoredJson,
            """, "index_status": { "state": "delisted", "since": 123, "reason": "The entry was removed." }""");
        await WriteIndexAsync(Snapshot(listing));

        var result = await _reader.ReadAsync();

        var status = Assert.Single(result.Listings).IndexStatus!;
        Assert.Equal(IndexStatusState.Delisted, status.State);
        Assert.Equal("delisted", status.RawState);
        Assert.Null(status.Since);
        Assert.Equal("The entry was removed.", status.Reason);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ContentIndexDiagnosticKind.Malformed, diagnostic.Kind);
        Assert.Equal(ContentIndexDiagnosticScope.IndexStatus, diagnostic.Scope);
        Assert.Equal("test-mod", diagnostic.Id);
        Assert.Contains("must be a string", diagnostic.Reason);
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
        string gameVersions = """{ "spec_version": 1, "source": "master-server", "versions": ["2026.9.7.5402"] }""") =>
        $$"""
        {
            "snapshot_version": 1,
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
}
