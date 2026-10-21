namespace Borea.Preview.Scene;

/// <summary>
/// A decoded texture level with four bytes per pixel in R, G, B, A order. The first row is the top
/// row, which is the UV origin of glTF and KTX2.
/// </summary>
public sealed class PreviewImage
{
    /// <exception cref="ArgumentException">The size is not positive or the pixel bytes do not match it.</exception>
    public PreviewImage(int width, int height, byte[] rgba)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException($"The size {width}x{height} is not positive.");
        }

        if ((long)width * height * 4 != rgba.Length)
        {
            throw new ArgumentException($"{rgba.Length} bytes do not hold {width}x{height} RGBA pixels.", nameof(rgba));
        }

        Width = width;
        Height = height;
        Rgba = rgba;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Rgba { get; }
}
