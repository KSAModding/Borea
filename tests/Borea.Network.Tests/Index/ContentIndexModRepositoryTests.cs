using Borea.Core.Index;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.UserContent;
using Borea.Network.Index;
using Borea.Network.Tests.Temp;

namespace Borea.Network.Tests.Index;

public sealed class ContentIndexModRepositoryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "borea-snapshot-provider-tests-" + Guid.NewGuid());

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
    public async Task ReleaseWhoseDownloadIsGone_IsNotOffered_ButStaysInTheHistoryAndForAnExactRead()
    {
        var mod = TestFixtures.FullModMetadata("active-mod", "index");
        var releases = new[]
        {
            Release("active-mod", "1.0.0"),
            Release("active-mod", "2.0.0", gone: true),
            Release("active-mod", "3.0.0", yanked: true),
        };
        var reader = new FakeReader(Snapshot(new ContentIndexListing("active-mod", mod, releases, null)));
        var repository = new ContentIndexModRepository(new FakeFetcher(ContentIndexFetchResult.Downloaded), reader, new TestPathProvider());

        var latest = await repository.GetLatestReleaseAsync("active-mod");
        var versions = await repository.GetAvailableVersionsAsync("active-mod");
        var history = await repository.GetReleaseHistoryAsync("active-mod");
        var gone = await repository.GetReleaseAsync("active-mod", ModVersion.Parse("2.0.0"));

        Assert.Equal(ModVersion.Parse("1.0.0"), latest!.Version);
        Assert.Equal(new[] { ModVersion.Parse("1.0.0") }, versions);
        Assert.Equal(new[] { ModVersion.Parse("2.0.0"), ModVersion.Parse("1.0.0") }, history.Select(release => release.Version));
        Assert.Equal(GoneSince, gone!.Download.UnavailableSince);
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
    public async Task UserContent_ServesVehiclesAndSavesOnly()
    {
        var snapshot = new ContentIndexSnapshot(
            1,
            [new ContentIndexListing("active-mod", TestFixtures.SampleModMetadata("active-mod", "index"), [Release("active-mod", "1.0.0")], null)],
            [],
            null,
            [],
            vehicles:
            [
                Content("rover", ContentType.Vehicle, Release("rover", "1.0.0", type: ContentType.Vehicle), Release("rover", "2.0.0", type: ContentType.Vehicle, status: ReleaseStatus.Testing), Release("rover", "3.0.0", yanked: true, type: ContentType.Vehicle)),
                Content("old-rover", ContentType.Vehicle, delisted: true, Release("old-rover", "1.0.0", type: ContentType.Vehicle)),
            ],
            saves: [Content("campaign", ContentType.Save, Release("campaign", "1.0.0", type: ContentType.Save))]);
        var repository = new ContentIndexModRepository(new FakeFetcher(ContentIndexFetchResult.Downloaded), new FakeReader(snapshot), new TestPathProvider());
        IUserContentRepository content = repository;

        Assert.Equal(ContentType.Vehicle, (await content.FindAsync("ROVER"))!.Type);
        Assert.Equal(ContentType.Save, (await content.FindAsync("campaign"))!.Type);
        Assert.Null(await content.FindAsync("old-rover"));
        Assert.Null(await content.FindAsync("active-mod"));
        Assert.Null(await repository.GetReleaseAsync("rover", ModVersion.Parse("1.0.0")));
        Assert.True((await content.GetReleaseAsync("rover", ModVersion.Parse("3.0.0")))!.Yanked);
        Assert.Equal(ModVersion.Parse("1.0.0"), (await content.GetLatestReleaseAsync("rover", ReleaseChannel.Stable))!.Version);
        Assert.Equal(ModVersion.Parse("2.0.0"), (await content.GetLatestReleaseAsync("rover", ReleaseChannel.Testing))!.Version);
        Assert.Equal("rover", Assert.Single(await content.SearchAsync(ContentType.Vehicle, "rov")).ModId);
        Assert.Empty(await content.SearchAsync(ContentType.Save, "rov"));
        Assert.DoesNotContain(await repository.SearchAsync("rov"), listing => listing.ModId == "rover");
        Assert.Contains("old-rover", await repository.GetClaimedModIdsAsync(), ModIds.Comparer);
        Assert.Contains("campaign", await repository.GetClaimedModIdsAsync(), ModIds.Comparer);
        Assert.Equal(["rover", "campaign"], (await content.GetAvailableAsync()).Select(listing => listing.ModId));
        Assert.Equal(["2.0.0", "1.0.0"], (await content.GetReleaseHistoryAsync("rover")).Select(release => release.Version.ToString()));
        Assert.Empty(await content.GetReleaseHistoryAsync("old-rover"));
        Assert.Empty(await repository.GetReleaseHistoryAsync("rover"));
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

    [Fact]
    public void Status_BeforeAnyRefresh_HasNoOutcomeAndNoCache()
    {
        var provider = new ContentIndexSnapshotProvider(
            new FakeFetcher(ContentIndexFetchResult.Downloaded),
            new FakeReader(Snapshot()),
            new TestPathProvider(Path.Combine(_tempDir, "index.json")));

        Assert.Equal(new ContentIndexRefreshStatus(ContentIndexRefreshOutcome.NotAttempted, CachedAt: null), provider.Status);
    }

    [Fact]
    public async Task RefreshAsync_FetchFailure_KeepsTheCacheAgeAndReportsTheReason()
    {
        var time = new FakeTimeProvider();
        var writtenAt = time.UtcNow - TimeSpan.FromDays(2);
        var fetcher = new FakeFetcher(_ => Task.FromException<ContentIndexFetchResult>(new HttpRequestException("offline")));
        var provider = new ContentIndexSnapshotProvider(fetcher, new FakeReader(Snapshot()), new TestPathProvider(CachedIndex(writtenAt)), time);

        await provider.RefreshAsync();

        Assert.Equal(new ContentIndexRefreshStatus(ContentIndexRefreshOutcome.Failed, writtenAt, "offline"), provider.Status);
    }

    [Fact]
    public async Task RefreshAsync_FreshSnapshot_FetchesAgainAndNotModifiedRestartsTheCacheAge()
    {
        var time = new FakeTimeProvider();
        var fetcher = new FakeFetcher(
            _ => Task.FromException<ContentIndexFetchResult>(new HttpRequestException("offline")),
            _ => Task.FromResult(ContentIndexFetchResult.NotModified));
        var reader = new FakeReader(Snapshot());
        var provider = new ContentIndexSnapshotProvider(fetcher, reader, new TestPathProvider(CachedIndex(time.UtcNow - TimeSpan.FromDays(2))), time);

        await provider.GetSnapshotAsync();
        time.UtcNow += TimeSpan.FromMinutes(1);
        await provider.RefreshAsync();

        Assert.Equal(2, fetcher.CallCount);
        Assert.Equal(1, reader.CallCount);
        Assert.Equal(new ContentIndexRefreshStatus(ContentIndexRefreshOutcome.NotModified, time.UtcNow), provider.Status);
    }

    [Fact]
    public async Task CachedOnly_ServesTheSnapshotInMemoryAndReadsTheFileOnlyWithoutOne()
    {
        var time = new FakeTimeProvider();
        var fetcher = new FakeFetcher(ContentIndexFetchResult.Downloaded);
        var reader = new FakeReader(Snapshot());
        var provider = new ContentIndexSnapshotProvider(fetcher, reader, new TestPathProvider(), time);

        await provider.CachedOnly.GetSnapshotAsync();
        var loaded = await provider.GetSnapshotAsync();
        time.UtcNow += TimeSpan.FromDays(1);
        var cached = await provider.CachedOnly.GetSnapshotAsync();

        Assert.Same(loaded, cached);
        Assert.Equal(1, fetcher.CallCount);
        Assert.Equal(2, reader.CallCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CachedIndex(DateTimeOffset writtenAt)
    {
        Directory.CreateDirectory(_tempDir);
        var indexPath = Path.Combine(_tempDir, "index.json");
        File.WriteAllText(indexPath, "{}");
        File.SetLastWriteTimeUtc(indexPath, writtenAt.UtcDateTime);
        return indexPath;
    }

    private static ContentIndexSnapshot Snapshot(params ContentIndexListing[] listings) => new(
        1,
        listings,
        Array.Empty<ContentIndexPack>(),
        new ContentIndexGameVersions(1, "master-server", new[] { "2026.9.7.5402" }),
        Array.Empty<ContentIndexDiagnostic>());

    private static readonly DateTimeOffset GoneSince = new(2026, 9, 23, 10, 24, 0, TimeSpan.Zero);

    private static ContentIndexListing Content(string id, ContentType type, params ModVersionMetadata[] releases) => Content(id, type, false, releases);

    private static ContentIndexListing Content(string id, ContentType type, bool delisted, params ModVersionMetadata[] releases) => new(
        id,
        new ModMetadata(1, id, "index", id, ["Author"], "Something to fly.", "MIT", new Dictionary<string, string>(), null, type),
        releases,
        delisted ? new IndexStatus(IndexStatusState.Delisted, "delisted") : null);

    private static ModVersionMetadata Release(string modId, string version, bool yanked = false, bool gone = false, ContentType type = ContentType.Mod, ReleaseStatus status = ReleaseStatus.Stable) => new(
        specVersion: 1,
        modId: modId,
        version: ModVersion.Parse(version),
        type: type,
        install: type == ContentType.Mod ? null : new InstallInfo(null, derived: false, folder: "Rover"),
        releaseStatus: status,
        releaseDate: new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
        gameMin: "2026.9.7.5402",
        gameMinRevision: 5402,
        download: new DownloadInfo(
            "https://example.test/mod.zip",
            new string('A', 64),
            1024,
            "application/zip",
            unavailableSince: gone ? GoneSince : null),
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

    private sealed class TestPathProvider(string indexPath = "index.json") : IGamePathProvider
    {
        public string GetIndexPath() => indexPath;
        public string GetAnnouncementsPath() => throw new NotSupportedException();
        public string GetListingSchemaPath() => throw new NotSupportedException();

        public string GetShareHostsPath() => throw new NotSupportedException();
        public string GetImageCacheFolder() => throw new NotSupportedException();
        public string GetGamePatchNotesFolder() => throw new NotSupportedException();
        public string GetAppPreferencesPath() => "app-preferences.json";
        public string GetTaskHistoryPath() => throw new NotSupportedException();
        public string GetAppLockPath() => throw new NotSupportedException();
        public string GetSecretsFolder() => throw new NotSupportedException();
        public string GetInstancesRoot() => throw new NotSupportedException();
        public string GetStaticModFilesRoot() => throw new NotSupportedException();
        public string GetLoadersRoot() => throw new NotSupportedException();
        public string GetPresetsRoot() => throw new NotSupportedException();
        public string GetGameSettingsPresetsRoot() => throw new NotSupportedException();
        public string GetActiveInstancePointerPath() => throw new NotSupportedException();
        public string GetModFavoritesPath() => throw new NotSupportedException();
        public string GetModPackFavoritesPath() => throw new NotSupportedException();
        public string GetBoreaSettingsPath() => throw new NotSupportedException();
        public string GetBackupsRoot() => throw new NotSupportedException();
        public string GetLogsFolder() => throw new NotSupportedException();
        public string GetLanguagesFolder() => throw new NotSupportedException();
        public string GetSharedProfileRoot() => throw new NotSupportedException();
        public string GetInstanceRoot(Guid instanceId) => throw new NotSupportedException();
        public string GetInstanceModsFolder(Guid instanceId) => throw new NotSupportedException();
        public string GetInstanceSavesFolder(Guid instanceId) => throw new NotSupportedException();
        public string GetInstanceVehiclesFolder(Guid instanceId) => throw new NotSupportedException();
        public string GetInstanceSettingsPath(Guid instanceId) => throw new NotSupportedException();
        public string GetInstanceHudLayoutsFolder(Guid instanceId) => throw new NotSupportedException();
        public string GetInstanceCrashDumpsFolder(Guid instanceId) => throw new NotSupportedException();
        public string GetInstanceExportsFolder(Guid instanceId) => throw new NotSupportedException();
        public string GetInstanceGameLogPath(Guid instanceId) => throw new NotSupportedException();
        public string GetInstanceLaunchLogPath(Guid instanceId) => throw new NotSupportedException();
        public string GetInstancePlaytimePath(Guid instanceId) => throw new NotSupportedException();
        public string GetInstanceInstalledContentPath(Guid instanceId) => throw new NotSupportedException();
        public string GetInstanceManifestPath(Guid instanceId) => throw new NotSupportedException();
        public string GetInstanceMetadataPath(Guid instanceId) => throw new NotSupportedException();
        public string? GetGameDirectoryPath() => throw new NotSupportedException();
        public string? GetLoaderDirectoryPath(string loaderId) => throw new NotSupportedException();
    }
}
