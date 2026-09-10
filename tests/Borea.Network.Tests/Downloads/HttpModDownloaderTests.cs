using System.Net;
using System.Security.Cryptography;
using Borea.Core.Dependencies;
using Borea.Core.Mods;
using Borea.Network.Downloads;

namespace Borea.Network.Tests.Downloads;

public sealed class HttpModDownloaderTests : IDisposable
{
    private const string GitHubUrl = "https://github.com/owner/repo/releases/download/v1.0.0/ModA.zip";
    private const string SpaceDockUrl = "https://spacedock.info/mod/1/ModA/download/1.0.0";
    private const string MirrorUrl = "https://mirror.example/ModA.zip";

    private static readonly byte[] Archive = "the archive bytes"u8.ToArray();

    // Same length as the archive, so a length check alone would let it through.
    private static readonly byte[] OtherBytes = "not the archive!!"u8.ToArray();

    private readonly string _tempRoot;
    private readonly string _archivePath;

    public HttpModDownloaderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempRoot);
        _archivePath = Path.Combine(_tempRoot, "archive.zip");
    }

    private static string Sha256Of(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static ModVersionMetadata Release(string url, string? sha256, long? sizeBytes, params string[] mirrors) => new(
        specVersion: 1,
        modId: "ModA",
        version: ModVersion.Parse("1.0.0"),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        gameMin: "2026.7.4.2131",
        gameMinRevision: 2131,
        download: new DownloadInfo(url, sha256, sizeBytes, "application/zip", mirrors),
        installSizeBytes: null,
        dependencies: Array.Empty<ModDependency>());

    /// <summary>A release stamped the way the index stamps it: hash and size known.</summary>
    private static ModVersionMetadata StampedRelease(string url = GitHubUrl, params string[] mirrors) =>
        Release(url, Sha256Of(Archive), Archive.Length, mirrors);

    /// <summary>A client that answers by URL and records every request in order.</summary>
    private static HttpClient Client(Func<string, HttpResponseMessage> respond, List<string> requested) =>
        FakeHttpMessageHandler.BuildClient(request =>
        {
            var url = request.RequestUri!.ToString();
            requested.Add(url);
            return respond(url);
        }, out _);

    private static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    private static HttpResponseMessage Body(HttpContent content) => new(HttpStatusCode.OK) { Content = content };

    /// <summary>Reports on the calling thread, so a test can read the list right after the await.</summary>
    private sealed class SyncProgress : IProgress<DownloadProgress>
    {
        public List<DownloadProgress> Reports { get; } = new();

        public void Report(DownloadProgress value) => Reports.Add(value);
    }

    /// <summary>A body served without a Content-Length header.</summary>
    private sealed class LengthlessContent(Stream body) : HttpContent
    {
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(body);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => body.CopyToAsync(stream);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    /// <summary>
    /// A body that hands out one chunk and then cancels the caller's token, the
    /// way a user stops a transfer part-way through.
    /// </summary>
    private sealed class CancelAfterFirstReadStream(byte[] firstChunk, CancellationTokenSource cancellation) : Stream
    {
        private bool _handedOut;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_handedOut)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }

            _handedOut = true;
            firstChunk.CopyTo(buffer);
            return firstChunk.Length;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(Read(buffer.Span));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Read(buffer, offset, count));

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    #region Sources

    [Theory]
    [InlineData(GitHubUrl)]
    [InlineData(SpaceDockUrl)]
    public async Task DownloadAsync_AnyHost_WritesTheArchiveAndVerifiesIt(string url)
    {
        var requested = new List<string>();
        var downloader = new HttpModDownloader(Client(_ => FakeHttpMessageHandler.ByteResponse(Archive), requested));

        var result = await downloader.DownloadAsync(StampedRelease(url), _archivePath);

        Assert.Equal(new[] { url }, requested);
        Assert.Equal(Archive, await File.ReadAllBytesAsync(_archivePath));
        Assert.Equal(url, result.Url);
        Assert.Equal(Archive.Length, result.BytesDownloaded);
        Assert.Equal(Sha256Of(Archive), result.Sha256);
    }

    [Fact]
    public async Task DownloadAsync_PrimaryDown_MirrorServesTheArchive()
    {
        var requested = new List<string>();
        var downloader = new HttpModDownloader(Client(
            url => url == MirrorUrl ? FakeHttpMessageHandler.ByteResponse(Archive) : Status(HttpStatusCode.NotFound),
            requested));

        var result = await downloader.DownloadAsync(StampedRelease(GitHubUrl, MirrorUrl), _archivePath);

        Assert.Equal(new[] { GitHubUrl, MirrorUrl }, requested);
        Assert.Equal(MirrorUrl, result.Url);
        Assert.Equal(Archive, await File.ReadAllBytesAsync(_archivePath));
    }

    [Fact]
    public async Task DownloadAsync_PrimaryStalls_MirrorServesTheArchive()
    {
        var requested = new List<string>();
        var client = FakeHttpMessageHandler.BuildClient(async (request, cancellationToken) =>
        {
            var url = request.RequestUri!.ToString();
            requested.Add(url);
            if (url == MirrorUrl)
                return FakeHttpMessageHandler.ByteResponse(Archive);

            // A host that accepts the connection and never answers.
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The delay cannot end on its own.");
        }, out _);
        client.Timeout = TimeSpan.FromMilliseconds(200);
        var downloader = new HttpModDownloader(client);

        var result = await downloader.DownloadAsync(StampedRelease(GitHubUrl, MirrorUrl), _archivePath);

        Assert.Equal(new[] { GitHubUrl, MirrorUrl }, requested);
        Assert.Equal(MirrorUrl, result.Url);
        Assert.Equal(Archive, await File.ReadAllBytesAsync(_archivePath));
    }

    [Fact]
    public async Task DownloadAsync_EverySourceStalls_ReportsAFailureAndNotACancellation()
    {
        var client = FakeHttpMessageHandler.BuildClient(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The delay cannot end on its own.");
        }, out _);
        client.Timeout = TimeSpan.FromMilliseconds(200);
        var downloader = new HttpModDownloader(client);

        var ex = await Assert.ThrowsAsync<DownloadFailedException>(
            () => downloader.DownloadAsync(StampedRelease(GitHubUrl, MirrorUrl), _archivePath));

        Assert.Contains(GitHubUrl, ex.Message);
        Assert.Contains(MirrorUrl, ex.Message);
        Assert.IsAssignableFrom<OperationCanceledException>(ex.InnerException);
        Assert.False(File.Exists(_archivePath));
    }

    [Fact]
    public async Task DownloadAsync_PrimaryServesOtherBytes_MirrorServesTheArchive()
    {
        var requested = new List<string>();
        var downloader = new HttpModDownloader(Client(
            url => FakeHttpMessageHandler.ByteResponse(url == MirrorUrl ? Archive : OtherBytes),
            requested));

        var result = await downloader.DownloadAsync(StampedRelease(GitHubUrl, MirrorUrl), _archivePath);

        Assert.Equal(new[] { GitHubUrl, MirrorUrl }, requested);
        Assert.Equal(MirrorUrl, result.Url);
        Assert.Equal(Archive, await File.ReadAllBytesAsync(_archivePath));
    }

    [Fact]
    public async Task DownloadAsync_EverySourceFails_NamesEachOne()
    {
        var downloader = new HttpModDownloader(Client(
            url => Status(url == GitHubUrl ? HttpStatusCode.InternalServerError : HttpStatusCode.NotFound),
            new()));

        var ex = await Assert.ThrowsAsync<DownloadFailedException>(
            () => downloader.DownloadAsync(StampedRelease(GitHubUrl, MirrorUrl), _archivePath));

        Assert.Contains(GitHubUrl, ex.Message);
        Assert.Contains(MirrorUrl, ex.Message);
        Assert.IsType<HttpRequestException>(ex.InnerException);
        Assert.False(File.Exists(_archivePath));
    }

    [Theory]
    [InlineData("mod/1/ModA/download/1.0.0")]
    [InlineData("file:///C:/mods/ModA.zip")]
    [InlineData("ftp://files.example/ModA.zip")]
    public async Task DownloadAsync_NotAnHttpUrl_CountsAsAFailedSource(string url)
    {
        var requested = new List<string>();
        var downloader = new HttpModDownloader(Client(_ => FakeHttpMessageHandler.ByteResponse(Archive), requested));
        var release = Release(url, Sha256Of(Archive), Archive.Length, MirrorUrl);

        var result = await downloader.DownloadAsync(release, _archivePath);

        Assert.Equal(new[] { MirrorUrl }, requested);
        Assert.Equal(MirrorUrl, result.Url);
    }

    #endregion

    #region Verification

    [Fact]
    public async Task DownloadAsync_HashMismatch_FailsAndLeavesNoFile()
    {
        var downloader = new HttpModDownloader(Client(_ => FakeHttpMessageHandler.ByteResponse(OtherBytes), new()));

        var ex = await Assert.ThrowsAsync<DownloadFailedException>(() => downloader.DownloadAsync(StampedRelease(), _archivePath));

        Assert.Contains("SHA-256", ex.Message);
        Assert.Contains(GitHubUrl, ex.Message);
        Assert.Null(ex.InnerException);
        Assert.False(File.Exists(_archivePath));
    }

    [Fact]
    public async Task DownloadAsync_StatedSizeWrongButHashMatches_Accepts()
    {
        // Any source whose bytes match the hash is acceptable (RFC 0031).
        var downloader = new HttpModDownloader(Client(_ => FakeHttpMessageHandler.ByteResponse(Archive), new()));
        var release = Release(GitHubUrl, Sha256Of(Archive), Archive.Length + 5);

        var result = await downloader.DownloadAsync(release, _archivePath);

        Assert.Equal(Sha256Of(Archive), result.Sha256);
        Assert.Equal(Archive, await File.ReadAllBytesAsync(_archivePath));
    }

    [Fact]
    public async Task DownloadAsync_NoHash_AcceptsTheBytesAsReceived()
    {
        var downloader = new HttpModDownloader(Client(_ => FakeHttpMessageHandler.ByteResponse(Archive), new()));

        var result = await downloader.DownloadAsync(Release(SpaceDockUrl, sha256: null, sizeBytes: null), _archivePath);

        Assert.Equal(Sha256Of(Archive), result.Sha256);
        Assert.Equal(Archive, await File.ReadAllBytesAsync(_archivePath));
    }

    [Fact]
    public async Task DownloadAsync_NoHash_RejectsAnAnnouncedLengthThatDisagrees()
    {
        var downloader = new HttpModDownloader(Client(_ => FakeHttpMessageHandler.ByteResponse(Archive), new()));
        var release = Release(SpaceDockUrl, sha256: null, sizeBytes: Archive.Length + 1);

        var ex = await Assert.ThrowsAsync<DownloadFailedException>(() => downloader.DownloadAsync(release, _archivePath));

        Assert.Contains("announces", ex.Message);
        Assert.False(File.Exists(_archivePath));
    }

    [Fact]
    public async Task DownloadAsync_NoHash_StopsReadingPastTheStatedSize()
    {
        var downloader = new HttpModDownloader(Client(
            _ => Body(new LengthlessContent(new MemoryStream(Archive))), new()));
        var release = Release(SpaceDockUrl, sha256: null, sizeBytes: 5);

        var ex = await Assert.ThrowsAsync<DownloadFailedException>(() => downloader.DownloadAsync(release, _archivePath));

        Assert.Contains("more than the 5 bytes", ex.Message);
        Assert.False(File.Exists(_archivePath));
    }

    [Fact]
    public async Task DownloadAsync_NoHash_RejectsAShorterBody()
    {
        var downloader = new HttpModDownloader(Client(
            _ => Body(new LengthlessContent(new MemoryStream(Archive))), new()));
        var release = Release(SpaceDockUrl, sha256: null, sizeBytes: Archive.Length + 1);

        var ex = await Assert.ThrowsAsync<DownloadFailedException>(() => downloader.DownloadAsync(release, _archivePath));

        Assert.Contains($"received {Archive.Length} bytes", ex.Message);
        Assert.False(File.Exists(_archivePath));
    }

    #endregion

    #region Progress and cancellation

    [Fact]
    public async Task DownloadAsync_ReportsProgressUpToTheFullLength()
    {
        var downloader = new HttpModDownloader(Client(_ => FakeHttpMessageHandler.ByteResponse(Archive), new()));
        var progress = new SyncProgress();

        await downloader.DownloadAsync(StampedRelease(), _archivePath, progress);

        var last = Assert.Single(progress.Reports.TakeLast(1));
        Assert.Equal(Archive.Length, last.BytesDownloaded);
        Assert.Equal(Archive.Length, last.TotalBytes);
        Assert.Equal(100, last.PercentComplete);
    }

    [Fact]
    public async Task DownloadAsync_NoContentLength_ReportsTheStatedSizeAsTheTotal()
    {
        var downloader = new HttpModDownloader(Client(
            _ => Body(new LengthlessContent(new MemoryStream(Archive))), new()));
        var progress = new SyncProgress();

        await downloader.DownloadAsync(StampedRelease(), _archivePath, progress);

        Assert.All(progress.Reports, report => Assert.Equal(Archive.Length, report.TotalBytes));
    }

    [Fact]
    public async Task DownloadAsync_NoContentLengthAndNoStatedSize_ReportsAnUnknownTotal()
    {
        var downloader = new HttpModDownloader(Client(
            _ => Body(new LengthlessContent(new MemoryStream(Archive))), new()));
        var progress = new SyncProgress();

        await downloader.DownloadAsync(Release(GitHubUrl, Sha256Of(Archive), sizeBytes: null), _archivePath, progress);

        var last = Assert.Single(progress.Reports.TakeLast(1));
        Assert.Equal(-1, last.TotalBytes);
        Assert.Equal(0, last.PercentComplete);
    }

    [Fact]
    public async Task DownloadAsync_CancelledBeforeAnyRequest_AsksNoSource()
    {
        var requested = new List<string>();
        var downloader = new HttpModDownloader(Client(_ => FakeHttpMessageHandler.ByteResponse(Archive), requested));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => downloader.DownloadAsync(StampedRelease(GitHubUrl, MirrorUrl), _archivePath, cancellationToken: cancellation.Token));

        Assert.Empty(requested);
        Assert.False(File.Exists(_archivePath));
    }

    [Fact]
    public async Task DownloadAsync_CancelledDuringTheBody_LeavesNoFileAndAsksNoMirror()
    {
        var requested = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var downloader = new HttpModDownloader(Client(
            _ => Body(new LengthlessContent(new CancelAfterFirstReadStream(Archive, cancellation))), requested));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => downloader.DownloadAsync(StampedRelease(GitHubUrl, MirrorUrl), _archivePath, cancellationToken: cancellation.Token));

        Assert.Equal(new[] { GitHubUrl }, requested);
        Assert.False(File.Exists(_archivePath));
    }

    #endregion

    #region Arguments

    [Fact]
    public async Task DownloadAsync_NullRelease_ThrowsArgumentNullException()
    {
        var downloader = new HttpModDownloader(Client(_ => FakeHttpMessageHandler.ByteResponse(Archive), new()));

        await Assert.ThrowsAsync<ArgumentNullException>(() => downloader.DownloadAsync(null!, _archivePath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DownloadAsync_InvalidArchivePath_ThrowsArgumentException(string? archivePath)
    {
        var downloader = new HttpModDownloader(Client(_ => FakeHttpMessageHandler.ByteResponse(Archive), new()));

        await Assert.ThrowsAsync<ArgumentException>(() => downloader.DownloadAsync(StampedRelease(), archivePath!));
    }

    [Fact]
    public void Constructor_NullClient_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new HttpModDownloader(null!));
    }

    #endregion

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
