using System.Security.Cryptography;
using System.Text;
using Borea.Core.Mods;
using Borea.Core.Updates;

namespace Borea.Network.GitHub;

/// <summary>
/// <see cref="IBoreaReleaseFiles"/> against the release downloads of the Borea repository. Only a URL
/// of that repository is fetched, although the host is pinned for the first request alone, because a
/// release download answers with a redirect to the storage host that serves the bytes. A file above
/// the limits here is refused before it is written, so a wrong answer cannot fill the disk.
/// </summary>
public sealed class BoreaReleaseFiles : IBoreaReleaseFiles
{
    /// <summary>The checksums file holds one short line per published file.</summary>
    internal const int MaxTextBytes = 64 * 1024;

    /// <summary>A self-contained archive is far below this, which leaves room to grow.</summary>
    internal const long MaxArchiveBytes = 512L * 1024 * 1024;

    private const int BufferSize = 81920;

    private readonly HttpClient _httpClient;

    public BoreaReleaseFiles(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<string?> ReadTextAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!IsReleaseDownload(url))
            return null;

        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxTextBytes)
                return null;

            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var text = new MemoryStream();
            var buffer = new byte[BufferSize];
            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (text.Length + read > MaxTextBytes)
                    return null;

                text.Write(buffer, 0, read);
            }

            return Encoding.UTF8.GetString(text.GetBuffer(), 0, (int)text.Length);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or HttpIOException or OperationCanceledException)
        {
            return null;
        }
    }

    public async Task<string> DownloadAsync(
        string url,
        string path,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsReleaseDownload(url))
            throw new HttpRequestException($"'{url}' is not a release download of the Borea repository.");

        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1;
            if (total > MaxArchiveBytes)
                throw new HttpRequestException($"'{url}' announces {total} bytes, which is more than Borea downloads for an update.");

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            await using (var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            {
                var buffer = new byte[BufferSize];
                long received = 0;
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    received += read;
                    if (received > MaxArchiveBytes)
                        throw new HttpRequestException($"'{url}' sends more than the {MaxArchiveBytes} bytes Borea downloads for an update.");

                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    hash.AppendData(buffer.AsSpan(0, read));
                    progress?.Report(new DownloadProgress(received, total));
                }
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    private static bool IsReleaseDownload(string? url)
        => url is not null && url.StartsWith(BoreaReleaseCheck.DownloadUrlPrefix, StringComparison.Ordinal);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
