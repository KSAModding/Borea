using System.Net;
using System.Net.Http.Headers;

namespace Borea.Network.Tests.Index;

/// <summary>
/// HttpContent that writes a truncated body then fails, simulating a
/// connection dropping partway through a download.
/// </summary>
internal sealed class FaultyContent : HttpContent
{
    private readonly byte[] _partialBytes;

    public FaultyContent(byte[] partialBytes, string mediaType)
    {
        _partialBytes = partialBytes;
        Headers.ContentType = new MediaTypeHeaderValue(mediaType);
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        await stream.WriteAsync(_partialBytes);
        await stream.FlushAsync();
        throw new IOException("Simulated connection drop mid-download.");
    }

    protected override bool TryComputeLength(out long length)
    {
        length = -1;
        return false;
    }
}
