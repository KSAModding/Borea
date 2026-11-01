using System.Net;
using System.Security.Cryptography;
using Borea.Core.Dependencies;
using Borea.Core.Mods;
using Borea.Core.Sharing;
using Borea.Network.Downloads;

namespace Borea.Network.Tests.Downloads;

public sealed class ShareFileDownloaderTests : IDisposable
{
    private const string Url = "https://files.example/share/files/vehicle/Rover/1.0.0";
    private const string Bucket = "https://cdn.example/archives/rover.zip";

    private static readonly byte[] Archive = "the archive bytes"u8.ToArray();

    private static readonly ShareHost Host = new("files", "files.example", new Uri("https://files.example/api/"), ["vehicle"], new Dictionary<string, long>())
    {
        Data = new Uri("https://data.example/"),
        Downloads = [new Uri("https://files.example/share/files/"), new Uri("https://cdn.example/archives/")],
    };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly string _path;

    public ShareFileDownloaderTests()
    {
        Directory.CreateDirectory(_root);
        _path = Path.Combine(_root, "archive.part");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static ModVersionMetadata Release(string url = Url, string? sha256 = null, long? size = null) => new(
        specVersion: 1,
        modId: "Rover",
        version: ModVersion.Parse("1.0.0"),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: DateTimeOffset.UnixEpoch,
        gameMin: "2026.7.4.2131",
        gameMinRevision: 2131,
        download: new DownloadInfo(url, sha256 ?? Convert.ToHexString(SHA256.HashData(Archive)), size ?? Archive.Length, "application/zip", ["https://archive.example/rover.zip"]),
        installSizeBytes: null,
        dependencies: Array.Empty<ModDependency>());

    private static HttpResponseMessage Serve(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private static HttpResponseMessage RedirectTo(string url) => new(HttpStatusCode.Found) { Headers = { Location = new Uri(url) } };

    [Fact]
    public async Task AGoodUrl_GetsOnePlainGetWithoutARange_AndKeepsTheVerifiedBytes()
    {
        var host = new Answers(_ => Serve(Archive));

        var result = await new ShareFileDownloader(new HttpClient(host), Host).DownloadAsync(Release(), _path);

        var request = Assert.Single(host.Requests);
        Assert.Equal((HttpMethod.Get, Url), (request.Method, request.Url));
        Assert.False(request.HasRange);
        Assert.Equal(Archive, await File.ReadAllBytesAsync(_path));
        Assert.Equal((Url, (long)Archive.Length), (result.Url, result.BytesDownloaded));
    }

    [Fact]
    public async Task ARedirectUnderThePrefixes_IsFollowed()
    {
        var host = new Answers(request => request.AbsoluteUri == Url ? RedirectTo(Bucket) : Serve(Archive));

        var result = await new ShareFileDownloader(new HttpClient(host), Host).DownloadAsync(Release(), _path);

        Assert.Equal([Url, Bucket], host.Requests.Select(request => request.Url));
        Assert.Equal(Bucket, result.Url);
    }

    [Fact]
    public async Task ARedirectAwayFromThePrefixes_IsNotFollowed_AndNoMirrorIsAsked()
    {
        var host = new Answers(_ => RedirectTo("https://other.example/rover.zip"));

        await Assert.ThrowsAsync<DownloadFailedException>(() => new ShareFileDownloader(new HttpClient(host), Host).DownloadAsync(Release(), _path));

        Assert.Equal([Url], host.Requests.Select(request => request.Url));
    }

    [Fact]
    public async Task AUrlOutsideThePrefixes_SendsNoRequest()
    {
        var host = new Answers(_ => Serve(Archive));

        await Assert.ThrowsAsync<DownloadFailedException>(() => new ShareFileDownloader(new HttpClient(host), Host).DownloadAsync(Release("https://other.example/rover.zip"), _path));

        Assert.Empty(host.Requests);
    }

    [Fact]
    public async Task MoreBytesThanTheStatedSize_FailAndLeaveNoFile()
    {
        var host = new Answers(_ =>
        {
            var answer = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Archive) };
            answer.Content.Headers.ContentLength = null;
            return answer;
        });

        var failure = await Assert.ThrowsAsync<DownloadFailedException>(() => new ShareFileDownloader(new HttpClient(host), Host).DownloadAsync(Release(size: Archive.Length - 1), _path));

        Assert.Contains("sends more", failure.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public async Task OtherBytes_FailAndLeaveNoFile()
    {
        var host = new Answers(_ => Serve("not the archive!!"u8.ToArray()));

        await Assert.ThrowsAsync<DownloadFailedException>(() => new ShareFileDownloader(new HttpClient(host), Host).DownloadAsync(Release(), _path));

        Assert.False(File.Exists(_path));
    }

    [Fact]
    public async Task AnErrorAnswer_Fails()
    {
        var host = new Answers(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<DownloadFailedException>(() => new ShareFileDownloader(new HttpClient(host), Host).DownloadAsync(Release(), _path));
    }

    private sealed class Answers(Func<Uri, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, bool HasRange)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsoluteUri, request.Headers.Range is not null));
            return Task.FromResult(answer(request.RequestUri));
        }
    }
}
