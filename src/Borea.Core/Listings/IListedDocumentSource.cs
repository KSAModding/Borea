namespace Borea.Core.Listings;

/// <summary>Reads the listed documents of content-index as the main branch holds them.</summary>
public interface IListedDocumentSource
{
    /// <exception cref="HttpRequestException">The document could not be read.</exception>
    Task<string> GetListingAsync(string id, CancellationToken cancellationToken = default);
}
