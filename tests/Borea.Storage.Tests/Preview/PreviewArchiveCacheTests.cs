using System.Security.Cryptography;
using Borea.Core.Dependencies;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Storage.Files;
using Borea.Storage.Preview;
using Borea.Storage.Tests.Logging;
using Borea.Storage.Tests.Mods;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Preview;

public sealed class PreviewArchiveCacheTests : IDisposable
{
    private static readonly IFreeSpaceProbe PlentyOfSpace = new FixedFreeSpace(long.MaxValue);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly RecordingLog _log = new();

    public PreviewArchiveCacheTests() => _paths = new TestGamePathProvider(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Archives => Path.Combine(_paths.GetPreviewCacheFolder(), "archives");

    [Fact]
    public async Task Fetch_KeepsOnlyTheVerifiedZipInsideTheCache_AndASecondFetchDoesNotDownload()
    {
        byte[] bytes = [1, 2, 3];
        var cache = new PreviewArchiveCache(_paths, _log);
        Assert.False(Directory.Exists(_root));

        var downloader = new FakeModDownloader { Bytes = bytes };
        var release = Release(bytes);
        var first = await cache.FetchAsync(release, downloader, PlentyOfSpace);
        var second = await cache.FetchAsync(release, downloader, PlentyOfSpace);

        Assert.Equal(Path.Combine(Archives, Convert.ToHexStringLower(SHA256.HashData(bytes)) + ".zip"), first);
        Assert.Equal(first, second);
        Assert.Single(downloader.ArchivePaths);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(first));
        Assert.Equal([first], Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
        Assert.Equal(first, cache.TryGet(release.Download.Sha256!));
    }

    [Fact]
    public async Task Fetch_BytesWithAnotherHash_LeaveNoFile()
    {
        var cache = new PreviewArchiveCache(_paths, _log);
        var downloader = new FakeModDownloader { Bytes = [9, 9] };

        await Assert.ThrowsAsync<DownloadFailedException>(() => cache.FetchAsync(Release([1, 2, 3]), downloader, PlentyOfSpace));

        Assert.Empty(Directory.GetFiles(Archives));
    }

    [Fact]
    public async Task Fetch_TwoAtOnceOfOneHash_DownloadOnceAndLeaveOneFile()
    {
        byte[] bytes = [4, 5, 6];
        var cache = new PreviewArchiveCache(_paths, _log);
        var release = Release(bytes);
        var started = new TaskCompletionSource();
        var finish = new TaskCompletionSource();
        var downloader = new FakeModDownloader
        {
            Bytes = bytes,
            Downloading = async (_, _) =>
            {
                started.TrySetResult();
                await finish.Task;
            },
        };

        var first = cache.FetchAsync(release, downloader, PlentyOfSpace);
        await started.Task;
        var second = cache.FetchAsync(release, downloader, PlentyOfSpace);
        finish.SetResult();

        Assert.Equal(await first, await second);
        Assert.Single(downloader.ArchivePaths);
        Assert.Single(Directory.GetFiles(Archives));
    }

    [Fact]
    public async Task Fetch_MoreBytesThanTheReleaseStates_IsStopped_AndLeavesNoFile()
    {
        const long stated = 3;
        var cache = new PreviewArchiveCache(_paths, _log);
        var downloader = new FakeModDownloader { Bytes = [1, 2, 3] };
        downloader.Downloading = async (_, token) =>
        {
            downloader.LastProgress!.Report(new DownloadProgress(stated + 1, -1));
            await Task.Delay(Timeout.Infinite, token);
        };

        await Assert.ThrowsAsync<DownloadFailedException>(() => cache.FetchAsync(Release([1, 2, 3], sizeBytes: stated), downloader, PlentyOfSpace));

        Assert.Empty(Directory.GetFiles(Archives));
    }

    [Fact]
    public async Task Fetch_LowSpaceOrNoHash_IsRefusedBeforeTheDownload()
    {
        var cache = new PreviewArchiveCache(_paths, _log);
        var downloader = new FakeModDownloader { Bytes = [1] };
        var release = Release([1], sizeBytes: 100L * 1000 * 1000);

        // The archive needs 100 MB plus the 50 MB minimum margin.
        await Assert.ThrowsAsync<InsufficientDiskSpaceException>(() => cache.FetchAsync(release, downloader, new FixedFreeSpace(149L * 1000 * 1000)));
        await Assert.ThrowsAsync<ArgumentException>(() => cache.FetchAsync(Release([1], hashed: false), downloader, PlentyOfSpace));

        Assert.Empty(downloader.ArchivePaths);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void Sweep_EvictsTheLeastRecentlyUsedFirst_AndKeepsAPinnedOne()
    {
        var cache = new PreviewArchiveCache(_paths, _log, capacityBytes: 250);
        var now = DateTime.UtcNow;
        var oldestPinned = Archive('a', now.AddHours(-3));
        var older = Archive('b', now.AddHours(-2));
        var newest = Archive('c', now.AddHours(-1));

        using (cache.Pin(new string('A', 64)))
            Assert.Equal(1, cache.Sweep());

        Assert.True(File.Exists(oldestPinned));
        Assert.False(File.Exists(older));
        Assert.True(File.Exists(newest));
    }

    [Fact]
    public void Sweep_DeletesStalePartFilesAndOtherVersionFolders_AndKeepsFreshParts()
    {
        var cache = new PreviewArchiveCache(_paths, _log);
        Directory.CreateDirectory(Archives);
        var stale = Path.Combine(Archives, "x.1.part");
        var fresh = Path.Combine(Archives, "x.2.part");
        File.WriteAllBytes(stale, [1]);
        File.WriteAllBytes(fresh, [1]);
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-25));
        var oldVersion = Path.Combine(_root, "PreviewCache", "v0");
        Directory.CreateDirectory(Path.Combine(oldVersion, "archives"));

        Assert.Equal(2, cache.Sweep());

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));
        Assert.False(Directory.Exists(oldVersion));
        Assert.True(Directory.Exists(Archives));
    }

    private string Archive(char hex, DateTime lastUse)
    {
        Directory.CreateDirectory(Archives);
        var path = Path.Combine(Archives, new string(hex, 64) + ".zip");
        File.WriteAllBytes(path, new byte[100]);
        File.SetLastWriteTimeUtc(path, lastUse);
        return path;
    }

    private static ModVersionMetadata Release(byte[] bytes, long? sizeBytes = null, bool hashed = true) => new(
        specVersion: 1,
        modId: "Rover",
        version: ModVersion.Parse("1.0.0"),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
        gameMin: "2026.10.19.5638",
        gameMinRevision: 5638,
        download: new DownloadInfo("https://example.invalid/Rover.zip", hashed ? Convert.ToHexString(SHA256.HashData(bytes)) : null, sizeBytes, "application/zip"),
        installSizeBytes: null,
        dependencies: Array.Empty<ModDependency>());

    private sealed class FixedFreeSpace(long availableBytes) : IFreeSpaceProbe
    {
        public FreeSpace? Measure(string path) => new("Test", availableBytes);
    }
}
