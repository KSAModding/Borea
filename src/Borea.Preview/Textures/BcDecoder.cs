namespace Borea.Preview.Textures;

/// <summary>
/// Decodes block-compressed texture levels to RGBA8, four bytes per pixel with the top row first.
/// The block layouts follow the BC1, BC4 and BC5 sections of the Khronos Data Format
/// Specification. Interpolated values are rounded to the nearest integer. GPUs may differ from this
/// by one step, which does not matter for a preview.
/// </summary>
public static class BcDecoder
{
    /// <summary>
    /// Decodes one level. Gives false when the format has no decoder here, when the size is not
    /// positive or too large, or when <paramref name="data"/> does not hold exactly the blocks of the size.
    /// </summary>
    public static bool TryDecode(Ktx2PixelFormat format, ReadOnlySpan<byte> data, int width, int height, out byte[]? rgba)
    {
        rgba = null;
        if (width <= 0 || height <= 0 || (long)width * height * 4 > Array.MaxLength)
        {
            return false;
        }

        if (data.Length != Ktx2Reader.LevelByteCount(format, width, height))
        {
            return false;
        }

        switch (format)
        {
            case Ktx2PixelFormat.Rgba8:
                rgba = data.ToArray();
                return true;
            case Ktx2PixelFormat.Bc1Rgb:
            case Ktx2PixelFormat.Bc1Rgba:
                rgba = DecodeBlocks(data, width, height, 8, format == Ktx2PixelFormat.Bc1Rgba ? DecodeBc1RgbaBlock : DecodeBc1RgbBlock);
                return true;
            case Ktx2PixelFormat.Bc4:
                rgba = DecodeBlocks(data, width, height, 8, DecodeBc4Block);
                return true;
            case Ktx2PixelFormat.Bc5:
                rgba = DecodeBlocks(data, width, height, 16, DecodeBc5Block);
                return true;
            default:
                return false;
        }
    }

    private delegate void BlockDecoder(ReadOnlySpan<byte> block, Span<byte> texels);

    private static byte[] DecodeBlocks(ReadOnlySpan<byte> data, int width, int height, int blockBytes, BlockDecoder decodeBlock)
    {
        var rgba = new byte[width * height * 4];
        Span<byte> texels = stackalloc byte[64];
        var blocksX = (width + 3) / 4;
        var blocksY = (height + 3) / 4;
        for (var by = 0; by < blocksY; by++)
        {
            for (var bx = 0; bx < blocksX; bx++)
            {
                decodeBlock(data.Slice(((by * blocksX) + bx) * blockBytes, blockBytes), texels);

                // The last row and column of blocks can reach past the level, and those texels are dropped.
                var rows = Math.Min(4, height - (by * 4));
                var columns = Math.Min(4, width - (bx * 4));
                for (var y = 0; y < rows; y++)
                {
                    texels.Slice(y * 16, columns * 4).CopyTo(rgba.AsSpan((((by * 4) + y) * width + (bx * 4)) * 4));
                }
            }
        }

        return rgba;
    }

    private static void DecodeBc1RgbBlock(ReadOnlySpan<byte> block, Span<byte> texels) => DecodeBc1Block(block, texels, transparentBlack: false);

    private static void DecodeBc1RgbaBlock(ReadOnlySpan<byte> block, Span<byte> texels) => DecodeBc1Block(block, texels, transparentBlack: true);

