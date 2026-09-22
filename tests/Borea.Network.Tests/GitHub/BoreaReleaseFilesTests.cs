using System.Net;
using System.Security.Cryptography;
using System.Text;
using Borea.Core.Mods;
using Borea.Network.GitHub;

namespace Borea.Network.Tests;

public sealed class BoreaReleaseFilesTests : IDisposable
{
    private const string ArchiveUrl = "https://github.com/KSAModding/Borea/releases/download/v0.4.0/Borea-0.4.0-win-x64.zip";

    private readonly string _path = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }

    private static BoreaReleaseFiles Files(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(FakeHttpMessageHandler.BuildClient(responder, out _));

    [Fact]
    public async Task DownloadAsync_WritesTheFileAndHashesWhatArrived()
    {
        var content = Encoding.UTF8.GetBytes(new string('b', 200000));
        var files = Files(_ => FakeHttpMessageHandler.ByteResponse(content));
        var reported = new List<long>();

        var hash = await files.DownloadAsync(ArchiveUrl, _path, new CollectingProgress(reported));

        Assert.Equal(Convert.ToHexString(SHA256.HashData(content)), hash);
        Assert.Equal(content, await File.ReadAllBytesAsync(_path));
        Assert.Equal(content.Length, reported[^1]);
    }

    [Fact]
    public async Task DownloadAsync_AFailedRequest_LeavesNoFileBehind()
    {
        var files = Files(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<HttpRequestException>(() => files.DownloadAsync(ArchiveUrl, _path));

        Assert.False(File.Exists(_path));
    }

    [Fact]
    public async Task DownloadAsync_AUrlOutsideTheBoreaReleases_IsNotFetched()
    {
        var files = Files(_ => throw new InvalidOperationException("nothing may be fetched"));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => files.DownloadAsync("https://example.com/Borea-0.4.0-win-x64.zip", _path));
    }

    [Fact]
    public async Task ReadTextAsync_ReadsASmallPublishedFile()
    {
        var files = Files(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("abc  Borea.zip\n") });

        Assert.Equal("abc  Borea.zip\n", await files.ReadTextAsync(ArchiveUrl));
    }

    [Fact]
    public async Task ReadTextAsync_AFileAboveTheLimitOrAFailedRequest_IsNull()
    {
        var tooLong = new string('x', BoreaReleaseFiles.MaxTextBytes + 1);

        Assert.Null(await Files(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(tooLong) }).ReadTextAsync(ArchiveUrl));
        Assert.Null(await Files(_ => new HttpResponseMessage(HttpStatusCode.NotFound)).ReadTextAsync(ArchiveUrl));
        Assert.Null(await Files(_ => throw new InvalidOperationException("nothing may be fetched")).ReadTextAsync("https://example.com/SHA256SUMS.txt"));
    }

    private sealed class CollectingProgress(List<long> received) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => received.Add(value.BytesDownloaded);
    }
}
