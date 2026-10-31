namespace Borea.Core.Listings;

/// <summary>Reads the listed documents of content-index as the main branch holds them.</summary>
public interface IListedDocumentSource
{
    /// <exception cref="HttpRequestException">The document could not be read.</exception>
    Task<string> GetListingAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Reads the listing of the type from its folder, such as vehicles/&lt;id&gt;.toml for a vehicle (<see cref="ListingFolders.Of"/>).</summary>
    /// <exception cref="HttpRequestException">The document could not be read.</exception>
    Task<string> GetListingAsync(string id, string type, CancellationToken cancellationToken = default);

    /// <summary>Reads the file of one pack version, packs/&lt;id&gt;/&lt;version&gt;.toml.</summary>
    /// <exception cref="HttpRequestException">The document could not be read.</exception>
    Task<string> GetPackVersionAsync(string id, string version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether main already has the file of this pack version. It reads the file and not the contents API, which allows
    /// 60 calls an hour without a token. The paths are case-sensitive, so the id needs the spelling of the index.
    /// </summary>
    /// <exception cref="HttpRequestException">GitHub answered neither with the file nor with not found.</exception>
    Task<bool> HasPackVersionAsync(string id, string version, CancellationToken cancellationToken = default);
}
