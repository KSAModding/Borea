using System.Diagnostics;
using Borea.Preview.Textures;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using Xunit.Abstractions;
using ReferenceDecoder = BCnEncoder.Decoder.BcDecoder;

namespace Borea.Preview.Tests.Textures;

public sealed class Bc7DecoderTests(ITestOutputHelper output)
{
    [Fact]
    public void AMode6Block_DecodesWithItsPBitsAndFourBitWeights()
    {
        // Endpoint 0 has P-bit 1 and endpoint 1 has P-bit 0, which gives the 8-bit endpoints
        // (255, 1, 129, 255) and (0, 254, 64, 126). Texel i has index i.
        var block = new Bc7BlockWriter()
            .Bits(1 << 6, 7)
            .Bits(127, 7).Bits(0, 7).Bits(0, 7).Bits(127, 7).Bits(64, 7).Bits(32, 7).Bits(127, 7).Bits(63, 7)
            .Bits(1, 1).Bits(0, 1)
            .Indices(Enumerable.Range(0, 16).ToArray(), 4, anchors: [0])
            .ToArray();

        byte[] expected =
        [
            255, 1, 129, 255, 239, 17, 125, 247, 219, 37, 120, 237, 203, 52, 116, 229,
            187, 68, 112, 221, 171, 84, 108, 213, 151, 104, 103, 203, 135, 120, 99, 195,
            120, 135, 94, 186, 104, 151, 90, 178, 84, 171, 85, 168, 68, 187, 81, 160,
            52, 203, 77, 152, 36, 218, 73, 144, 16, 238, 68, 134, 0, 254, 64, 126,
        ];
        Assert.Equal(expected, DecodeBlock(block));
    }

    [Fact]
    public void AMode5Block_SwapsAlphaAndRedAfterItsRotation()
    {
        // Rotation 1 swaps A and R. The color endpoints are (255, 0, 129) and (0, 255, 129), the
        // alpha endpoints are 0 and 200. The color index of texel i is i mod 4 and its alpha index
        // is its row, so after the swap R follows the row and A follows the column.
        var block = new Bc7BlockWriter()
            .Bits(1 << 5, 6)
            .Bits(1, 2)
            .Bits(127, 7).Bits(0, 7).Bits(0, 7).Bits(127, 7).Bits(64, 7).Bits(64, 7)
            .Bits(0, 8).Bits(200, 8)
            .Indices(Enumerable.Range(0, 16).Select(texel => texel % 4).ToArray(), 2, anchors: [0])
            .Indices(Enumerable.Range(0, 16).Select(texel => texel / 4).ToArray(), 2, anchors: [0])
            .ToArray();

        byte[] row = [0, 0, 129, 255, 0, 84, 129, 171, 0, 171, 129, 84, 0, 255, 129, 0];
        var expected = new[] { 0, 66, 134, 200 }.SelectMany(red => row.Select((value, i) => i % 4 == 0 ? (byte)red : value)).ToArray();
        Assert.Equal(expected, DecodeBlock(block));
    }

    [Fact]
    public void AMode1BlockWithPartition17_UsesItsSubsetsAndItsAnchorAtTexel2()
    {
        // In partition 17, texels 1, 2, 3 and 7 are in subset 1, whose anchor is texel 2 and not
        // texel 1, so texel 2 has a 2-bit index. Subset 0 goes from black to (253, 253, 253) with
        // shared P-bit 0, and subset 1 goes from (255, 2, 131) to (2, 2, 131) with shared P-bit 1.
        // Texel i has index i mod 8.
        var block = new Bc7BlockWriter()
            .Bits(0b10, 2)
            .Bits(17, 6)
            .Bits(0, 6).Bits(63, 6).Bits(63, 6).Bits(0, 6)
            .Bits(0, 6).Bits(63, 6).Bits(0, 6).Bits(0, 6)
            .Bits(0, 6).Bits(63, 6).Bits(32, 6).Bits(32, 6)
            .Bits(0, 1).Bits(1, 1)
            .Indices(Enumerable.Range(0, 16).Select(texel => texel % 8).ToArray(), 3, anchors: [0, 2])
            .ToArray();

        byte[] expected =
        [
            0, 0, 0, 255, 219, 2, 131, 255, 184, 2, 131, 255, 148, 2, 131, 255,
            146, 146, 146, 255, 182, 182, 182, 255, 217, 217, 217, 255, 2, 2, 131, 255,
            0, 0, 0, 255, 36, 36, 36, 255, 71, 71, 71, 255, 107, 107, 107, 255,
            146, 146, 146, 255, 182, 182, 182, 255, 217, 217, 217, 255, 253, 253, 253, 255,
        ];
        Assert.Equal(expected, DecodeBlock(block));
    }

