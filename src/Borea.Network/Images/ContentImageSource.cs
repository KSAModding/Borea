using Borea.Core.Index;

namespace Borea.Network.Images;

/// <summary>Serves image records from the cache, and fetches missing ones one digest at a time with a handler of its own.</summary>
public sealed class ContentImageSource : IContentImageSource, IDisposable
{
    public const int MaxParallelFetches = 4;

    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(30);

    private readonly IContentImageCache _cache;
    private readonly ContentImageFetcher _fetcher;
    private readonly SemaphoreSlim _fetchSlots = new(MaxParallelFetches, MaxParallelFetches);
    private readonly Dictionary<string, DigestGate> _gates = new(StringComparer.OrdinalIgnoreCase);

    public ContentImageSource(IContentImageCache cache)
        : this(cache, ContentImageFetcher.CreateHandler(), FetchTimeout)
    {
    }

    internal ContentImageSource(IContentImageCache cache, HttpMessageHandler handler, TimeSpan timeout)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _fetcher = new ContentImageFetcher(handler, timeout);
    }

    public async Task<ContentImageResult> GetAsync(ContentImage image, bool loadFromAuthorHosts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (!loadFromAuthorHosts)
        {
            return await ReadCacheAsync(image, cancellationToken).ConfigureAwait(false)
                ?? ContentImageResult.Failed(ContentImageFailure.DisabledByPreference, $"{image.Url} is not cached, and loading images from author hosts is off.");
        }

        using var entered = await EnterAsync(image.Sha256, cancellationToken).ConfigureAwait(false);
        if (entered.Gate.Finished is { } finished && Shared(finished.Image, finished.Result, image) is { } shared)
            return shared;

        if (await ReadCacheAsync(image, cancellationToken).ConfigureAwait(false) is { } cached)
            return cached;

        ContentImageResult fetched;
        await _fetchSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            fetched = await _fetcher.FetchAsync(image, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fetchSlots.Release();
        }

        entered.Gate.Finished = (image, fetched);
        if (fetched.IsLoaded)
            await TryWriteCacheAsync(image.Sha256, fetched.Bytes).ConfigureAwait(false);

        return fetched;
    }

    /// <summary>Loaded bytes serve every record of the digest that they verify against, and a failure only the same record.</summary>
    private static ContentImageResult? Shared(ContentImage fetchedFor, ContentImageResult result, ContentImage image)
    {
        if (result.IsLoaded)
            return Verified(image, result.Bytes.ToArray());

        var sameRecord = fetchedFor.GetType() == image.GetType()
            && string.Equals(fetchedFor.Url, image.Url, StringComparison.Ordinal)
            && fetchedFor.Width == image.Width
            && fetchedFor.Height == image.Height
            && fetchedFor.SizeBytes == image.SizeBytes;
        return sameRecord ? result : null;
    }

    private async Task<ContentImageResult?> ReadCacheAsync(ContentImage image, CancellationToken cancellationToken)
    {
        var bytes = await _cache.ReadAsync(image.Sha256, cancellationToken).ConfigureAwait(false);
        return bytes is null ? null : Verified(image, bytes);
    }

    private static ContentImageResult? Verified(ContentImage image, byte[] bytes)
    {
        var verified = ContentImageBytes.Verify(image, bytes);
        return verified.IsLoaded ? verified : null;
    }

    /// <summary>Ignores the caller's token and disk errors, because the bytes already arrived and a failed write only costs a later fetch.</summary>
    private async Task TryWriteCacheAsync(string sha256, ReadOnlyMemory<byte> bytes)
    {
        try
        {
            await _cache.WriteAsync(sha256, bytes, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task<Entered> EnterAsync(string sha256, CancellationToken cancellationToken)
    {
        DigestGate? gate;
        lock (_gates)
        {
            if (!_gates.TryGetValue(sha256, out gate))
            {
                gate = new DigestGate();
                _gates.Add(sha256, gate);
            }

            gate.Users++;
        }

        try
        {
            await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Leave(sha256, gate);
            throw;
        }

        return new Entered(this, sha256, gate);
    }

    private void Leave(string sha256, DigestGate gate)
    {
        lock (_gates)
        {
            if (--gate.Users == 0)
                _gates.Remove(sha256);
        }
    }

    public void Dispose() => _fetcher.Dispose();

    private sealed class DigestGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int Users { get; set; }

        /// <summary>The last fetch through the gate, which the callers that waited on it share until the gate has no users.</summary>
        public (ContentImage Image, ContentImageResult Result)? Finished { get; set; }
    }

    private sealed class Entered(ContentImageSource source, string sha256, DigestGate gate) : IDisposable
    {
        public DigestGate Gate => gate;

        public void Dispose()
        {
            gate.Semaphore.Release();
            source.Leave(sha256, gate);
        }
    }
}
