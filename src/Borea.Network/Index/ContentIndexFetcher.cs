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
            // I am not sure what to throw here but the caller should know that the response was not what was expected.
            throw new HttpRequestException($"ETag does not exist yet request returned 304 Not Modified: {response.ReasonPhrase}");
        }

        response.EnsureSuccessStatusCode();

        // Read the content as a byte array to verify the JSON structure before writing to disk.
        byte[] body = await response.Content.ReadAsByteArrayAsync(ct);

        VerifyIndexBasics(response.Content, body);

        try
        {
            // Write the index to disk in a temp file then move to real file.
            // This is to avoid leaving a corrupted file if the process is interrupted.
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
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

    /// <summary>
    /// Verifies that the content index has the basic required fields and is valid JSON.
    /// Only checks that "snapshot_version" and "sources" field exists.
    /// </summary>
    /// <returns>No return, only Exceptions</returns>
    /// <exception cref="HttpRequestException"></exception>
    private static void VerifyIndexBasics(HttpContent content, byte[] body)
    {
        if (content.Headers.ContentType?.MediaType != "application/json")
        {
            throw new HttpRequestException($"Expected content type 'application/json' but got '{content.Headers.ContentType?.MediaType}'");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException("Failed to parse JSON content", ex);
        }

        using (document)
        {
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new HttpRequestException("Expected JSON object as root element");
            }

            if (!root.TryGetProperty("snapshot_version", out _))
            {
                throw new HttpRequestException("Content index is missing required field 'snapshot_version'.");
            }

            if (!root.TryGetProperty("sources", out _))
            {
                throw new HttpRequestException("Content index is missing required field 'sources'.");
            }
        }
    }
}
