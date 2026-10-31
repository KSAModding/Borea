using System.Runtime.InteropServices;

namespace Borea.Preview.Rendering;

/// <summary>
/// The target of the software renderer. <see cref="Rgba"/> holds four bytes per pixel in R, G, B, A
/// order with the top row first, which is the layout of an Avalonia Rgba8888 bitmap. <see cref="Depth"/>
/// holds the depth of the projection, 0 at the near plane and 1 at the far plane or where nothing is drawn.
/// </summary>
public sealed class FrameBuffer
{
    /// <summary>
    /// The largest width or height. It keeps the fixed-point edge math of the renderer inside 64-bit
    /// integers, also for vertices far outside the view.
    /// </summary>
    public const int MaxSize = 8192;

    /// <exception cref="ArgumentOutOfRangeException">A side is below 1 or above <see cref="MaxSize"/>.</exception>
    public FrameBuffer(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MaxSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, MaxSize);

        Width = width;
        Height = height;
        Rgba = new byte[width * height * 4];
        Depth = new float[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Rgba { get; }

    public float[] Depth { get; }

    public void Clear(Rgba8 color)
    {
        // The four bytes are read back as one value in memory order, so the fill is correct for any byte order.
        ReadOnlySpan<byte> pixel = [color.R, color.G, color.B, color.A];
        MemoryMarshal.Cast<byte, uint>(Rgba.AsSpan()).Fill(MemoryMarshal.Read<uint>(pixel));
        Depth.AsSpan().Fill(1f);
    }
}
