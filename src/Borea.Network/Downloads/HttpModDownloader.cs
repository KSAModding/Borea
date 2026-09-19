using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Borea.Core.Mods;

namespace Borea.Network.Downloads;

/// <summary>
/// <see cref="IModDownloader"/> over plain HTTP. The release file already holds
/// everything host-specific, so this asks no host API for anything: it streams
/// the archive from the URL into the file, hashes it on the way, and moves on
/// to the next mirror when a source fails or serves other bytes. After a pause
/// it asks the same source for the rest of the file.
/// </summary>
public sealed class HttpModDownloader : IModDownloader
{
    private const int BufferSize = 81920;
    private static readonly TimeSpan DefaultBodyInactivityTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _bodyInactivityTimeout;

    public HttpModDownloader(HttpClient httpClient, TimeSpan? bodyInactivityTimeout = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _bodyInactivityTimeout = bodyInactivityTimeout ?? DefaultBodyInactivityTimeout;

        if (_bodyInactivityTimeout <= TimeSpan.Zero || _bodyInactivityTimeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(bodyInactivityTimeout), "Body inactivity timeout must be a positive finite timer interval.");
    }

    public async Task<DownloadResult> DownloadAsync(
        ModVersionMetadata release,
        string archivePath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        if (string.IsNullOrWhiteSpace(archivePath))
            throw new ArgumentException("Archive path cannot be null or whitespace.", nameof(archivePath));

        var download = release.Download;
        var sources = new List<string>(1 + download.Mirrors.Count) { download.Url };
        sources.AddRange(download.Mirrors);

        var pause = DownloadPause.Current;
        var failures = new List<string>();
        Exception? lastTransportError = null;

        // Each source writes over the same file, so only the exit that returns
        // nothing has to clear it.
        try
        {
            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Uri.TryCreate(source, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
                {
                    failures.Add($"{source}: not an http or https URL");
                    continue;
                }

                Fetched fetched;
                try
                {
                    fetched = await FetchAsync(url, archivePath, download, progress, pause, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsSourceFailure(ex, cancellationToken))
                {
                    failures.Add($"{source}: {ex.Message}");
                    lastTransportError = ex;
                    continue;
                }

                if (fetched.Rejection is null)
                    return new DownloadResult(source, fetched.Bytes, fetched.Sha256);

                failures.Add($"{source}: {fetched.Rejection}");
            }

            throw new DownloadFailedException(
                $"No source served the archive of '{release.ModId}' {release.Version}: {string.Join("; ", failures)}.",
                lastTransportError);
        }
        catch
        {
            if (File.Exists(archivePath))
                File.Delete(archivePath);

            throw;
        }
    }

    /// <summary>
    /// A failure of one source, which says nothing about the next: the host is
    /// down, answers with an error status, cuts the body short, or stops sending
    /// body data. A cancellation the caller asked for is not one.
    /// </summary>
    private static bool IsSourceFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException or HttpIOException or TimeoutException ||
        (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private async Task<Fetched> FetchAsync(
        Uri url,
        string archivePath,
        DownloadInfo download,
        IProgress<DownloadProgress>? progress,
        DownloadPause? pause,
        CancellationToken cancellationToken)
    {
        await using var file = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        using var part = new Part(file, download.SizeBytes ?? -1);
        while (true)
        {
            if (pause is { IsPaused: true })
            {
                progress?.Report(new DownloadProgress(part.Bytes, part.Total, IsPaused: true));
                await pause.WaitForResumeAsync(cancellationToken).ConfigureAwait(false);
            }

            var paused = pause?.Token ?? CancellationToken.None;
            using var transfer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, paused);
            try
            {
                if (await TransferAsync(url, part, download, progress, transfer.Token, cancellationToken).ConfigureAwait(false) is { } fetched)
                    return fetched;
            }
            catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or HttpIOException
                && paused.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    /// <summary>
    /// Asks for the rest of the part, or for the whole file when the part is
    /// empty or cannot resume. Null when the answer does not continue the part,
    /// which is then empty for the next request.
    /// </summary>
    /// <param name="transferToken">Also canceled by a pause, which keeps the part.</param>
    private async Task<Fetched?> TransferAsync(
        Uri url,
        Part part,
        DownloadInfo download,
        IProgress<DownloadProgress>? progress,
        CancellationToken transferToken,
        CancellationToken cancellationToken)
    {
        if (!part.CanResume)
            part.Restart();

        var resuming = part.Bytes > 0;
        if (resuming)
            progress?.Report(new DownloadProgress(part.Bytes, part.Total));

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (resuming)
        {
            request.Headers.Range = new RangeHeaderValue(part.Bytes, null);
            request.Headers.IfRange = part.Validator;
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, transferToken).ConfigureAwait(false);
        var continues = resuming && part.IsContinuedBy(response);
        if (resuming && !continues)
        {
            part.Restart();
            if (response.StatusCode is HttpStatusCode.PartialContent or HttpStatusCode.RequestedRangeNotSatisfiable)
                return null;
        }

        response.EnsureSuccessStatusCode();

        // Without a hash the stated size is the only check there is, so it is
        // applied before and during the transfer and not only after it.
        var sizeDecides = download.Sha256 is null ? download.SizeBytes : null;
        if (!continues)
        {
            var announced = response.Content.Headers.ContentLength;
            if (sizeDecides is { } stated && announced is { } length && length != stated)
                return Fetched.Rejected($"announces {length} bytes where the release states {stated}");

            part.Begin(response, announced ?? download.SizeBytes ?? -1);
        }

        await using (var content = await response.Content.ReadAsStreamAsync(transferToken).ConfigureAwait(false))
        {
            var buffer = new byte[BufferSize];
            int read;
            while ((read = await ReadBodyAsync(content, buffer, transferToken).ConfigureAwait(false)) > 0)
            {
                if (sizeDecides is { } cap && part.Bytes + read > cap)
                    return Fetched.Rejected($"sends more than the {cap} bytes the release states");

                await part.AppendAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                progress?.Report(new DownloadProgress(part.Bytes, part.Total));
            }
        }

        var sha256 = part.Sha256();
        return Mismatch(download, part.Bytes, sha256) is { } mismatch
            ? Fetched.Rejected(mismatch)
            : Fetched.Served(part.Bytes, sha256);
    }

    private async ValueTask<int> ReadBodyAsync(Stream content, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        using var inactivityCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        inactivityCancellation.CancelAfter(_bodyInactivityTimeout);

        try
        {
            return await content.ReadAsync(buffer, inactivityCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && inactivityCancellation.IsCancellationRequested)
        {
            throw new TimeoutException($"The response body transferred no data for {_bodyInactivityTimeout}.", ex);
        }
    }

    /// <summary>
    /// Why the received bytes are not the release's archive, or null when they
    /// are. The hash decides where the release has one, because any source whose
    /// bytes match it is acceptable (RFC 0031). The stated size is the only
    /// check left for a release without one.
    /// </summary>
    private static string? Mismatch(DownloadInfo download, long bytes, string sha256)
    {
        if (download.Sha256 is not null)
            return download.HashMatches(sha256) ? null : $"SHA-256 {sha256} does not match the release's {download.Sha256}";

        if (download.SizeBytes is { } size && size != bytes)
            return $"received {bytes} bytes where the release states {size}";

        return null;
    }

    /// <summary>
    /// The bytes one source served so far, with their running hash and what the
    /// first answer said about the file, so that a resume can ask for the rest.
    /// </summary>
    private sealed class Part(FileStream file, long statedSize) : IDisposable
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private long? _length;

        public long Bytes { get; private set; }

        /// <summary>The total size for progress reports, -1 when it is unknown.</summary>
        public long Total { get; private set; } = statedSize;

        /// <summary>The strong ETag of the first answer, or its Last-Modified date when it has no strong ETag.</summary>
        public RangeConditionHeaderValue? Validator { get; private set; }

        public bool CanResume => Bytes > 0 && Validator is not null && _length is not null;

        public void Begin(HttpResponseMessage response, long total)
        {
            Total = total;
            _length = response.Content.Headers.ContentLength;
            Validator = response.Headers.ETag is { IsWeak: false } etag ? new RangeConditionHeaderValue(etag)
                : response.Content.Headers.LastModified is { } modified ? new RangeConditionHeaderValue(modified)
                : null;
        }

        /// <summary>Whether the answer to a range request holds exactly the rest of the same file.</summary>
        public bool IsContinuedBy(HttpResponseMessage response) =>
            response.StatusCode == HttpStatusCode.PartialContent
            && response.Content.Headers.ContentRange is { HasRange: true, HasLength: true } range
            && range.From == Bytes
            && range.Length == _length
            && range.To == _length - 1;

        public async Task AppendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        {
            await file.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            _hash.AppendData(data.Span);
            Bytes += data.Length;
        }

        public void Restart()
        {
            if (Bytes > 0)
            {
                file.SetLength(0);
                file.Position = 0;
                _hash.GetHashAndReset();
                Bytes = 0;
            }

            _length = null;
            Validator = null;
        }

        public string Sha256() => Convert.ToHexString(_hash.GetHashAndReset());

        public void Dispose() => _hash.Dispose();
    }

    /// <summary>What one source produced: the archive, or the reason its bytes were refused.</summary>
    private readonly record struct Fetched(long Bytes, string Sha256, string? Rejection)
    {
        public static Fetched Served(long bytes, string sha256) => new(bytes, sha256, null);

        public static Fetched Rejected(string reason) => new(0, string.Empty, reason);
    }
}
