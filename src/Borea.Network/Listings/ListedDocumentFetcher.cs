using System.Text;
using Borea.Core.Listings;

namespace Borea.Network.Listings;

/// <summary>Reads the listed documents and the authored schema of content-index from its main branch.</summary>
public sealed class ListedDocumentFetcher : IListedDocumentSource, IListingSchemaFetcher
{
    internal const string ContentIndexUrl = "https://raw.githubusercontent.com/KSAModding/content-index/main/";

    private const int MaxDocumentBytes = 1024 * 1024;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;

    public ListedDocumentFetcher(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public Task<string> GetListingAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        return GetAsync($"{ListingDraft.ListingsFolder}/{Uri.EscapeDataString(id)}.toml", cancellationToken);
    }

    public Task<string> FetchAsync(CancellationToken cancellationToken = default) => GetAsync("schemas/authored.schema.json", cancellationToken);

    private async Task<string> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Timeout);
        using var response = await _http.GetAsync(ContentIndexUrl + path, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxDocumentBytes)
            throw TooLarge(path);

        await using var body = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        using var document = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await body.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) > 0)
        {
            document.Write(buffer, 0, read);
            if (document.Length > MaxDocumentBytes)
                throw TooLarge(path);
        }

        return Encoding.UTF8.GetString(document.GetBuffer(), 0, (int)document.Length);
    }

    private static HttpRequestException TooLarge(string path) => new($"{ContentIndexUrl}{path} is larger than {MaxDocumentBytes} bytes.");
}
