using Borea.Preview.Scene;

namespace Borea.Preview.Textures;

/// <summary>
/// Decodes PNG and JPEG textures to RGBA. The App gives the platform decoder, so that Borea.Preview needs no image
/// package of its own. <see cref="PreviewTextureLoader"/> asks for the size first and decodes only when the size is
/// inside its limits, so an implementation must read the size from the header without the pixels.
/// </summary>
public interface IRgbaImageDecoder
{
    /// <summary>
    /// Reads the pixel size from the header of the image at the start of <paramref name="stream"/> and does not decode
    /// the pixels. Gives false when the header is not a PNG or JPEG header that the decoder can read.
    /// </summary>
    /// <exception cref="IOException">The stream failed.</exception>
    bool TryReadSize(Stream stream, out int width, out int height);

    /// <summary>
    /// Decodes the image at the start of <paramref name="stream"/> to <paramref name="width"/> pixels across, with
    /// its aspect ratio kept. The first row of the result is the top row of the image.
    /// </summary>
    /// <exception cref="InvalidDataException">The data is not an image that the decoder can decode.</exception>
    /// <exception cref="IOException">The stream failed.</exception>
    PreviewImage Decode(Stream stream, int width);
}
