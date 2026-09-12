using Borea.Core.Index;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Network.Index;
using Borea.Network.Tests.Temp;

namespace Borea.Network.Tests.Index;

public sealed class ContentIndexModRepositoryTests
{
    [Fact]
    public void Constructor_NullFetcher_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ContentIndexModRepository(
            null!, new FakeReader(Snapshot()), new TestPathProvider()));
    }

    [Fact]
    public async Task Accessors_UseUsableListingsAndReleasesFromOneCachedRead()
    {
        var active = TestFixtures.FullModMetadata("active-mod", "index");
        var hidden = TestFixtures.SampleModMetadata("hidden-mod", "index");
        var releases = new[]
        {
            Release("active-mod", "1.0.0"),
            Release("active-mod", "2.0.0"),
            Release("active-mod", "3.0.0", yanked: true),
        };
        var snapshot = Snapshot(
            new ContentIndexListing("active-mod", active, releases, null),
            new ContentIndexListing(
                "hidden-mod",
                hidden,
                Array.Empty<ModVersionMetadata>(),
                new IndexStatus(IndexStatusState.Delisted, "delisted")));
        var fetcher = new FakeFetcher(ContentIndexFetchResult.Downloaded);
        var reader = new FakeReader(snapshot);
        var repository = new ContentIndexModRepository(fetcher, reader, new TestPathProvider());

        var available = await repository.GetAvailableModsAsync();
        var latest = await repository.GetLatestReleaseAsync("ACTIVE-MOD");
        var yanked = await repository.GetReleaseAsync("active-mod", ModVersion.Parse("3.0.0"));
        var versions = await repository.GetAvailableVersionsAsync("active-mod");
        var search = await repository.SearchAsync("tools");

        Assert.Equal("active-mod", Assert.Single(available).ModId);
        Assert.Equal(ModVersion.Parse("2.0.0"), latest!.Version);
        Assert.True(yanked!.Yanked);
        Assert.Equal(new[] { ModVersion.Parse("2.0.0"), ModVersion.Parse("1.0.0") }, versions);
        Assert.Equal("active-mod", Assert.Single(search).ModId);
        Assert.Equal(1, fetcher.CallCount);
        Assert.Equal(1, reader.CallCount);
    }

    [Fact]
    public async Task GetAvailableModsAsync_NotModifiedWithinNewInterval_DoesNotReadAgain()
    {
        var time = new FakeTimeProvider();
        var fetcher = new FakeFetcher(ContentIndexFetchResult.Downloaded, ContentIndexFetchResult.NotModified);
        var reader = new FakeReader(Snapshot(new ContentIndexListing(
            "test-mod",
            TestFixtures.SampleModMetadata("test-mod", "index"),
            Array.Empty<ModVersionMetadata>(),
            null)));
        var repository = new ContentIndexModRepository(fetcher, reader, new TestPathProvider(), time);

        await repository.GetAvailableModsAsync();
        await repository.GetAvailableModsAsync();
        time.UtcNow += TimeSpan.FromMinutes(11);
        await repository.GetAvailableModsAsync();

        Assert.Equal(2, fetcher.CallCount);
        Assert.Equal(1, reader.CallCount);
    }

    [Fact]
    public async Task GetAvailableModsAsync_ConcurrentCallersShareRefresh()
    {
        var fetchCompletion = new TaskCompletionSource<ContentIndexFetchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetcher = new FakeFetcher(_ => fetchCompletion.Task);
        var reader = new FakeReader(Snapshot(new ContentIndexListing(
            "test-mod",
            TestFixtures.SampleModMetadata("test-mod", "index"),
            Array.Empty<ModVersionMetadata>(),
            null)));
        var repository = new ContentIndexModRepository(fetcher, reader, new TestPathProvider());

        var first = repository.GetAvailableModsAsync();
        var second = repository.GetAvailableModsAsync();
        fetchCompletion.SetResult(ContentIndexFetchResult.Downloaded);
        await Task.WhenAll(first, second);

        Assert.Equal(1, fetcher.CallCount);
        Assert.Equal(1, reader.CallCount);
    }

    [Fact]
    public async Task GetAvailableModsAsync_CanceledCallerDoesNotCancelSharedRefresh()
    {
        var fetchCompletion = new TaskCompletionSource<ContentIndexFetchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetcher = new FakeFetcher(_ => fetchCompletion.Task);
        var reader = new FakeReader(Snapshot(new ContentIndexListing(
            "test-mod",
            TestFixtures.SampleModMetadata("test-mod", "index"),
            Array.Empty<ModVersionMetadata>(),
            null)));
        var repository = new ContentIndexModRepository(fetcher, reader, new TestPathProvider());
        using var cancellation = new CancellationTokenSource();

        var canceledCall = repository.GetAvailableModsAsync(cancellation.Token);
        var successfulCall = repository.GetAvailableModsAsync();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledCall);
        fetchCompletion.SetResult(ContentIndexFetchResult.Downloaded);

        Assert.Single(await successfulCall);
        Assert.Equal(1, fetcher.CallCount);
        Assert.Equal(1, reader.CallCount);
    }

    [Fact]
    public async Task GetAvailableModsAsync_FetchFailureUsesDiskSnapshot()
    {
        var fetcher = new FakeFetcher(_ => Task.FromException<ContentIndexFetchResult>(new HttpRequestException("offline")));
        var reader = new FakeReader(Snapshot(new ContentIndexListing(
            "test-mod",
            TestFixtures.SampleModMetadata("test-mod", "index"),
            Array.Empty<ModVersionMetadata>(),
            null)));
        var repository = new ContentIndexModRepository(fetcher, reader, new TestPathProvider());

        var available = await repository.GetAvailableModsAsync();

        Assert.Single(available);
        Assert.Equal(1, fetcher.CallCount);
        Assert.Equal(1, reader.CallCount);
    }

    [Fact]
    public async Task GetAvailableModsAsync_RefreshFailureKeepsLastUsableSnapshot()
    {
        var time = new FakeTimeProvider();
        var fetcher = new FakeFetcher(
            _ => Task.FromResult(ContentIndexFetchResult.Downloaded),
            _ => Task.FromException<ContentIndexFetchResult>(new HttpRequestException("offline")));
        var reader = new FakeReader(Snapshot(new ContentIndexListing(
            "test-mod",
            TestFixtures.SampleModMetadata("test-mod", "index"),
            Array.Empty<ModVersionMetadata>(),
            null)));
        var repository = new ContentIndexModRepository(fetcher, reader, new TestPathProvider(), time);

        await repository.GetAvailableModsAsync();
        time.UtcNow += TimeSpan.FromMinutes(11);
        var available = await repository.GetAvailableModsAsync();

        Assert.Single(available);
        Assert.Equal(2, fetcher.CallCount);
        Assert.Equal(1, reader.CallCount);
    }

    [Fact]
    public async Task GetClaimedModIdsAsync_IncludesHiddenAndUnsupportedIds()
    {
        var hidden = new ContentIndexListing(
            "hidden-mod",
            TestFixtures.SampleModMetadata("hidden-mod", "index"),
            Array.Empty<ModVersionMetadata>(),
            new IndexStatus(IndexStatusState.Delisted, "delisted"));
        var snapshot = new ContentIndexSnapshot(
            1,
            new[] { hidden },
            Array.Empty<ContentIndexPack>(),
            null,
            new[]
            {
                new ContentIndexDiagnostic(
                    ContentIndexDiagnosticKind.UnsupportedVersion,
                    ContentIndexDiagnosticScope.Listing,
                    "Unsupported authored version.",
                    "future-mod",
                    SpecVersion: 2),
            });
        var repository = new ContentIndexModRepository(
            new FakeFetcher(ContentIndexFetchResult.Downloaded),
            new FakeReader(snapshot),
            new TestPathProvider());

        var claims = await repository.GetClaimedModIdsAsync();

        Assert.Contains("hidden-mod", claims, ModIds.Comparer);
        Assert.Contains("future-mod", claims, ModIds.Comparer);
    }

    [Fact]
    public async Task FindBySha256Async_RequiresOneUsableModWithMatchingIdAndHash()
    {
        var digest = new string('A', 64);
        var snapshot = Snapshot(
            new ContentIndexListing(
                "target-mod",
                TestFixtures.SampleModMetadata("target-mod", "index"),
                [Release("target-mod", "1.0.0")],
                null),
            new ContentIndexListing(
                "other-mod",
                TestFixtures.SampleModMetadata("other-mod", "index"),
                [Release("other-mod", "1.0.0")],
                null),
            new ContentIndexListing(
                "hidden-mod",
                TestFixtures.SampleModMetadata("hidden-mod", "index"),
                [Release("hidden-mod", "1.0.0")],
                new IndexStatus(IndexStatusState.Delisted, "delisted")));
        var repository = new ContentIndexModRepository(
            new FakeFetcher(ContentIndexFetchResult.Downloaded),
            new FakeReader(snapshot),
            new TestPathProvider());

        var match = await repository.FindBySha256Async("TARGET-MOD", digest);
        var wrongId = await repository.FindBySha256Async("missing-mod", digest);
        var unknownArchive = await repository.FindBySha256Async("target-mod", new string('B', 64));
        var delisted = await repository.FindBySha256Async("hidden-mod", digest);

        Assert.Equal("target-mod", match!.ModId);
        Assert.Null(wrongId);
        Assert.Null(unknownArchive);
        Assert.Null(delisted);
    }

    [Fact]
    public async Task GetDiagnosticsAsync_ExposesUnknownModerationWarning()
    {
        var diagnostic = new ContentIndexDiagnostic(
            ContentIndexDiagnosticKind.UnsupportedValue,
            ContentIndexDiagnosticScope.IndexStatus,
            "Index status state 'future-state' is not supported.",
            "test-mod");
        var snapshot = new ContentIndexSnapshot(
            1,
            Array.Empty<ContentIndexListing>(),
            Array.Empty<ContentIndexPack>(),
            null,
            new[] { diagnostic });
        var repository = new ContentIndexModRepository(
            new FakeFetcher(ContentIndexFetchResult.Downloaded),
            new FakeReader(snapshot),
            new TestPathProvider());

        Assert.Same(diagnostic, Assert.Single(await repository.GetDiagnosticsAsync()));
    }

    private static ContentIndexSnapshot Snapshot(params ContentIndexListing[] listings) => new(
        1,
        listings,
        Array.Empty<ContentIndexPack>(),
        new ContentIndexGameVersions(1, "master-server", new[] { "2026.9.7.5402" }),
        Array.Empty<ContentIndexDiagnostic>());

    private static ModVersionMetadata Release(string modId, string version, bool yanked = false) => new(
        specVersion: 1,
        modId: modId,
        version: ModVersion.Parse(version),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
        gameMin: "2026.9.7.5402",
        gameMinRevision: 5402,
        download: new DownloadInfo(
            "https://example.test/mod.zip",
            new string('A', 64),
            1024,
            "application/zip"),
        installSizeBytes: 2048,
        dependencies: Array.Empty<Borea.Core.Dependencies.ModDependency>(),
        yanked: yanked,
        yankedReason: yanked ? "Retracted." : null,
        source: "index");

    private sealed class FakeFetcher : IContentIndexFetcher
    {
        private readonly Queue<Func<CancellationToken, Task<ContentIndexFetchResult>>> _results;

        public int CallCount { get; private set; }

        public FakeFetcher(params ContentIndexFetchResult[] results)
            : this(results.Select(result => new Func<CancellationToken, Task<ContentIndexFetchResult>>(
                _ => Task.FromResult(result))).ToArray())
        {
        }

        public FakeFetcher(params Func<CancellationToken, Task<ContentIndexFetchResult>>[] results)
        {
            _results = new Queue<Func<CancellationToken, Task<ContentIndexFetchResult>>>(results);
        }

        public Task<ContentIndexFetchResult> FetchAsync(string destinationPath, CancellationToken ct = default)
        {
            CallCount++;
            return _results.Dequeue()(ct);
        }
    }

    private sealed class FakeReader : IContentIndexReader
    {
        private readonly ContentIndexSnapshot _snapshot;

        public int CallCount { get; private set; }

        public FakeReader(ContentIndexSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public Task<ContentIndexSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_snapshot);
        }
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
