using System.Net;
using System.Security.Cryptography;
using Borea.Core.Mods;
using Borea.Core.Sharing;

namespace Borea.Network.Downloads;

/// <summary>
/// Fetches a vehicle file that Borea asks for by itself, from a share host that keeps no download count. It sends one
/// plain GET to the URL of the release and to no mirror, follows a redirect only under the download prefixes of the
/// host, and reads no more than the stated size, so a fetch that nobody clicked for stays small and stays on that host.
/// </summary>
/// <param name="http">A client that does not follow redirects by itself.</param>
public sealed class ShareFileDownloader(HttpClient http, ShareHost host) : IModDownloader
{
    private const int BufferSize = 81920;
    private const int MaxRedirects = 3;
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(1);

    public async Task<DownloadResult> DownloadAsync(
        ModVersionMetadata release,
        string archivePath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Limit);
        try
        {
            return await FetchAsync(release, archivePath, progress, deadline.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or HttpIOException or IOException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            TryDelete(archivePath);
            throw new DownloadFailedException($"{host.Name} did not serve the archive of '{release.ModId}' {release.Version}: {exception.Message}", exception);
        }
        catch
        {
            TryDelete(archivePath);
            throw;
        }
    }

    private async Task<DownloadResult> FetchAsync(ModVersionMetadata release, string archivePath, IProgress<DownloadProgress>? progress, CancellationToken token)
    {
        if (release.Download is not { Sha256: { } sha256, SizeBytes: { } stated })
            throw new DownloadFailedException($"The release '{release.ModId}' {release.Version} states no hash or no size.");

        if (!Uri.TryCreate(release.Download.Url, UriKind.Absolute, out var url) || !host.ServesDownload(url))
            throw new DownloadFailedException($"The URL of '{release.ModId}' {release.Version} is not under the download prefixes of {host.Name}.");

        for (var redirects = 0; ; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308))
            {
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new DownloadFailedException($"{url} answered HTTP {(int)response.StatusCode}.");

                return await ReadAsync(release, response, url, archivePath, sha256, stated, progress, token).ConfigureAwait(false);
            }

            var next = response.Headers.Location is { } location ? new Uri(url, location) : null;
            if (redirects == MaxRedirects || next is null || !host.ServesDownload(next))
                throw new DownloadFailedException($"{url} redirects to {next?.AbsoluteUri ?? "nowhere"}, which is not under the download prefixes of {host.Name}.");

            url = next;
        }
    }

    private static async Task<DownloadResult> ReadAsync(ModVersionMetadata release, HttpResponseMessage response, Uri url, string archivePath, string sha256, long stated, IProgress<DownloadProgress>? progress, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength is { } announced && announced > stated)
            throw new DownloadFailedException($"{url} announces {announced} bytes where the release states {stated}.");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long received = 0;
        await using (var file = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
        await using (var body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
        {
            var buffer = new byte[BufferSize];
            int read;
            while ((read = await body.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                received += read;
                if (received > stated)
                    throw new DownloadFailedException($"{url} sends more than the {stated} bytes that the release states.");

                hash.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                progress?.Report(new DownloadProgress(received, stated));
            }
        }

        var actual = Convert.ToHexString(hash.GetHashAndReset());
        if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
            throw new DownloadFailedException($"The archive of '{release.ModId}' {release.Version} has the SHA-256 {actual}, and the release names {sha256}.");

        return new DownloadResult(url.AbsoluteUri, received, actual);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The caller keeps the file under a name of its own for one fetch, and its sweep removes what stays behind.
        }
    }
}
