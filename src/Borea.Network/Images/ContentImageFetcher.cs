using System.Net;
using System.Net.Http.Headers;
using Borea.Core.Index;

namespace Borea.Network.Images;

/// <summary>Fetches the image of one record, and follows redirects itself so every hop is HTTPS and the chain stops after three.</summary>
internal sealed class ContentImageFetcher : IDisposable
{
    public const int MaxRedirects = 3;

    private const int ChunkSize = 64 * 1024;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(2);

    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;

    public ContentImageFetcher(HttpMessageHandler handler, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(timeout), "The timeout must be a positive finite timer interval.");

        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Borea", typeof(ContentImageFetcher).Assembly.GetName().Version?.ToString(3)));
        _timeout = timeout;
    }

    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        Credentials = null,
        PreAuthenticate = false,
        UseProxy = false,
        AutomaticDecompression = DecompressionMethods.None,
        MaxResponseDrainSize = 0,
        ConnectTimeout = ConnectTimeout,
        PooledConnectionLifetime = ConnectionLifetime,
        ConnectCallback = PublicNetworkTarget.ConnectAsync,
    };

    public async Task<ContentImageResult> FetchAsync(ContentImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);

        try
        {
            var url = new Uri(image.Url);
            for (var redirects = 0; ; redirects++)
            {
                if (url.Scheme != Uri.UriSchemeHttps || url.UserInfo.Length > 0)
                {
                    return ContentImageResult.Failed(
                        redirects == 0 ? ContentImageFailure.BlockedNetworkTarget : ContentImageFailure.RedirectNotAllowed,
                        $"{url} is not an HTTPS URL without credentials.");
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, url)
                {
                    // HTTP/3 connects over QUIC, which does not pass through the ConnectCallback.
                    Version = HttpVersion.Version11,
                    VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
                };
                request.Headers.Accept.ParseAdd("image/png, image/jpeg, image/webp");
                request.Headers.AcceptEncoding.ParseAdd("identity");

                using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308))
                    return await ReadAsync(image, url, response, deadline.Token).ConfigureAwait(false);

                if (redirects == MaxRedirects)
                    return ContentImageResult.Failed(ContentImageFailure.RedirectNotAllowed, $"{image.Url} is reached through more than {MaxRedirects} redirects.");

                if (response.Headers.Location is not { } location)
                    return ContentImageResult.Failed(ContentImageFailure.RedirectNotAllowed, $"{url} answered HTTP {(int)response.StatusCode} with no Location.");

                url = location.IsAbsoluteUri ? location : new Uri(url, location);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ContentImageResult.Failed(ContentImageFailure.Unavailable, $"{image.Url} did not load within {_timeout.TotalSeconds} seconds.");
        }
        catch (Exception exception) when (FindBlockedTarget(exception) is { } blocked)
        {
            return ContentImageResult.Failed(ContentImageFailure.BlockedNetworkTarget, blocked.Message);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            return ContentImageResult.Failed(ContentImageFailure.Unavailable, $"{image.Url} did not answer: {exception.Message}");
        }
    }

    private static async Task<ContentImageResult> ReadAsync(ContentImage image, Uri url, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        if (status is 404 or 410)
            return ContentImageResult.Failed(ContentImageFailure.NotFound, $"{url} answered HTTP {status}, so the image is not there.");

        if (status is 408 or 425 or 429 or >= 500)
            return ContentImageResult.Failed(ContentImageFailure.Unavailable, $"{url} answered HTTP {status}.");

        if (status != 200)
            return ContentImageResult.Failed(ContentImageFailure.NotFound, $"{url} answered HTTP {status}.");

        var encodings = response.Content.Headers.ContentEncoding;
        if (encodings.Any(encoding => !string.Equals(encoding, "identity", StringComparison.OrdinalIgnoreCase)))
            return ContentImageResult.Failed(ContentImageFailure.UnsupportedFormat, $"{url} is served with Content-Encoding {string.Join(", ", encodings)}, not as the image bytes.");

        var cap = ContentImageBytes.MaxBytes(image);
        if (response.Content.Headers.ContentLength is { } announced && announced > cap)
            return ContentImageResult.Failed(ContentImageFailure.TooLarge, $"{url} announces {announced} bytes, above the cap of {cap}.");

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var received = new MemoryStream();
        var chunk = new byte[ChunkSize];
        int read;
        while ((read = await body.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, cap + 1 - received.Length)), cancellationToken).ConfigureAwait(false)) > 0)
        {
            received.Write(chunk, 0, read);
            if (received.Length > cap)
                return ContentImageResult.Failed(ContentImageFailure.TooLarge, $"{url} sends more than the cap of {cap} bytes.");
        }

        return ContentImageBytes.Verify(image, received.ToArray());
    }

    private static BlockedNetworkTargetException? FindBlockedTarget(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is BlockedNetworkTargetException blocked)
                return blocked;
        }

        return null;
    }

    public void Dispose() => _client.Dispose();
}
