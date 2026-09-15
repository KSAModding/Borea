using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Borea.Core.Index;
using Borea.Network.Images;

namespace Borea.Network.Tests.Images;

public sealed class ContentImageSourceTests
{
    private const string IconUrl = "https://images.example/icon.png";

    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(30);

    private static readonly byte[] IconBytes = Png(variant: 0);

    #region Fetching

    [Fact]
    public async Task GetAsync_HostServesTheImage_ReturnsTheBytesAndCachesThem()
    {
        var requested = new List<Uri>();
        var cache = new MemoryImageCache();
        using var source = Source(_ => Image(IconBytes), requested, cache);

        var result = await source.GetAsync(Icon(IconBytes), loadFromAuthorHosts: true);

        Assert.True(result.IsLoaded, result.Reason);
        Assert.Equal(IconBytes, result.Bytes.ToArray());
        Assert.Equal(new[] { new Uri(IconUrl) }, requested);
        Assert.Equal(IconBytes, cache.Entries[Icon(IconBytes).Sha256]);
    }

    [Fact]
    public async Task GetAsync_HostServesOtherBytes_FailsAndCachesNothing()
    {
        var cache = new MemoryImageCache();
        using var source = Source(_ => Image(Png(variant: 1)), new List<Uri>(), cache);

        var result = await source.GetAsync(Icon(IconBytes), loadFromAuthorHosts: true);

        Assert.Equal(ContentImageFailure.FactsMismatch, result.Failure);
        Assert.Empty(cache.Entries);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ContentImageFailure.NotFound)]
    [InlineData(HttpStatusCode.Gone, ContentImageFailure.NotFound)]
    [InlineData(HttpStatusCode.Forbidden, ContentImageFailure.NotFound)]
    [InlineData(HttpStatusCode.RequestTimeout, ContentImageFailure.Unavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, ContentImageFailure.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ContentImageFailure.Unavailable)]
    public async Task GetAsync_HostAnswersWithAnError_ReportsTheFailure(HttpStatusCode status, ContentImageFailure failure)
    {
        using var source = Source(_ => new HttpResponseMessage(status), new List<Uri>());

        var result = await source.GetAsync(Icon(IconBytes), loadFromAuthorHosts: true);

        Assert.Equal(failure, result.Failure);
    }

    [Fact]
    public async Task GetAsync_HostStalls_IsUnavailableAfterTheTimeout()
    {
        using var source = Source(
            async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The delay cannot end on its own.");
            },
            timeout: TimeSpan.FromMilliseconds(200));

        var result = await source.GetAsync(Icon(IconBytes), loadFromAuthorHosts: true);

        Assert.Equal(ContentImageFailure.Unavailable, result.Failure);
    }

    [Fact]
    public async Task GetAsync_CompressedBody_IsNotTheImage()
    {
        var response = Image(IconBytes);
        response.Content.Headers.ContentEncoding.Add("gzip");
        using var source = Source(_ => response, new List<Uri>());

        var result = await source.GetAsync(Icon(IconBytes), loadFromAuthorHosts: true);

        Assert.Equal(ContentImageFailure.UnsupportedFormat, result.Failure);
    }

    [Fact]
    public async Task GetAsync_UrlWithCredentials_IsNotRequested()
    {
        var requested = new List<Uri>();
        using var source = Source(_ => Image(IconBytes), requested);

        var result = await source.GetAsync(Icon(IconBytes, "https://user:secret@images.example/icon.png"), loadFromAuthorHosts: true);

        Assert.Equal(ContentImageFailure.BlockedNetworkTarget, result.Failure);
        Assert.Empty(requested);
    }

    [Fact]
    public async Task GetAsync_CacheCannotWrite_StillServesTheImage()
    {
        using var source = Source(_ => Image(IconBytes), new List<Uri>(), new UnwritableImageCache());

        var result = await source.GetAsync(Icon(IconBytes), loadFromAuthorHosts: true);

        Assert.True(result.IsLoaded, result.Reason);
    }

    #endregion

    #region Redirects

    [Fact]
    public async Task GetAsync_ThreeRedirects_FollowsThemToTheImage()
    {
        var requested = new List<Uri>();
        using var source = Source(
            url => url.AbsoluteUri switch
            {
                IconUrl => Redirect("https://mirror.example/a", HttpStatusCode.MovedPermanently),
                "https://mirror.example/a" => Redirect("/b", HttpStatusCode.TemporaryRedirect),
                "https://mirror.example/b" => Redirect("https://cdn.example/icon.png", HttpStatusCode.PermanentRedirect),
                _ => Image(IconBytes),
            },
            requested);

        var result = await source.GetAsync(Icon(IconBytes), loadFromAuthorHosts: true);

        Assert.True(result.IsLoaded, result.Reason);
        Assert.Equal(
            new[] { IconUrl, "https://mirror.example/a", "https://mirror.example/b", "https://cdn.example/icon.png" },
            requested.Select(url => url.AbsoluteUri));
    }

    [Fact]
    public async Task GetAsync_RedirectToHttp_IsNotAllowedAndNotFollowed()
    {
        var requested = new List<Uri>();
        using var source = Source(
            url => url.Scheme == Uri.UriSchemeHttp ? Image(IconBytes) : Redirect("http://images.example/icon.png"),
            requested);

        var result = await source.GetAsync(Icon(IconBytes), loadFromAuthorHosts: true);

        Assert.Equal(ContentImageFailure.RedirectNotAllowed, result.Failure);
        Assert.Equal(new[] { new Uri(IconUrl) }, requested);
    }

    [Fact]
    public async Task GetAsync_FourthRedirect_IsNotAllowed()
    {
        var requested = new List<Uri>();
        using var source = Source(_ => Redirect($"/hop-{requested.Count}"), requested);

        var result = await source.GetAsync(Icon(IconBytes), loadFromAuthorHosts: true);

        Assert.Equal(ContentImageFailure.RedirectNotAllowed, result.Failure);
        Assert.Equal(ContentImageFetcher.MaxRedirects + 1, requested.Count);
    }

    [Fact]
    public async Task GetAsync_RedirectWithoutLocation_IsNotAllowed()
    {
        using var source = Source(_ => new HttpResponseMessage(HttpStatusCode.Found), new List<Uri>());

        var result = await source.GetAsync(Icon(IconBytes), loadFromAuthorHosts: true);

        Assert.Equal(ContentImageFailure.RedirectNotAllowed, result.Failure);
    }

    #endregion

    #region Byte cap

    [Fact]
    public async Task GetAsync_BodyAboveTheCap_StopsReadingWhileItStreams()
    {
        var body = new EndlessStream();
        using var source = Source(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new LengthlessContent(body) }, new List<Uri>());

        var result = await source.GetAsync(Icon(IconBytes), loadFromAuthorHosts: true);

        Assert.Equal(ContentImageFailure.TooLarge, result.Failure);
        Assert.InRange(body.Served, IconImage.MaxBytes + 1, 2 * IconImage.MaxBytes);
    }

    [Fact]
    public async Task GetAsync_AnnouncedLengthAboveTheCap_IsTooLargeBeforeReading()
    {
        var body = new EndlessStream();
        var content = new LengthlessContent(body);
        content.Headers.ContentLength = IconImage.MaxBytes + 1;
        using var source = Source(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content }, new List<Uri>());

        var result = await source.GetAsync(Icon(IconBytes), loadFromAuthorHosts: true);

        Assert.Equal(ContentImageFailure.TooLarge, result.Failure);
        Assert.Equal(0, body.Served);
    }

    #endregion

    #region Cache and preference

    [Fact]
    public async Task GetAsync_CachedImage_MakesNoRequest()
    {
        var requested = new List<Uri>();
        var cache = new MemoryImageCache();
        cache.Entries[Icon(IconBytes).Sha256] = IconBytes;
        using var source = Source(_ => Image(IconBytes), requested, cache);

        var result = await source.GetAsync(Icon(IconBytes), loadFromAuthorHosts: true);

        Assert.True(result.IsLoaded, result.Reason);
        Assert.Empty(requested);
    }

    [Fact]
    public async Task GetAsync_CachedBytesDoNotMatchTheRecord_FetchesTheImageAgain()
    {
        var requested = new List<Uri>();
        var cache = new MemoryImageCache();
        cache.Entries[Icon(IconBytes).Sha256] = Png(variant: 1);
        using var source = Source(_ => Image(IconBytes), requested, cache);

        var result = await source.GetAsync(Icon(IconBytes), loadFromAuthorHosts: true);

        Assert.True(result.IsLoaded, result.Reason);
        Assert.Single(requested);
        Assert.Equal(IconBytes, cache.Entries[Icon(IconBytes).Sha256]);
    }

    [Fact]
    public async Task GetAsync_LoadingFromAuthorHostsOff_MakesNoRequestForAnUncachedImage()
    {
        var requested = new List<Uri>();
        using var source = Source(_ => Image(IconBytes), requested);

        var result = await source.GetAsync(Icon(IconBytes), loadFromAuthorHosts: false);

        Assert.Equal(ContentImageFailure.DisabledByPreference, result.Failure);
        Assert.Empty(requested);
    }

    [Fact]
    public async Task GetAsync_LoadingFromAuthorHostsOff_ServesACachedImage()
    {
        var requested = new List<Uri>();
        var cache = new MemoryImageCache();
        cache.Entries[Icon(IconBytes).Sha256] = IconBytes;
        using var source = Source(_ => Image(IconBytes), requested, cache);

        var result = await source.GetAsync(Icon(IconBytes), loadFromAuthorHosts: false);

        Assert.True(result.IsLoaded, result.Reason);
        Assert.Empty(requested);
    }

    #endregion

    #region Concurrency

    [Fact]
    public async Task GetAsync_TwoRequestsForOneDigest_MakeOneRequest()
    {
        var requests = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var source = Source(async (_, cancellationToken) =>
        {
            Interlocked.Increment(ref requests);
            await release.Task.WaitAsync(cancellationToken);
            return Image(IconBytes);
        });

        var first = source.GetAsync(Icon(IconBytes), loadFromAuthorHosts: true);
        var second = source.GetAsync(Icon(IconBytes), loadFromAuthorHosts: true);
        release.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, requests);
        Assert.All(results, result => Assert.True(result.IsLoaded, result.Reason));
    }

    [Fact]
    public async Task GetAsync_TwoRequestsForOneRecordThatFails_MakeOneRequest()
    {
        var requests = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var source = Source(async (_, cancellationToken) =>
        {
            Interlocked.Increment(ref requests);
            await release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });

        var first = source.GetAsync(Icon(IconBytes), loadFromAuthorHosts: true);
        var second = source.GetAsync(Icon(IconBytes), loadFromAuthorHosts: true);
        release.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, requests);
        Assert.All(results, result => Assert.Equal(ContentImageFailure.Unavailable, result.Failure));
    }

    [Fact]
    public async Task GetAsync_OtherUrlWaitsOnADigestThatFails_IsFetchedItself()
    {
        var requested = new List<Uri>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var source = Source(async (request, cancellationToken) =>
        {
            lock (requested)
                requested.Add(request.RequestUri!);

            await release.Task.WaitAsync(cancellationToken);
            return request.RequestUri!.AbsoluteUri == IconUrl ? new HttpResponseMessage(HttpStatusCode.NotFound) : Image(IconBytes);
        });

        var dead = source.GetAsync(Icon(IconBytes), loadFromAuthorHosts: true);
        var alive = source.GetAsync(Icon(IconBytes, "https://mirror.example/icon.png"), loadFromAuthorHosts: true);
        release.SetResult();

        Assert.Equal(ContentImageFailure.NotFound, (await dead).Failure);
        Assert.True((await alive).IsLoaded);
        Assert.Equal(2, requested.Count);
    }

    [Fact]
    public async Task GetAsync_ManyImages_FetchesAtMostTheParallelLimitAtOnce()
    {
        var running = 0;
        var most = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var images = Enumerable.Range(0, ContentImageSource.MaxParallelFetches + 2)
            .Select(index => Png(variant: (byte)index))
            .ToArray();
        using var source = Source(async (request, cancellationToken) =>
        {
            var now = Interlocked.Increment(ref running);
            lock (images)
                most = Math.Max(most, now);

            await release.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref running);
            return Image(images[int.Parse(request.RequestUri!.Segments[^1])]);
        });

        var loads = images
            .Select((bytes, index) => source.GetAsync(Icon(bytes, $"https://images.example/{index}"), loadFromAuthorHosts: true))
            .ToArray();
        await WaitUntilAsync(() => Volatile.Read(ref running) == ContentImageSource.MaxParallelFetches);
        await Task.Delay(100);
        release.SetResult();
        var results = await Task.WhenAll(loads);

        Assert.Equal(ContentImageSource.MaxParallelFetches, most);
        Assert.All(results, result => Assert.True(result.IsLoaded, result.Reason));
    }

    #endregion

    #region Handler

    [Theory]
    [InlineData("https://127.0.0.1/icon.png")]
    [InlineData("https://[::1]/icon.png")]
    [InlineData("https://10.0.0.1/icon.png")]
    [InlineData("https://169.254.169.254/icon.png")]
    public async Task GetAsync_HostIsNotPublic_IsBlocked(string url)
    {
        using var source = new ContentImageSource(new MemoryImageCache());

        var result = await source.GetAsync(Icon(IconBytes, url), loadFromAuthorHosts: true);

        Assert.Equal(ContentImageFailure.BlockedNetworkTarget, result.Failure);
    }

    [Fact]
    public void CreateHandler_FollowsNoRedirectAndSendsNoCookiesOrCredentials()
    {
        using var handler = ContentImageFetcher.CreateHandler();

        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.Null(handler.Credentials);
        Assert.False(handler.PreAuthenticate);
        Assert.False(handler.UseProxy);
        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.Equal(0, handler.MaxResponseDrainSize);
        Assert.NotNull(handler.ConnectCallback);
    }

    #endregion

    private static ContentImageSource Source(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        IContentImageCache? cache = null,
        TimeSpan? timeout = null) =>
        new(cache ?? new MemoryImageCache(), new FakeHttpMessageHandler(respond), timeout ?? FetchTimeout);

    /// <summary>A source whose host answers by URL and records every request in order.</summary>
    private static ContentImageSource Source(Func<Uri, HttpResponseMessage> respond, List<Uri> requested, IContentImageCache? cache = null) =>
        Source(
            (request, _) =>
            {
                requested.Add(request.RequestUri!);
                return Task.FromResult(respond(request.RequestUri!));
            },
            cache);

    private static IconImage Icon(byte[] bytes, string url = IconUrl) =>
        new(url, Convert.ToHexString(SHA256.HashData(bytes)), 256, 256, bytes.Length);

    private static HttpResponseMessage Image(byte[] bytes) => FakeHttpMessageHandler.ByteResponse(bytes);

    private static HttpResponseMessage Redirect(string location, HttpStatusCode status = HttpStatusCode.Found)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    /// <summary>A 256 by 256 PNG whose bytes, and so its digest, change with <paramref name="variant"/>. No chunk CRC is read, so it stays zero.</summary>
    private static byte[] Png(byte variant)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, 256);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 256);
        header[8] = 8;
        header[9] = 6;

        List<byte> bytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        foreach (var (kind, body) in new[] { ("IHDR", header), ("IDAT", new byte[] { 0x78, 0x9C, variant }), ("IEND", Array.Empty<byte>()) })
        {
            var length = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(length, (uint)body.Length);
            bytes.AddRange(length);
            bytes.AddRange(Encoding.ASCII.GetBytes(kind));
            bytes.AddRange(body);
            bytes.AddRange(new byte[4]);
        }

        return [.. bytes];
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The condition did not become true in time.");

            await Task.Delay(10);
        }
    }

    private sealed class MemoryImageCache : IContentImageCache
    {
        public Dictionary<string, byte[]> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<byte[]?> ReadAsync(string sha256, CancellationToken cancellationToken = default)
        {
            lock (Entries)
                return Task.FromResult(Entries.TryGetValue(sha256, out var bytes) ? bytes : null);
        }

        public Task WriteAsync(string sha256, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
        {
            lock (Entries)
                Entries[sha256] = bytes.ToArray();

            return Task.CompletedTask;
        }
    }

    private sealed class UnwritableImageCache : IContentImageCache
    {
        public Task<byte[]?> ReadAsync(string sha256, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);

        public Task WriteAsync(string sha256, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default) =>
            throw new IOException("The disk is full.");
    }

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

    private sealed class EndlessStream : Stream
    {
        public long Served { get; private set; }

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
            buffer.Fill(0xAB);
            Served += buffer.Length;
            return buffer.Length;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(Read(buffer.Span));

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
