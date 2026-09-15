using System.Security.Cryptography;
using Borea.Storage.Images;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Images;

public sealed class FileContentImageCacheTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly TestGamePathProvider _paths;

    public FileContentImageCacheTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
        _paths = new TestGamePathProvider(_tempRoot);
    }

    [Fact]
    public async Task WriteThenRead_ReturnsTheBytesFromTheImageCacheFolder()
    {
        var cache = new FileContentImageCache(_paths);
        var bytes = Bytes(1);

        await cache.WriteAsync(Sha256Of(bytes), bytes);

        Assert.Equal(bytes, await cache.ReadAsync(Sha256Of(bytes)));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(PathOf(bytes)));
    }

    [Fact]
    public async Task ReadAsync_NothingStored_ReturnsNull()
    {
        var cache = new FileContentImageCache(_paths);

        Assert.Null(await cache.ReadAsync(Sha256Of(Bytes(1))));
        Assert.False(Directory.Exists(_tempRoot));
    }

    [Fact]
    public async Task ReadAsync_FileNoLongerHasItsDigest_ReturnsNullAndDeletesTheFile()
    {
        var cache = new FileContentImageCache(_paths);
        var bytes = Bytes(1);
        await cache.WriteAsync(Sha256Of(bytes), bytes);
        await File.WriteAllBytesAsync(PathOf(bytes), Bytes(2));

        Assert.Null(await cache.ReadAsync(Sha256Of(bytes)));
        Assert.False(File.Exists(PathOf(bytes)));
    }

    [Fact]
    public async Task WriteAsync_PastTheSizeBound_RemovesTheLeastRecentlyUsedFiles()
    {
        var cache = new FileContentImageCache(_paths, maxBytes: 250);
        var first = Bytes(1);
        var second = Bytes(2);
        var third = Bytes(3);
        await cache.WriteAsync(Sha256Of(first), first);
        await cache.WriteAsync(Sha256Of(second), second);
        File.SetLastWriteTimeUtc(PathOf(first), DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(PathOf(second), DateTime.UtcNow.AddHours(-1));
        await cache.ReadAsync(Sha256Of(first));

        await cache.WriteAsync(Sha256Of(third), third);

        Assert.True(File.Exists(PathOf(first)));
        Assert.False(File.Exists(PathOf(second)));
        Assert.True(File.Exists(PathOf(third)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("zz23456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("../0123456789abcdef0123456789abcdef0123456789abcdef0123456789abc")]
    public async Task ReadAsync_DigestIsNot64HexCharacters_Throws(string sha256)
    {
        var cache = new FileContentImageCache(_paths);

        await Assert.ThrowsAsync<ArgumentException>(() => cache.ReadAsync(sha256));
    }

    private static byte[] Bytes(byte fill) => Enumerable.Repeat(fill, 100).ToArray();

    private static string Sha256Of(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private string PathOf(byte[] bytes) => Path.Combine(_paths.GetImageCacheFolder(), Sha256Of(bytes).ToLowerInvariant());

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
