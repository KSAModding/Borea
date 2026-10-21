using System.Security.Cryptography;
using Borea.Core.Dependencies;
using Borea.Core.Mods;
using Borea.Storage.Preview;
using Borea.Storage.Tests.Logging;
using Borea.Storage.Tests.Mods;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Preview;

public sealed class CachingModDownloaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly RecordingLog _log = new();
    private readonly PreviewArchiveCache _cache;
    private readonly FakeModDownloader _inner = new() { Bytes = [7, 7, 7] };
    private readonly CachingModDownloader _downloader;

    public CachingModDownloaderTests()
    {
        _paths = new TestGamePathProvider(_root);
        _cache = new PreviewArchiveCache(_paths, _log);
        _downloader = new CachingModDownloader(_inner, _cache, _log);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Target => Path.Combine(_root, "target.zip");

    [Fact]
    public async Task Download_CachedZip_IsCopiedWithoutTheInnerDownloader()
    {
        byte[] bytes = [1, 2, 3, 4];
        var release = Release(Sha(bytes));
        PutInCache(release, bytes);

        var result = await _downloader.DownloadAsync(release, Target);

        Assert.Empty(_inner.ArchivePaths);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Target));
        Assert.Equal(new DownloadResult(release.Download.Url, bytes.Length, Sha(bytes)), result);
    }

    [Fact]
    public async Task Download_CorruptCachedFile_IsDeleted_AndTheInnerDownloaderIsCalled()
    {
        var release = Release(Sha([1, 2, 3, 4]));
        var cached = PutInCache(release, [9, 9]);

        var result = await _downloader.DownloadAsync(release, Target);

        Assert.Equal([Target], _inner.ArchivePaths);
        Assert.False(File.Exists(cached));
        Assert.Equal(_inner.Bytes, await File.ReadAllBytesAsync(Target));
        Assert.Equal(Sha(_inner.Bytes), result.Sha256);
    }

    [Fact]
    public async Task Download_ReleaseWithoutSha256_GoesToTheInnerDownloader()
    {
        var release = Release(sha256: null);

        await _downloader.DownloadAsync(release, Target);

        Assert.Equal([Target], _inner.ArchivePaths);
    }

    private string PutInCache(ModVersionMetadata release, byte[] bytes)
    {
        var path = Path.Combine(_paths.GetPreviewCacheFolder(), "archives", release.Download.Sha256!.ToLowerInvariant() + ".zip");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static ModVersionMetadata Release(string? sha256) => new(
        specVersion: 1,
        modId: "Rover",
        version: ModVersion.Parse("1.0.0"),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
        gameMin: "2026.10.19.5638",
        gameMinRevision: 5638,
        download: new DownloadInfo("https://example.invalid/Rover.zip", sha256, null, "application/zip"),
        installSizeBytes: null,
        dependencies: Array.Empty<ModDependency>());
}
