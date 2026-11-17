using System.Net.Http.Headers;
using System.Security.Cryptography;
using Borea.Core.Files;
using Borea.Core.Index;
using Borea.Core.Logging;

namespace Borea.Network.Index;

public sealed class ContentIndexFetcher : IContentIndexFetcher
{
    private readonly HttpClient _client;
    private readonly Uri _indexUri;
    private readonly IContentIndexCandidateValidator _candidateValidator;
    private readonly IReadOnlyList<ContentIndexTypeFile> _typeFiles;
    private readonly IBoreaLog? _log;

    /// <param name="typeFiles">The type files fetched next to the index. Null fetches none.</param>
    /// <param name="log">Where a type file that could not be refreshed is written. Null writes nothing.</param>
    public ContentIndexFetcher(
        HttpClient client,
        Uri indexUri,
        IContentIndexCandidateValidator candidateValidator,
        IReadOnlyList<ContentIndexTypeFile>? typeFiles = null,
        IBoreaLog? log = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _indexUri = indexUri ?? throw new ArgumentNullException(nameof(indexUri));
        _candidateValidator = candidateValidator ?? throw new ArgumentNullException(nameof(candidateValidator));
        _typeFiles = typeFiles ?? [];
        _log = log;
    }

    /// <summary>
    /// Fetches the content index from the remote server and saves it to the specified destination path.
    /// Uses an ETag to determine if the index has changed since the last fetch. Then fetches each type
    /// file next to it against its own ETag; a type file never fails the fetch, see <see cref="FetchTypeFileAsync"/>.
    /// </summary>
    /// <param name="destinationPath">The full path to the file where the index will be saved.</param>
    /// <returns>Downloaded when the index or any type file changed, so a reader reads them again.</returns>
    /// <exception cref="HttpRequestException"></exception>
    public async Task<ContentIndexFetchResult> FetchAsync(string destinationPath, CancellationToken ct = default)
    {
        var result = await FetchFileAsync(_indexUri, destinationPath, ContentIndexRootValidator.ValidateIndexRoot, _candidateValidator, missingIsEmpty: false, ct).ConfigureAwait(false);

        foreach (var file in _typeFiles)
        {
            if (await FetchTypeFileAsync(file, destinationPath, ct).ConfigureAwait(false) == ContentIndexFetchResult.Downloaded)
                result = ContentIndexFetchResult.Downloaded;
        }

        return result;
    }

    /// <summary>
    /// A 404 leaves the cache as it is: no copy reads as no entries, and a copy is kept. Every other
    /// failure keeps the copy too, because a published type file never goes away and a fault read as
    /// "no vehicles" would show every installed vehicle as unlisted. Only the root is checked, because
    /// each entry fails alone when it is read.
    /// </summary>
    private async Task<ContentIndexFetchResult> FetchTypeFileAsync(ContentIndexTypeFile file, string indexPath, CancellationToken ct)
    {
        try
        {
            return await FetchFileAsync(
                file.AddressFrom(_indexUri),
                file.CachePathFrom(indexPath),
                (body, response) => ContentIndexRootValidator.ValidateTypeFileRoot(body, response, file),
                candidateValidator: null,
                missingIsEmpty: true,
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException)
        {
            _log?.Write($"Index fetch: kept the cached {file.FileName}, the fetch failed: {exception.GetType().Name}: {exception.Message}");
            return ContentIndexFetchResult.NotModified;
        }
    }

    private async Task<ContentIndexFetchResult> FetchFileAsync(
        Uri uri,
        string destinationPath,
        Func<byte[], HttpResponseMessage, bool> validateRoot,
        IContentIndexCandidateValidator? candidateValidator,
        bool missingIsEmpty,
        CancellationToken ct,
        bool useCachedEtag = true)
    {
        string etagPath = destinationPath + ".etag";
        string tempPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        string etagTempPath = $"{etagPath}.{Guid.NewGuid():N}.tmp";

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);

        var cachedEtag = useCachedEtag ? await ReadCachedEtagAsync(destinationPath, ct).ConfigureAwait(false) : null;
        if (cachedEtag is { } cached)
            request.Headers.IfNoneMatch.Add(cached.Tag);

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (missingIsEmpty && response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return ContentIndexFetchResult.NotModified;
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
        {
            if (cachedEtag is { } sent)
            {
                // A 304 confirms the request's body, which another process can replace while the request is in flight.
                if (sent.BodyHash == await ReadBodyHashAsync(destinationPath, ct).ConfigureAwait(false))
                    return ContentIndexFetchResult.NotModified;

                response.Dispose();
                return await FetchFileAsync(uri, destinationPath, validateRoot, candidateValidator, missingIsEmpty, ct, useCachedEtag: false).ConfigureAwait(false);
            }

            throw new HttpRequestException($"ETag does not exist yet request returned 304 Not Modified: {response.ReasonPhrase}");
        }

        response.EnsureSuccessStatusCode();

        byte[] body = await response.Content.ReadAsByteArrayAsync(ct);

        validateRoot(body, response);

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        try
        {
            // The reader accepts the complete candidate before one atomic move
            // replaces the persistent known-good snapshot.
            await File.WriteAllBytesAsync(tempPath, body, ct);
            if (candidateValidator is not null)
            {
                try
                {
                    await candidateValidator.ValidateAsync(tempPath, ct).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex)
                {
                    throw new HttpRequestException("The downloaded content index was not accepted.", ex);
                }
            }
            await FileReplace.MoveOverAsync(tempPath, destinationPath, ct).ConfigureAwait(false);

            string? newEtag = response.Headers.ETag?.ToString();
            if (newEtag is not null)
            {
                var sidecar = newEtag + "\n" + Convert.ToHexString(SHA256.HashData(body));
                await File.WriteAllTextAsync(etagTempPath, sidecar, ct).ConfigureAwait(false);
                await FileReplace.MoveOverAsync(etagTempPath, etagPath, ct).ConfigureAwait(false);
            }
            else
            {
                File.Delete(etagPath);
            }
        }
        finally
        {
            File.Delete(tempPath);
            File.Delete(etagTempPath);
        }

        return ContentIndexFetchResult.Downloaded;
    }

    private static async Task<(EntityTagHeaderValue Tag, string BodyHash)?> ReadCachedEtagAsync(string destinationPath, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(new FileStream(destinationPath + ".etag", FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan));
            var tagText = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            var bodyHash = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            // Separate body and sidecar moves can overlap, so a tag is usable only for its own body.
            if (await reader.ReadLineAsync(ct).ConfigureAwait(false) is null && EntityTagHeaderValue.TryParse(tagText, out var tag)
                && bodyHash is not null && bodyHash == await ReadBodyHashAsync(destinationPath, ct).ConfigureAwait(false))
                return (tag, bodyHash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The sidecar is optional, and another process can replace it before it is read.
        }

        return null;
    }

    private static async Task<string?> ReadBodyHashAsync(string destinationPath, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(destinationPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
