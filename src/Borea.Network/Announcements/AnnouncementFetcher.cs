using System.Net;
using System.Net.Http.Headers;
using Borea.Core.Announcements;

namespace Borea.Network.Announcements;

/// <summary>Fetches announcements.toml with the ETag of the cached file, like the content index.</summary>
public sealed class AnnouncementFetcher : IAnnouncementFetcher
{
    public static readonly Uri DefaultUri = new("https://raw.githubusercontent.com/KSAModding/Borea/main/announcements.toml");

    private readonly HttpClient _client;
    private readonly Uri _uri;
    private readonly IAnnouncementReader _reader;

    public AnnouncementFetcher(HttpClient client, Uri uri, IAnnouncementReader reader)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _uri = uri ?? throw new ArgumentNullException(nameof(uri));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<bool> FetchAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destinationPath);
        var etagPath = destinationPath + ".etag";
        var tempPath = destinationPath + ".tmp";

        using var request = new HttpRequestMessage(HttpMethod.Get, _uri);
        var sentEtag = false;
        if (File.Exists(destinationPath) && File.Exists(etagPath))
        {
            if (EntityTagHeaderValue.TryParse((await File.ReadAllTextAsync(etagPath, cancellationToken).ConfigureAwait(false)).Trim(), out var tag))
            {
                request.Headers.IfNoneMatch.Add(tag);
                sentEtag = true;
            }
            else
            {
                File.Delete(etagPath);
            }
        }

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return sentEtag
                ? false
                : throw new HttpRequestException("The host answered 304 Not Modified to a request without an ETag.");
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        try
        {
            await File.WriteAllBytesAsync(tempPath, body, cancellationToken).ConfigureAwait(false);
            await _reader.ReadAsync(tempPath, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        finally
        {
            File.Delete(tempPath);
        }

        if (response.Headers.ETag?.ToString() is { } etag)
            await File.WriteAllTextAsync(etagPath, etag, cancellationToken).ConfigureAwait(false);
        else
            File.Delete(etagPath);

        return true;
    }
}
