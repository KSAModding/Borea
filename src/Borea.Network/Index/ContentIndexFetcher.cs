using System.Net.Http.Headers;
using System.Text.Json;
using Borea.Core.Index;

namespace Borea.Network.Index;

public sealed class ContentIndexFetcher : IContentIndexFetcher
{
    private readonly HttpClient _client;
    private readonly Uri _indexUri;

    public ContentIndexFetcher(HttpClient client, Uri indexUri)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _indexUri = indexUri ?? throw new ArgumentNullException(nameof(indexUri));
    }

    /// <summary>
    /// Fetches the content index from the remote server and saves it to the specified destination path.
    /// Uses an ETag to determine if the index has changed since the last fetch.
    /// </summary>
    /// <param name="destinationPath"></param>
    /// <param name="ct"></param>
    /// <exception cref="HttpRequestException"></exception>
    public async Task<ContentIndexFetchResult> FetchAsync(string destinationPath, CancellationToken ct = default)
    {
        string etagPath = destinationPath + ".etag";
        string tempPath = destinationPath + ".tmp";

        using var request = new HttpRequestMessage(HttpMethod.Get, _indexUri);

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

        // Make sure index is JSON or plain text
        if (response.Content.Headers.ContentType?.MediaType != "application/json" && response.Content.Headers.ContentType?.MediaType != "text/plain")
        {
            throw new HttpRequestException($"Expected content type 'application/json' or 'text/plain' but got '{response.Content.Headers.ContentType?.MediaType}'");
        }

        // Read the content as a byte array to then write to disk.
        byte[] body = await response.Content.ReadAsByteArrayAsync(ct);

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        try
        {
            // Write the index to disk in a temp file then move to real file.
            // This is to avoid leaving a corrupted file if the download is interrupted.
            await File.WriteAllBytesAsync(tempPath, body, ct);
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
