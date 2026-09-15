namespace Borea.Core.Index;

/// <summary>Loads the image of an image record and verifies its bytes against the record (RFC 0058).</summary>
public interface IContentImageSource
{
    /// <param name="loadFromAuthorHosts">False makes no network request and serves only an image that is already cached.</param>
    Task<ContentImageResult> GetAsync(ContentImage image, bool loadFromAuthorHosts, CancellationToken cancellationToken = default);
}
