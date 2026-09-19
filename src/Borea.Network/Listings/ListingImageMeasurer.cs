using Borea.Core.Index;
using Borea.Core.Listings;
using Borea.Network.Images;

namespace Borea.Network.Listings;

/// <summary>Fetches a hosted image with the handler and rules of the listing images, and measures its bytes.</summary>
public sealed class ListingImageMeasurer : IListingImageMeasurer, IDisposable
{
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(30);

    private readonly ContentImageFetcher _fetcher;

    public ListingImageMeasurer()
        : this(ContentImageFetcher.CreateHandler(), FetchTimeout)
    {
    }

    internal ListingImageMeasurer(HttpMessageHandler handler, TimeSpan timeout)
    {
        _fetcher = new ContentImageFetcher(handler, timeout);
    }

    public async Task<ListingImageMeasurement> MeasureAsync(string url, ListingImageRole role, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return ListingImageMeasurement.Failed($"{url} is not an https URL");

        var result = await _fetcher.FetchAsync(uri.AbsoluteUri, ListingImageMeasurement.MaxBytes(role), ContentImageResult.Loaded, cancellationToken).ConfigureAwait(false);
        return result.IsLoaded
            ? ListingImageMeasurement.Of(result.Bytes.Span, role)
            : ListingImageMeasurement.Failed(result.Reason!);
    }

    public void Dispose() => _fetcher.Dispose();
}
