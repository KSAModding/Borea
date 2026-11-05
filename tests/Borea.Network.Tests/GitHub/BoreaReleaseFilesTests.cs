using System.Net;
using System.Security.Cryptography;
using System.Text;
using Borea.Core.Logging;
using Borea.Core.Mods;
using Borea.Core.Updates;
using Borea.Network.GitHub;

namespace Borea.Network.Tests;

public sealed class BoreaReleaseFilesTests : IDisposable
{
    private const string ArchiveUrl = "https://github.com/KSAModding/Borea/releases/download/v0.4.0/Borea-0.4.0-win-x64.zip";

    private const string ApiUrl = "https://api.github.com/repos/KSAModding/Borea/releases/assets/713012505";

    private static readonly BoreaReleaseAsset Archive = new("Borea-0.4.0-win-x64.zip", ArchiveUrl) { ApiUrl = ApiUrl };

    private readonly string _path = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly ListLog _log = new();
    private readonly List<HttpRequestMessage> _requests = [];

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }

    private BoreaReleaseFiles Files(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(FakeHttpMessageHandler.BuildClient(
            request =>
            {
                _requests.Add(request);
                return responder(request);
            },
            out _), _log);

    /// <summary>Answers the download link with <paramref name="link"/> and the API address with <paramref name="api"/>.</summary>
    private BoreaReleaseFiles FilesAnswering(Func<HttpResponseMessage> link, Func<HttpResponseMessage> api)
        => Files(request => request.RequestUri!.AbsoluteUri == ApiUrl ? api() : link());

    private string[] RequestedUrls() => _requests.Select(request => request.RequestUri!.AbsoluteUri).ToArray();

    private static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    private static HttpResponseMessage Text(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };

    [Fact]
    public async Task DownloadAsync_WritesTheFileAndHashesWhatArrived()
    {
        var content = Encoding.UTF8.GetBytes(new string('b', 200000));
        var files = Files(_ => FakeHttpMessageHandler.ByteResponse(content));
        var reported = new List<long>();

        var hash = await files.DownloadAsync(Archive, _path, new CollectingProgress(reported));

        Assert.Equal(Convert.ToHexString(SHA256.HashData(content)), hash);
        Assert.Equal(content, await File.ReadAllBytesAsync(_path));
        Assert.Equal(content.Length, reported[^1]);
        Assert.Equal([ArchiveUrl], RequestedUrls());
        Assert.Contains(_log.Lines, line => line.Contains($"came from {ArchiveUrl}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DownloadAsync_AFailedRequest_LeavesNoFileBehind()
    {
        var files = Files(_ => Status(HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<HttpRequestException>(() => files.DownloadAsync(Archive, _path));

        Assert.False(File.Exists(_path));
    }

    [Fact]
    public async Task DownloadAsync_AUrlOutsideTheBoreaReleases_IsNotFetched()
    {
        var files = Files(_ => throw new InvalidOperationException("nothing may be fetched"));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => files.DownloadAsync(new BoreaReleaseAsset("Borea-0.4.0-win-x64.zip", "https://example.com/Borea-0.4.0-win-x64.zip") { ApiUrl = ApiUrl }, _path));
    }

    [Theory]
    [InlineData("504")]
    [InlineData("500")]
    [InlineData("transport")]
    [InlineData("timeout")]
    public async Task DownloadAsync_ADownloadLinkThatFailsInTransit_IsAskedOnceMoreThroughTheApiAddress(string failure)
    {
        var content = Encoding.UTF8.GetBytes("the archive");
        var files = FilesAnswering(
            () => failure switch
            {
                "504" => Status(HttpStatusCode.GatewayTimeout),
                "500" => Status(HttpStatusCode.InternalServerError),
                "transport" => throw new HttpRequestException(HttpRequestError.ConnectionError, "The connection was reset."),
                _ => throw new TaskCanceledException("The request timed out."),
            },
            () => FakeHttpMessageHandler.ByteResponse(content));

        var hash = await files.DownloadAsync(Archive, _path);

        Assert.Equal(Convert.ToHexString(SHA256.HashData(content)), hash);
        Assert.Equal(content, await File.ReadAllBytesAsync(_path));
        Assert.Equal([ArchiveUrl, ApiUrl], RequestedUrls());
        Assert.Contains(_log.Lines, line => line.Contains($"came from {ApiUrl}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DownloadAsync_TheApiAddress_IsAskedForTheBytesOfTheFile()
    {
        var files = FilesAnswering(() => Status(HttpStatusCode.GatewayTimeout), () => FakeHttpMessageHandler.ByteResponse([1, 2, 3]));

        await files.DownloadAsync(Archive, _path);

        var api = _requests[^1];
        Assert.Equal(["application/octet-stream"], api.Headers.Accept.Select(accept => accept.MediaType));
        Assert.Equal([BoreaReleaseCheck.ApiVersion], api.Headers.GetValues("X-GitHub-Api-Version"));
        Assert.Empty(_requests[0].Headers.Accept);
    }

    [Fact]
    public async Task DownloadAsync_FromTheApiAddress_HandsBackTheHashOfWhatArrivedThere()
    {
        var expected = Encoding.UTF8.GetBytes("the archive the checksums record");
        var other = Encoding.UTF8.GetBytes("other bytes");
        var files = FilesAnswering(() => Status(HttpStatusCode.GatewayTimeout), () => FakeHttpMessageHandler.ByteResponse(other));

        var hash = await files.DownloadAsync(Archive, _path);

        // the caller compares this with the checksums, so other bytes cannot pass as the archive
        Assert.Equal(Convert.ToHexString(SHA256.HashData(other)), hash);
        Assert.NotEqual(Convert.ToHexString(SHA256.HashData(expected)), hash);
    }

    [Fact]
    public async Task DownloadAsync_BothAddressesFail_ThrowsAndLeavesNoFileBehind()
    {
        var files = FilesAnswering(() => Status(HttpStatusCode.GatewayTimeout), () => Status(HttpStatusCode.BadGateway));

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => files.DownloadAsync(Archive, _path));

        Assert.Equal(HttpStatusCode.BadGateway, failure.StatusCode);
        Assert.False(File.Exists(_path));
        Assert.Equal([ArchiveUrl, ApiUrl], RequestedUrls());
    }

    [Fact]
    public async Task DownloadAsync_ANotFoundOrAFileAboveTheLimit_IsNotAskedAgain()
    {
        var tooLarge = new ByteArrayContent([]);
        tooLarge.Headers.ContentLength = BoreaReleaseFiles.MaxArchiveBytes + 1;

        await Assert.ThrowsAsync<HttpRequestException>(
            () => FilesAnswering(() => Status(HttpStatusCode.NotFound), () => throw new InvalidOperationException("the API address may not be asked")).DownloadAsync(Archive, _path));
        await Assert.ThrowsAsync<HttpRequestException>(
            () => FilesAnswering(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = tooLarge }, () => throw new InvalidOperationException("the API address may not be asked")).DownloadAsync(Archive, _path));

        Assert.Equal([ArchiveUrl, ArchiveUrl], RequestedUrls());
    }

    [Fact]
    public async Task DownloadAsync_TheCallersCancellation_IsNotAskedAgain()
    {
        using var cancellation = new CancellationTokenSource();
        var files = FilesAnswering(
            () =>
            {
                cancellation.Cancel();
                throw new TaskCanceledException();
            },
            () => throw new InvalidOperationException("the API address may not be asked"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => files.DownloadAsync(Archive, _path, cancellationToken: cancellation.Token));

        Assert.Equal([ArchiveUrl], RequestedUrls());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://example.com/repos/KSAModding/Borea/releases/assets/713012505")]
    [InlineData("http://api.github.com/repos/KSAModding/Borea/releases/assets/713012505")]
    [InlineData("https://api.github.com.example.com/repos/KSAModding/Borea/releases/assets/713012505")]
    [InlineData("https://api.github.com/repos/Other/Repo/releases/assets/713012505")]
    [InlineData("https://api.github.com/repos/KSAModding/Borea/releases/assets/")]
    [InlineData("https://api.github.com/repos/KSAModding/Borea/releases/assets/1/../../../../other")]
    [InlineData("https://api.github.com/repos/KSAModding/Borea/releases/assets/1?redirect=https://example.com")]
    public async Task DownloadAsync_AnApiAddressOutsideTheBoreaReleaseFiles_IsNeverAsked(string? apiUrl)
    {
        var files = Files(request => request.RequestUri!.AbsoluteUri == ArchiveUrl
            ? Status(HttpStatusCode.GatewayTimeout)
            : throw new InvalidOperationException("only the download link may be asked"));

        await Assert.ThrowsAsync<HttpRequestException>(() => files.DownloadAsync(Archive with { ApiUrl = apiUrl }, _path));

        Assert.Equal([ArchiveUrl], RequestedUrls());
    }

    [Fact]
    public async Task ReadTextAsync_ReadsASmallPublishedFile()
    {
        var files = Files(_ => Text("abc  Borea.zip\n"));

        Assert.Equal("abc  Borea.zip\n", await files.ReadTextAsync(Archive));
    }

    [Fact]
    public async Task ReadTextAsync_AFileAboveTheLimitOrAFailedRequest_IsNull()
    {
        var tooLong = new string('x', BoreaReleaseFiles.MaxTextBytes + 1);

        Assert.Null(await Files(_ => Text(tooLong)).ReadTextAsync(Archive));
        Assert.Null(await Files(_ => Status(HttpStatusCode.NotFound)).ReadTextAsync(Archive));
        Assert.Null(await Files(_ => throw new InvalidOperationException("nothing may be fetched")).ReadTextAsync(new BoreaReleaseAsset("SHA256SUMS.txt", "https://example.com/SHA256SUMS.txt")));

        // a file above the limit or a missing file is the same at every address
        Assert.Equal([ArchiveUrl, ArchiveUrl], RequestedUrls());
    }

    [Fact]
    public async Task ReadTextAsync_A504OnTheDownloadLink_ReadsTheFileFromItsApiAddress()
    {
        var files = FilesAnswering(() => Status(HttpStatusCode.GatewayTimeout), () => Text("abc  Borea.zip\n"));

        Assert.Equal("abc  Borea.zip\n", await files.ReadTextAsync(Archive));

        Assert.Equal([ArchiveUrl, ApiUrl], RequestedUrls());
        Assert.Contains(_log.Lines, line => line.Contains($"could not be downloaded from {ArchiveUrl}, so Borea asks {ApiUrl}", StringComparison.Ordinal));
        Assert.Contains(_log.Lines, line => line.Contains($"came from {ApiUrl}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadTextAsync_BothAddressesFail_IsNull()
    {
        var files = FilesAnswering(() => Status(HttpStatusCode.GatewayTimeout), () => Status(HttpStatusCode.GatewayTimeout));

        Assert.Null(await files.ReadTextAsync(Archive));

        Assert.Equal([ArchiveUrl, ApiUrl], RequestedUrls());
    }

    [Fact]
    public async Task ReadTextAsync_AnApiAddressAboveTheLimit_IsNull()
    {
        var files = FilesAnswering(() => Status(HttpStatusCode.GatewayTimeout), () => Text(new string('x', BoreaReleaseFiles.MaxTextBytes + 1)));

        Assert.Null(await files.ReadTextAsync(Archive));
    }

    private sealed class CollectingProgress(List<long> received) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => received.Add(value.BytesDownloaded);
    }

    private sealed class ListLog : IBoreaLog
    {
        private readonly List<string> _lines = [];

        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (_lines)
                    return _lines.ToArray();
            }
        }

        public string CurrentFilePath => "borea.log";

        public void Write(string message)
        {
            lock (_lines)
                _lines.Add(message);
        }

        public void Write(string message, Exception exception) => Write($"{message} {exception.Message}");

        public IReadOnlyList<string> ReadRecentLines(int maxLines) => Lines.TakeLast(maxLines).ToList();
    }
}
