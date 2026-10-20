using System.Net.Http.Headers;
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
    /// <param name="destinationPath">The full path to the file where the index will be saved. (ex.g C:/Users/username/%LocalAppData%/Borea/index.json)</param>
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
        CancellationToken ct)
    {
        string etagPath = destinationPath + ".etag";
        string tempPath = destinationPath + ".tmp";

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);

        bool etagExists = File.Exists(destinationPath) && File.Exists(etagPath);

        if (etagExists)
        {
            string existingEtag = (await File.ReadAllTextAsync(etagPath, ct)).Trim();
            if (EntityTagHeaderValue.TryParse(existingEtag, out var tag))
            {
                request.Headers.IfNoneMatch.Add(tag);
            }
            else
            {
                etagExists = false;
                // Deletes the ETag file if the file is corrupted or can't be parsed.
                // The index will be downloaded even if the file was not deleted, but
                // this prevents the corrupted ETag from being used in future requests.
                File.Delete(etagPath);
            }
        }

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (missingIsEmpty && response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return ContentIndexFetchResult.NotModified;
        }

        if (etagExists && response.StatusCode == System.Net.HttpStatusCode.NotModified)
        {
            return ContentIndexFetchResult.NotModified;
        }
        else if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
        {
            // This is to prevent a situation where we should've downloaded the index, yet didn't due to a incorrect 304 response.
            throw new HttpRequestException($"ETag does not exist yet request returned 304 Not Modified: {response.ReasonPhrase}");
        }

        response.EnsureSuccessStatusCode();

        // Read the content as a byte array to then write to disk.
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
            // Metadata change that only changes the file's name
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        finally
        {
            File.Delete(tempPath);
        }

        string? newEtag = response.Headers.ETag?.ToString();

        if (newEtag is not null)
        {
            await File.WriteAllTextAsync(etagPath, newEtag, ct);
        }
        else if (File.Exists(etagPath))
        {
            File.Delete(etagPath);
        }

        return ContentIndexFetchResult.Downloaded;
    }

}
