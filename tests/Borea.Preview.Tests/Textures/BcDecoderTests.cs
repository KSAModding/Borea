using Borea.Preview.Textures;

namespace Borea.Preview.Tests.Textures;

public sealed class BcDecoderTests
{
    private const ushort Red565 = 0xF800;
    private const ushort Blue565 = 0x001F;

    // Index 0, 1, 2, 3 in each row of the block.
    private static readonly byte[] CountingBc1Indices = [0xE4, 0xE4, 0xE4, 0xE4];

    [Fact]
    public void ABc1BlockInFourColorMode_DecodesItsPalette()
    {
        var block = Bc1Block(Red565, Blue565, CountingBc1Indices);

        var rgba = Decode(Ktx2PixelFormat.Bc1Rgba, block, 4, 4);

        byte[] row = [255, 0, 0, 255, 0, 0, 255, 255, 170, 0, 85, 255, 85, 0, 170, 255];
        Assert.Equal(Repeat(row, 4), rgba);
    }

    [Theory]
    [InlineData(Ktx2PixelFormat.Bc1Rgb, 255)]
    [InlineData(Ktx2PixelFormat.Bc1Rgba, 0)]
    public void ABc1BlockInThreeColorMode_HasTheMeanAndBlack(Ktx2PixelFormat format, byte blackAlpha)
    {
        var block = Bc1Block(Blue565, Red565, CountingBc1Indices);

        var rgba = Decode(format, block, 4, 4);

        byte[] row = [0, 0, 255, 255, 255, 0, 0, 255, 128, 0, 128, 255, 0, 0, 0, blackAlpha];
        Assert.Equal(Repeat(row, 4), rgba);
    }

    [Fact]
    public void Bc4Blocks_DecodeBothInterpolationModesIntoRed()
    {
        var eightValues = ChannelBlock(255, 0);
        var sixValues = ChannelBlock(0, 255);

        var eight = Decode(Ktx2PixelFormat.Bc4, eightValues, 4, 4);
        var six = Decode(Ktx2PixelFormat.Bc4, sixValues, 4, 4);

        Assert.Equal(Texels([255, 0, 219, 182, 146, 109, 73, 36], red => [red, 0, 0, 255]), eight);
        Assert.Equal(Texels([0, 255, 51, 102, 153, 204, 0, 255], red => [red, 0, 0, 255]), six);
    }

    [Fact]
    public void ABc5Block_DecodesRedAndGreen_WithOpaqueBlue()
    {
        var block = ChannelBlock(255, 0).Concat(ChannelBlock(0, 255)).ToArray();
        byte[] red = [255, 0, 219, 182, 146, 109, 73, 36];
        byte[] green = [0, 255, 51, 102, 153, 204, 0, 255];

        var rgba = Decode(Ktx2PixelFormat.Bc5, block, 4, 4);

        var expected = Enumerable.Range(0, 16).SelectMany(texel => new[] { red[texel % 8], green[texel % 8], (byte)255, (byte)255 });
        Assert.Equal(expected, rgba);
    }

    [Fact]
    public void ALevelWhoseSizeIsNotAMultipleOfFour_KeepsOnlyItsOwnTexels()
    {
        // Two solid blocks side by side, red and green, for a 5x3 level.
        var blocks = Bc1Block(Red565, 0, [0, 0, 0, 0]).Concat(Bc1Block(0x07E0, 0, [0, 0, 0, 0])).ToArray();

        var rgba = Decode(Ktx2PixelFormat.Bc1Rgb, blocks, 5, 3);

        byte[] row = [255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 0, 255, 0, 255];
        Assert.Equal(Repeat(row, 3), rgba);
    }

    [Fact]
    public void Rgba8_PassesThrough()
    {
        var data = Enumerable.Range(0, 2 * 3 * 4).Select(value => (byte)value).ToArray();

        Assert.Equal(data, Decode(Ktx2PixelFormat.Rgba8, data, 2, 3));
    }

    [Theory]
    [InlineData(Ktx2PixelFormat.Bc1Rgb, 7, 4, 4)]
    [InlineData(Ktx2PixelFormat.Bc1Rgb, 9, 4, 4)]
    [InlineData(Ktx2PixelFormat.Bc4, 8, 5, 4)]
    [InlineData(Ktx2PixelFormat.Bc5, 15, 4, 4)]
    [InlineData(Ktx2PixelFormat.Rgba8, 63, 4, 4)]
    [InlineData(Ktx2PixelFormat.Bc1Rgb, 8, 0, 4)]
    public void InputThatDoesNotFitTheSize_FailsWithoutAnException(Ktx2PixelFormat format, int length, int width, int height)
    {
        Assert.False(BcDecoder.TryDecode(format, new byte[length], width, height, out var rgba));
        Assert.Null(rgba);
    }

    private static byte[] Decode(Ktx2PixelFormat format, byte[] data, int width, int height)
    {
        Assert.True(BcDecoder.TryDecode(format, data, width, height, out var rgba));
        return rgba!;
    }

    private static byte[] Bc1Block(ushort color0, ushort color1, byte[] indices) =>
        [(byte)color0, (byte)(color0 >> 8), (byte)color1, (byte)(color1 >> 8), .. indices];

    /// <summary>A BC4 channel block whose texel i has the index i modulo 8.</summary>
    private static byte[] ChannelBlock(byte end0, byte end1)
    {
        ulong indices = 0;
        for (var texel = 0; texel < 16; texel++)
        {
            indices |= (ulong)(texel % 8) << (3 * texel);
        }

        return [end0, end1, .. BitConverter.GetBytes(indices).AsSpan(0, 6)];
    }

    private static byte[] Texels(byte[] palette, Func<byte, byte[]> texel) =>
        Enumerable.Range(0, 16).SelectMany(index => texel(palette[index % 8])).ToArray();

    private static byte[] Repeat(byte[] row, int count) => Enumerable.Repeat(row, count).SelectMany(bytes => bytes).ToArray();
}