    [Fact]
    public void AReservedBlock_IsTransparentBlack()
    {
        byte[] block = [0, .. Enumerable.Repeat((byte)0xFF, 15)];

        Assert.Equal(new byte[64], DecodeBlock(block));
    }

    [Fact]
    public void AnImageEncodedByTheReferenceLibrary_DecodesLikeItsOwnDecoder()
    {
        const int Size = 64;
        var image = GenerateImage(Size);
        var encoder = new BcEncoder(CompressionFormat.Bc7);
        encoder.OutputOptions.GenerateMipMaps = false;
        encoder.OutputOptions.Quality = CompressionQuality.BestQuality;
        var blocks = encoder.EncodeToRawBytes(image, Size, Size, PixelFormat.Rgba32, 0, out _, out _);

        var modes = Enumerable.Range(0, blocks.Length / 16)
            .Select(block => blocks[block * 16])
            .Where(first => first != 0)
            .Select(first => System.Numerics.BitOperations.TrailingZeroCount(first))
            .Distinct()
            .ToArray();
        output.WriteLine($"Modes in the encoded image: {string.Join(", ", modes.Order())}");
        Assert.True(modes.Length >= 4, $"Only the modes {string.Join(", ", modes)} occurred.");

        var reference = new ReferenceDecoder().DecodeRaw(blocks, Size, Size, CompressionFormat.Bc7);
        var expected = reference.SelectMany(color => new[] { color.r, color.g, color.b, color.a }).ToArray();
        Assert.True(BcDecoder.TryDecode(Ktx2PixelFormat.Bc7, blocks, Size, Size, out var rgba));
        Assert.Equal(expected, rgba);
    }

    [Fact]
    public void RandomBlocksOfEveryMode_DecodeLikeTheReferenceDecoder()
    {
        // The encoder above does not choose every mode, so random blocks cover all eight modes, all
        // partitions and both P-bit kinds. The 4096 blocks of a 256x256 image give 512 blocks per
        // mode, so every one of the 64 partitions occurs with all but a negligible chance.
        const int Size = 256;
        var random = new Random(11);
        var blocks = new byte[Size * Size];
        random.NextBytes(blocks);
        for (var block = 0; block < blocks.Length / 16; block++)
        {
            var mode = block % 8;
            blocks[block * 16] = (byte)((blocks[block * 16] << (mode + 1)) | (1 << mode));
        }

        var reference = new ReferenceDecoder().DecodeRaw(blocks, Size, Size, CompressionFormat.Bc7);
        var expected = reference.SelectMany(color => new[] { color.r, color.g, color.b, color.a }).ToArray();
        Assert.True(BcDecoder.TryDecode(Ktx2PixelFormat.Bc7, blocks, Size, Size, out var rgba));
        Assert.Equal(expected, rgba);
    }

