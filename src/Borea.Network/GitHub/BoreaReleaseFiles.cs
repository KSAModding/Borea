using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Borea.Core.Logging;
using Borea.Core.Mods;
using Borea.Core.Updates;

namespace Borea.Network.GitHub;

/// <summary>
/// <see cref="IBoreaReleaseFiles"/> against the release downloads of the Borea repository. Only a URL
/// of that repository is fetched, although the host is pinned for the first request alone, because a
/// release download answers with a redirect to the storage host that serves the bytes. A file above
/// the limits here is refused before it is written, so a wrong answer cannot fill the disk.
/// </summary>
/// <remarks>
/// GitHub can answer the download link of one file with a server error every time while the API
/// address of the same file serves it, and a published release cannot be changed to repair that.
/// So a download link that fails with a transport error, a timeout or a 5xx answer is asked once more
/// through the API address of the file. Both addresses go through the same limits, and the caller
/// checks the SHA-256 of what arrived in the same way.
/// </remarks>
public sealed class BoreaReleaseFiles : IBoreaReleaseFiles
{
    /// <summary>The checksums file holds one short line per published file.</summary>
    internal const int MaxTextBytes = 64 * 1024;

    /// <summary>A self-contained archive is far below this, which leaves room to grow.</summary>
    internal const long MaxArchiveBytes = 512L * 1024 * 1024;

    private const int BufferSize = 81920;

    private readonly HttpClient _httpClient;
    private readonly IBoreaLog? _log;

    /// <param name="log">Where the address that served a file is written. Null writes nothing.</param>
    public BoreaReleaseFiles(HttpClient httpClient, IBoreaLog? log = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _log = log;
    }

    public async Task<string?> ReadTextAsync(BoreaReleaseAsset asset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (!IsReleaseDownload(asset.Url))
            return null;

        try
        {
            return await FetchAsync(asset, url => ReadTextFromAsync(url, cancellationToken), cancellationToken).ConfigureAwait(false);
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
        BoreaReleaseAsset asset,
        string path,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (!IsReleaseDownload(asset.Url))
            throw new HttpRequestException($"'{asset.Url}' is not a release download of the Borea repository.");

        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return await FetchAsync(asset, url => DownloadFromAsync(url, path, progress, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches <paramref name="asset"/> from its download link, and once more from its API address
    /// when the link failed in a way that another address can repair. A 404, a file above the limits
    /// and the caller's cancellation are final.
    /// </summary>
    private async Task<T> FetchAsync<T>(BoreaReleaseAsset asset, Func<string, Task<T>> fetch, CancellationToken cancellationToken)
    {
        var apiUrl = BoreaReleaseCheck.IsAssetApiUrl(asset.ApiUrl) ? asset.ApiUrl : null;
        try
        {
            var result = await fetch(asset.Url).ConfigureAwait(false);
            _log?.Write($"Self-update: {asset.Name} came from {asset.Url}.");
            return result;
        }
        catch (Exception exception) when (apiUrl is not null && CanAnotherAddressServe(exception, cancellationToken))
        {
            _log?.Write($"Self-update: {asset.Name} could not be downloaded from {asset.Url}, so Borea asks {apiUrl}. {exception.GetType().Name}: {exception.Message}");
        }

        var fallback = await fetch(apiUrl).ConfigureAwait(false);
        _log?.Write($"Self-update: {asset.Name} came from {apiUrl}.");
        return fallback;
    }

    /// <summary>Whether the failure is one of the address that answered and not of the file, which another address can repair.</summary>
    private static bool CanAnotherAddressServe(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        HttpRequestException { HttpRequestError: HttpRequestError.ConfigurationLimitExceeded } => false,
        HttpRequestException { StatusCode: { } status } => (int)status >= 500,
        HttpRequestException or HttpIOException => true,

        // a timeout, and not the caller's cancellation
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false,
    };

    private async Task<string> ReadTextFromAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxTextBytes)
            throw TooLarge($"'{url}' announces more than the {MaxTextBytes} bytes of a checksums file.");

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var text = new MemoryStream();
        var buffer = new byte[BufferSize];
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (text.Length + read > MaxTextBytes)
                throw TooLarge($"'{url}' sends more than the {MaxTextBytes} bytes of a checksums file.");

            text.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(text.GetBuffer(), 0, (int)text.Length);
    }

    private async Task<string> DownloadFromAsync(string url, string path, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1;
            if (total > MaxArchiveBytes)
                throw TooLarge($"'{url}' announces {total} bytes, which is more than Borea downloads for an update.");

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
                        throw TooLarge($"'{url}' sends more than the {MaxArchiveBytes} bytes Borea downloads for an update.");

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

    /// <summary>
    /// Sends the request for one address. The API address serves the file itself only when it is
    /// asked for raw bytes, and otherwise answers with the JSON that describes the file.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (BoreaReleaseCheck.IsAssetApiUrl(url))
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            request.Headers.Add("X-GitHub-Api-Version", BoreaReleaseCheck.ApiVersion);
        }

        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A file above the limits, which no other address of the same file can repair.</summary>
    private static HttpRequestException TooLarge(string message)
        => new(HttpRequestError.ConfigurationLimitExceeded, message);

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
