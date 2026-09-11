using System.Security.Cryptography;
using Borea.Core.Mods;

namespace Borea.Network.Downloads;

/// <summary>
/// <see cref="IModDownloader"/> over plain HTTP. The release file already holds
/// everything host-specific, so this asks no host API for anything: it streams
/// the archive from the URL into the file, hashes it on the way, and moves on
/// to the next mirror when a source fails or serves other bytes.
/// </summary>
public sealed class HttpModDownloader : IModDownloader
{
    private const int BufferSize = 81920;

    private readonly HttpClient _httpClient;

    public HttpModDownloader(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
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
                    fetched = await FetchAsync(url, archivePath, download, progress, cancellationToken).ConfigureAwait(false);
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
    /// down, answers with an error status, cuts the body short, or stalls until
    /// HttpClient.Timeout ends the request. A cancellation the caller asked for
    /// is not one.
    /// </summary>
    private static bool IsSourceFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException or HttpIOException ||
        (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private async Task<Fetched> FetchAsync(
        Uri url,
        string archivePath,
        DownloadInfo download,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // Without a hash the stated size is the only check there is, so it is
        // applied before and during the transfer and not only after it.
        var sizeDecides = download.Sha256 is null ? download.SizeBytes : null;
        var announced = response.Content.Headers.ContentLength;
        if (sizeDecides is { } stated && announced is { } length && length != stated)
            return Fetched.Rejected($"announces {length} bytes where the release states {stated}");

        var totalBytes = announced ?? download.SizeBytes ?? -1;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long bytesDownloaded = 0;

        await using (var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var file = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
        {
            var buffer = new byte[BufferSize];
            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                bytesDownloaded += read;
                if (sizeDecides is { } cap && bytesDownloaded > cap)
                    return Fetched.Rejected($"sends more than the {cap} bytes the release states");

                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                hash.AppendData(buffer, 0, read);
                progress?.Report(new DownloadProgress(bytesDownloaded, totalBytes));
            }
        }

        var sha256 = Convert.ToHexString(hash.GetHashAndReset());
        return Mismatch(download, bytesDownloaded, sha256) is { } mismatch
            ? Fetched.Rejected(mismatch)
            : Fetched.Served(bytesDownloaded, sha256);
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

    /// <summary>What one source produced: the archive, or the reason its bytes were refused.</summary>
    private readonly record struct Fetched(long Bytes, string Sha256, string? Rejection)
    {
        public static Fetched Served(long bytes, string sha256) => new(bytes, sha256, null);

        public static Fetched Rejected(string reason) => new(0, string.Empty, reason);
    }
}
