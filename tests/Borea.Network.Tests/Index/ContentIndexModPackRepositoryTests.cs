using Borea.Core.Index;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Network.Index;

namespace Borea.Network.Tests.Index;

public sealed class ContentIndexModPackRepositoryTests
{
    [Fact]
    public async Task Queries_AreCaseInsensitiveStableAndSkipRetractedLatestVersion()
    {
        var older = Version("zeta-pack", "1.0.0", name: "Navigation Tools");
        var latest = Version("zeta-pack", "2.0.0", name: "Navigation Tools");
        var retracted = Version(
            "zeta-pack",
            "3.0.0",
            name: "Navigation Tools",
            status: new IndexStatus(IndexStatusState.Retracted, "retracted", reason: "Broken release."));
        var snapshot = Snapshot(
            new ContentIndexPack("zeta-pack", [older, latest, retracted], null),
            new ContentIndexPack("alpha-pack", [Version("alpha-pack", "1.0.0", name: "Science")], null));
        var repository = new ContentIndexModPackRepository(new FixedSnapshotProvider(snapshot));

        var available = await repository.GetAvailableModPacksAsync();
        var selected = await repository.GetLatestAsync("ZETA-PACK");
        var versions = await repository.GetAvailableVersionsAsync("zeta-pack");
        var search = await repository.SearchAsync("navigation");
        var noMatch = await repository.SearchAsync("propulsion");

        Assert.Equal(["alpha-pack", "zeta-pack"], available.Select(result => result.Id));
        Assert.Equal("2.0.0", selected!.Version);
        Assert.Equal(["2.0.0", "1.0.0"], versions.Select(result => result.Version));
        Assert.Equal("zeta-pack", Assert.Single(search).Id);
        Assert.Empty(noMatch);
    }

    [Fact]
    public async Task ExactAndIdentityQueries_RetainWarningsAndUnknownIdentities()
    {
        var retractedStatus = new IndexStatus(IndexStatusState.Retracted, "retracted", reason: "Broken release.");
        var disputedStatus = new IndexStatus(IndexStatusState.Disputed, "disputed", reason: "Ownership is disputed.");
        var unknownStatus = new IndexStatus(IndexStatusState.Unknown, "future-state");
        var delistedStatus = new IndexStatus(IndexStatusState.Delisted, "delisted", reason: "Removed.");
        var diagnostics = new[]
        {
            Diagnostic("Unknown-Pack", null, ContentIndexDiagnosticScope.Pack),
            Diagnostic("mixed-pack", "2.0.0", ContentIndexDiagnosticScope.PackVersion),
            Diagnostic("listing-only", null, ContentIndexDiagnosticScope.Listing),
            new ContentIndexDiagnostic(
                ContentIndexDiagnosticKind.UnsupportedValue,
                ContentIndexDiagnosticScope.IndexStatus,
                "Unknown pack status.",
                "status-pack"),
        };
        var snapshot = new ContentIndexSnapshot(
            1,
            [],
            [
                new ContentIndexPack("retracted-pack", [Version("retracted-pack", "1.0.0", status: retractedStatus)], null),
                new ContentIndexPack("disputed-pack", [Version("disputed-pack", "1.0.0")], disputedStatus),
                new ContentIndexPack("delisted-pack", [], delistedStatus),
                new ContentIndexPack("status-pack", [Version("status-pack", "1.0.0")], unknownStatus),
                new ContentIndexPack("mixed-pack", [Version("mixed-pack", "1.0.0")], null),
            ],
            null,
            diagnostics);
        var repository = new ContentIndexModPackRepository(new FixedSnapshotProvider(snapshot));

        var retracted = await repository.GetVersionAsync("retracted-pack", ModVersion.Parse("1.0.0"));
        var disputed = await repository.GetLatestAsync("disputed-pack");
        var delisted = await repository.GetAsync("DELISTED-PACK");
        var unknownPack = await repository.GetAsync("unknown-pack");
        var unknownVersion = await repository.GetVersionAsync("mixed-pack", ModVersion.Parse("2.0.0"));
        var missingVersion = await repository.GetVersionAsync("status-pack", ModVersion.Parse("9.9.9"));
        var supportedSibling = await repository.GetVersionAsync("mixed-pack", ModVersion.Parse("1.0.0"));
        var unknownState = await repository.GetLatestAsync("status-pack");
        var listingOnly = await repository.GetAsync("listing-only");
        var missing = await repository.GetAsync("missing-pack");

        Assert.Equal(IndexStatusState.Retracted, retracted!.VersionStatus!.State);
        Assert.NotNull(retracted.Metadata);
        Assert.Equal(IndexStatusState.Disputed, disputed!.PackStatus!.State);
        Assert.Null(delisted!.Metadata);
        Assert.Equal(IndexStatusState.Delisted, delisted.PackStatus!.State);
        Assert.Null(unknownPack!.Metadata);
        Assert.Equal("Unknown-Pack", unknownPack.Id);
        Assert.Equal(ContentIndexDiagnosticKind.UnsupportedVersion, Assert.Single(unknownPack.Diagnostics).Kind);
        Assert.Null(unknownVersion!.Metadata);
        Assert.Equal("2.0.0", unknownVersion.Version);
        Assert.Null(missingVersion);
        Assert.NotNull(supportedSibling!.Metadata);
        Assert.Equal(IndexStatusState.Unknown, unknownState!.PackStatus!.State);
        Assert.Single(unknownState.Diagnostics);
        Assert.Null(listingOnly);
        Assert.Null(missing);
    }