    [GameDirFact]
    public void TheSmallLevelOfTheCoreFuelTankDiffuse_DecodesToAnImageThatIsNotUniform()
    {
        var path = Path.Combine(GameDirFactAttribute.GameDir!, "Content", "Core", "Textures", "CoreFuelTankA_TextureAtlas_Diffuse.ktx2");
        using var stream = File.OpenRead(path);
        var header = Ktx2Reader.ReadHeader(stream).Value!;
        var level = Ktx2Reader.ReadLevel(stream, header, Ktx2Reader.ChooseLevel(header, 512)).Value!;
        Assert.Equal(Ktx2PixelFormat.Bc7, level.Format);

        var watch = Stopwatch.StartNew();
        Assert.True(BcDecoder.TryDecode(level.Format, level.Data, level.Width, level.Height, out var rgba));
        output.WriteLine($"Decoded the {level.Width}x{level.Height} BC7 level in {watch.Elapsed.TotalMilliseconds:F1} ms.");

        var distinct = new HashSet<int>();
        for (var pixel = 0; pixel < rgba!.Length; pixel += 4)
        {
            distinct.Add(BitConverter.ToInt32(rgba, pixel));
        }

        Assert.True(distinct.Count > 100, $"The level has only {distinct.Count} distinct colors.");
    }

    private static byte[] DecodeBlock(byte[] block)
    {
        Assert.True(BcDecoder.TryDecode(Ktx2PixelFormat.Bc7, block, 4, 4, out var rgba));
        return rgba!;
    }

    /// <summary>
    /// An RGBA image whose 4x4 blocks differ in kind: smooth gradients, hard edges between two or
    /// three colors, noise, and alpha that varies with or apart from the color. The encoder picks
    /// different modes for these blocks.
    /// </summary>
    private static byte[] GenerateImage(int size)
    {
        var random = new Random(7);
        var image = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var kind = ((x / 4) + (y / 4 * 3)) % 6;
                var pixel = image.AsSpan(((y * size) + x) * 4, 4);
                switch (kind)
                {
                    case 0:
                        pixel[0] = (byte)(x * 4);
                        pixel[1] = (byte)(y * 4);
                        pixel[2] = 90;
                        pixel[3] = 255;
                        break;
                    case 1:
                        // Three flat colors split by the diagonals of the block.
                        var region = ((x % 4) + (y % 4)) switch { < 2 => 0, < 5 => 1, _ => 2 };
                        pixel[0] = (byte)(region == 0 ? 230 : 20);
                        pixel[1] = (byte)(region == 1 ? 210 : 30);
                        pixel[2] = (byte)(region == 2 ? 240 : 10);
                        pixel[3] = 255;
                        break;
                    case 2:
                        random.NextBytes(pixel);
                        break;
                    case 3:
                        pixel[0] = 200;
                        pixel[1] = 120;
                        pixel[2] = 40;
                        pixel[3] = (byte)((x % 4 * 60) + (y % 4 * 5));
                        break;
                    case 4:
                        pixel[0] = (byte)(x % 4 < 2 ? 255 : 0);
                        pixel[1] = (byte)(x % 4 < 2 ? 0 : 255);
                        pixel[2] = (byte)(y * 3);
                        pixel[3] = (byte)(y % 4 < 2 ? 255 : 64);
                        break;
                    default:
                        var gray = (byte)random.Next(100, 140);
                        pixel[0] = gray;
                        pixel[1] = gray;
                        pixel[2] = gray;
                        pixel[3] = (byte)random.Next(256);
                        break;
                }
            }
        }

        return image;
    }

    /// <summary>Writes the 128 bits of a BC7 block field by field, lowest bit first.</summary>
    private sealed class Bc7BlockWriter
    {
        private UInt128 _bits;
        private int _position;

        public Bc7BlockWriter Bits(int value, int count)
        {
            Assert.InRange(value, 0, (1 << count) - 1);
            _bits |= (UInt128)(uint)value << _position;
            _position += count;
            return this;
        }

        /// <summary>Writes one index per texel, with one bit less at each anchor texel.</summary>
        public Bc7BlockWriter Indices(int[] indices, int bits, int[] anchors)
        {
            for (var texel = 0; texel < 16; texel++)
            {
                Bits(indices[texel], anchors.Contains(texel) ? bits - 1 : bits);
            }

            return this;
        }

        public byte[] ToArray()
        {
            Assert.Equal(128, _position);
            var bytes = new byte[16];
            for (var i = 0; i < 16; i++)
            {
                bytes[i] = (byte)(_bits >> (8 * i));
            }

            return bytes;
        }
    }
}