    private static void DecodeBc1Block(ReadOnlySpan<byte> block, Span<byte> texels, bool transparentBlack)
    {
        var color0 = (ushort)(block[0] | (block[1] << 8));
        var color1 = (ushort)(block[2] | (block[3] << 8));
        Span<byte> palette = stackalloc byte[16];
        Expand565(color0, palette[..4]);
        Expand565(color1, palette[4..8]);
        for (var channel = 0; channel < 3; channel++)
        {
            int a = palette[channel];
            int b = palette[4 + channel];
            if (color0 > color1)
            {
                palette[8 + channel] = (byte)(((2 * a) + b + 1) / 3);
                palette[12 + channel] = (byte)((a + (2 * b) + 1) / 3);
            }
            else
            {
                // The 3-color mode has the mean as the third color and black as the fourth.
                palette[8 + channel] = (byte)((a + b + 1) / 2);
                palette[12 + channel] = 0;
            }
        }

        palette[11] = 255;
        palette[15] = (byte)(color0 <= color1 && transparentBlack ? 0 : 255);

        var indices = (uint)(block[4] | (block[5] << 8) | (block[6] << 16) | (block[7] << 24));
        for (var texel = 0; texel < 16; texel++)
        {
            var index = (int)((indices >> (2 * texel)) & 3);
            palette.Slice(index * 4, 4).CopyTo(texels.Slice(texel * 4, 4));
        }
    }

    private static void Expand565(ushort color, Span<byte> rgba)
    {
        var r = (color >> 11) & 31;
        var g = (color >> 5) & 63;
        var b = color & 31;
        rgba[0] = (byte)((r << 3) | (r >> 2));
        rgba[1] = (byte)((g << 2) | (g >> 4));
        rgba[2] = (byte)((b << 3) | (b >> 2));
        rgba[3] = 255;
    }

    /// <summary>BC4 has one channel, which goes to R. G and B are 0 and A is 255, as Vulkan samples a red-only format.</summary>
    private static void DecodeBc4Block(ReadOnlySpan<byte> block, Span<byte> texels)
    {
        Span<byte> values = stackalloc byte[16];
        DecodeChannel(block, values);
        for (var texel = 0; texel < 16; texel++)
        {
            texels[texel * 4] = values[texel];
            texels[(texel * 4) + 1] = 0;
            texels[(texel * 4) + 2] = 0;
            texels[(texel * 4) + 3] = 255;
        }
    }

    /// <summary>
    /// BC5 is two BC4 channels for R and G. B is 255, so that a two-channel normal map reads as a
    /// normal that points out of the surface when only R, G and B are used, and A is 255.
    /// </summary>
    private static void DecodeBc5Block(ReadOnlySpan<byte> block, Span<byte> texels)
    {
        Span<byte> red = stackalloc byte[16];
        Span<byte> green = stackalloc byte[16];
        DecodeChannel(block[..8], red);
        DecodeChannel(block[8..], green);
        for (var texel = 0; texel < 16; texel++)
        {
            texels[texel * 4] = red[texel];
            texels[(texel * 4) + 1] = green[texel];
            texels[(texel * 4) + 2] = 255;
            texels[(texel * 4) + 3] = 255;
        }
    }

    /// <summary>One unsigned BC4 channel block: two end values and sixteen 3-bit indices.</summary>
    private static void DecodeChannel(ReadOnlySpan<byte> block, Span<byte> values)
    {
        int a = block[0];
        int b = block[1];
        Span<byte> palette = stackalloc byte[8];
        palette[0] = (byte)a;
        palette[1] = (byte)b;
        if (a > b)
        {
            for (var step = 1; step < 7; step++)
            {
                palette[1 + step] = (byte)((((7 - step) * a) + (step * b) + 3) / 7);
            }
        }
        else
        {
            // The 6-value mode keeps the two last indices for the ends of the range.
            for (var step = 1; step < 5; step++)
            {
                palette[1 + step] = (byte)((((5 - step) * a) + (step * b) + 2) / 5);
            }

            palette[6] = 0;
            palette[7] = 255;
        }

        ulong indices = 0;
        for (var i = 0; i < 6; i++)
        {
            indices |= (ulong)block[2 + i] << (8 * i);
        }

        for (var texel = 0; texel < 16; texel++)
        {
            values[texel] = palette[(int)((indices >> (3 * texel)) & 7)];
        }
    }
}