    [Fact]
    public async Task ModAndPackRepositories_ShareRefreshCacheAndLastUsableSnapshot()
    {
        var time = new TestTimeProvider();
        var snapshot = Snapshot(new ContentIndexPack("test-pack", [Version("test-pack", "1.0.0")], null));
        var fetcher = new SequenceFetcher(
            _ => Task.FromResult(ContentIndexFetchResult.Downloaded),
            _ => Task.FromException<ContentIndexFetchResult>(new HttpRequestException("offline")));
        var reader = new CountingReader(snapshot);
        var provider = new ContentIndexSnapshotProvider(fetcher, reader, new TestPathProvider(), time);
        var mods = new ContentIndexModRepository(provider);
        var packs = new ContentIndexModPackRepository(provider);

        Assert.Single(await packs.GetAvailableModPacksAsync());
        Assert.Empty(await mods.GetAvailableModsAsync());
        time.UtcNow += TimeSpan.FromMinutes(11);
        Assert.Single(await packs.GetAvailableModPacksAsync());

        Assert.Equal(2, fetcher.CallCount);
        Assert.Equal(1, reader.CallCount);
    }

    [Fact]
    public async Task OfflineFirstQuery_ReadsTheCachedSnapshot()
    {
        var snapshot = Snapshot(new ContentIndexPack("test-pack", [Version("test-pack", "1.0.0")], null));
        var fetcher = new SequenceFetcher(
            _ => Task.FromException<ContentIndexFetchResult>(new HttpRequestException("offline")));
        var reader = new CountingReader(snapshot);
        var provider = new ContentIndexSnapshotProvider(fetcher, reader, new TestPathProvider());

        Assert.Single(await new ContentIndexModPackRepository(provider).GetAvailableModPacksAsync());
        Assert.Equal(1, reader.CallCount);
    }

    [Fact]
    public async Task ModAndPackRepositories_ShareOneConcurrentRefresh()
    {
        var completion = new TaskCompletionSource<ContentIndexFetchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetcher = new SequenceFetcher(_ => completion.Task);
        var reader = new CountingReader(Snapshot(new ContentIndexPack("test-pack", [Version("test-pack", "1.0.0")], null)));
        var provider = new ContentIndexSnapshotProvider(fetcher, reader, new TestPathProvider());

        var packQuery = new ContentIndexModPackRepository(provider).GetAvailableModPacksAsync();
        var modQuery = new ContentIndexModRepository(provider).GetAvailableModsAsync();
        Assert.Equal(1, fetcher.CallCount);

        completion.SetResult(ContentIndexFetchResult.Downloaded);
        await Task.WhenAll(packQuery, modQuery);

        Assert.Single(await packQuery);
        Assert.Empty(await modQuery);
        Assert.Equal(1, reader.CallCount);
    }

    private static ContentIndexDiagnostic Diagnostic(string id, string? version, ContentIndexDiagnosticScope scope) => new(
        ContentIndexDiagnosticKind.UnsupportedVersion,
        scope,
        "The document version is not supported.",
        id,
        version,
        2);

    private static ContentIndexSnapshot Snapshot(params ContentIndexPack[] packs) => new(1, [], packs, null, []);

    private static ContentIndexPackVersion Version(
        string id,
        string version,
        string name = "Test Pack",
        IndexStatus? status = null) => new(
            new ModPackMetadata(
                1,
                id,
                "index",
                name,
                ["Test Author"],
                "A test pack.",
                "CC0-1.0",
                new Dictionary<string, string> { ["forums"] = "https://forums.example/threads/test.1/" },
                "2026.9.7.5402",
                ModVersion.Parse(version),
                new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
                [new ModPackEntry("test-mod", ModVersion.Parse("1.0.0"))]),
            status);

    private sealed class FixedSnapshotProvider(ContentIndexSnapshot snapshot) : IContentIndexSnapshotProvider
    {
        public Task<ContentIndexSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class SequenceFetcher(params Func<CancellationToken, Task<ContentIndexFetchResult>>[] results) : IContentIndexFetcher
    {
        private readonly Queue<Func<CancellationToken, Task<ContentIndexFetchResult>>> _results = new(results);
        public int CallCount { get; private set; }

        public Task<ContentIndexFetchResult> FetchAsync(string destinationPath, CancellationToken ct = default)
        {
            CallCount++;
            return _results.Dequeue()(ct);
        }
    }

    private sealed class CountingReader(ContentIndexSnapshot snapshot) : IContentIndexReader
    {
        public int CallCount { get; private set; }

        public Task<ContentIndexSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class TestPathProvider : IGamePathProvider
    {
        public string GetIndexPath() => "index.json";
        public string GetInstancesRoot() => throw new NotSupportedException();
        public string GetLoadersRoot() => throw new NotSupportedException();
        public string GetActiveInstancePointerPath() => throw new NotSupportedException();
        public string GetModFavoritesPath() => throw new NotSupportedException();
        public string GetModPackFavoritesPath() => throw new NotSupportedException();
        public string GetBoreaSettingsPath() => throw new NotSupportedException();
        public string GetInstanceRoot(Guid instanceId) => throw new NotSupportedException();
        public string GetInstanceModsFolder(Guid instanceId) => throw new NotSupportedException();
        public string GetInstanceSavesFolder(Guid instanceId) => throw new NotSupportedException();
        public string GetInstanceVehiclesFolder(Guid instanceId) => throw new NotSupportedException();
        public string GetInstanceSettingsPath(Guid instanceId) => throw new NotSupportedException();
        public string GetInstanceManifestPath(Guid instanceId) => throw new NotSupportedException();
        public string GetInstanceMetadataPath(Guid instanceId) => throw new NotSupportedException();
        public string? GetGameDirectoryPath() => throw new NotSupportedException();
        public string? GetLoaderDirectoryPath(string loaderId) => throw new NotSupportedException();
    }
}
