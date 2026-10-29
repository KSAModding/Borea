namespace Borea.Preview.Rendering;

/// <summary>
/// Encodes a rendered frame as a PNG with straight alpha. The App gives the platform encoder, so that Borea.Preview
/// needs no image package of its own.
/// </summary>
public interface IPngEncoder
{
    /// <summary>Encodes the <see cref="FrameBuffer.Rgba"/> pixels of <paramref name="frame"/>, top row first.</summary>
    byte[] Encode(FrameBuffer frame);
}
