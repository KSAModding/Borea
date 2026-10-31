using System.Buffers.Binary;
using System.Numerics;

namespace Borea.Preview.Textures;

/// <summary>
/// Decodes one 16-byte BC7 block to sixteen RGBA8 texels, as the BPTC section of the Khronos Data
/// Format Specification 1.3 describes it. The game stores its diffuse and AoRoughMetal atlases in
/// this format (vkFormat 145 and 146, which BcDecoder dispatches here). The sRGB variant has the
/// same block bytes, so this decoder gives the stored values for both.
/// </summary>
internal static class Bc7Decoder
{
    /// <summary>Decodes <paramref name="block"/> into <paramref name="texels"/>, 64 bytes in row order.</summary>
    public static void DecodeBlock(ReadOnlySpan<byte> block, Span<byte> texels)
    {
        // The mode is the count of zero bits below the lowest one bit. A first byte of zero is
        // reserved, and the specification asks for zero in all channels then.
        if (block[0] == 0)
        {
            texels[..64].Clear();
            return;
        }

        var modeNumber = BitOperations.TrailingZeroCount(block[0]);
        var mode = Bc7Tables.Modes[modeNumber];
        var bits = new BitReader(block, modeNumber + 1);

        var partition = bits.Read(mode.PartitionBits);
        var rotation = bits.Read(mode.RotationBits);
        var indexSelection = bits.Read(mode.IndexSelectionBits);

        // The endpoints are stored channel by channel, and in each channel subset by subset with
        // two endpoints each. endpoints[(e * 4) + c] holds channel c of endpoint e.
        var endpointCount = mode.Subsets * 2;
        Span<int> endpoints = stackalloc int[24];
        for (var channel = 0; channel < 3; channel++)
        {
            for (var endpoint = 0; endpoint < endpointCount; endpoint++)
            {
                endpoints[(endpoint * 4) + channel] = bits.Read(mode.ColorBits);
            }
        }

        if (mode.AlphaBits > 0)
        {
            for (var endpoint = 0; endpoint < endpointCount; endpoint++)
            {
                endpoints[(endpoint * 4) + 3] = bits.Read(mode.AlphaBits);
            }
        }

        // A P-bit goes below the stored bits of every channel of its endpoint, alpha included.
        // A shared P-bit applies to both endpoints of its subset.
        Span<int> pBits = stackalloc int[6];
        var hasPBits = mode.EndpointPBits || mode.SharedPBits;
        if (mode.EndpointPBits)
        {
            for (var endpoint = 0; endpoint < endpointCount; endpoint++)
            {
                pBits[endpoint] = bits.Read(1);
            }
        }
        else if (mode.SharedPBits)
        {
            for (var subset = 0; subset < mode.Subsets; subset++)
            {
                pBits[(subset * 2) + 1] = pBits[subset * 2] = bits.Read(1);
            }
        }

        for (var endpoint = 0; endpoint < endpointCount; endpoint++)
        {
            for (var channel = 0; channel < 4; channel++)
            {
                var stored = channel < 3 ? mode.ColorBits : mode.AlphaBits;
                ref var value = ref endpoints[(endpoint * 4) + channel];
                if (stored == 0)
                {
                    value = 255;
                    continue;
                }

                var precision = stored;
                if (hasPBits)
                {
                    value = (value << 1) | pBits[endpoint];
                    precision++;
                }

                // The value fills the high bits of the byte, and its top bits repeat in the rest.
                value <<= 8 - precision;
                value |= value >> precision;
            }
        }

        // The index of each texel has the bit count of the mode, except at the anchor texel of each
        // subset, where the top bit is always zero and is not stored.
        var partitionTable = mode.Subsets switch
        {
            2 => Bc7Tables.TwoSubsetPartitions.Slice(partition * 16, 16),
            3 => Bc7Tables.ThreeSubsetPartitions.Slice(partition * 16, 16),
            _ => default,
        };
        var anchor1 = mode.Subsets switch
        {
            2 => Bc7Tables.TwoSubsetAnchors[partition],
            3 => Bc7Tables.ThreeSubsetSecondAnchors[partition],
            _ => -1,
        };
        var anchor2 = mode.Subsets == 3 ? Bc7Tables.ThreeSubsetThirdAnchors[partition] : -1;

        Span<byte> primary = stackalloc byte[16];
        for (var texel = 0; texel < 16; texel++)
        {
            var isAnchor = texel == 0 || texel == anchor1 || texel == anchor2;
            primary[texel] = (byte)bits.Read(isAnchor ? mode.IndexBits - 1 : mode.IndexBits);
        }

        // Modes 4 and 5 have a second index set for one subset, with texel 0 as its anchor.
        Span<byte> secondary = stackalloc byte[16];
        if (mode.SecondaryIndexBits > 0)
        {
            for (var texel = 0; texel < 16; texel++)
            {
                secondary[texel] = (byte)bits.Read(texel == 0 ? mode.SecondaryIndexBits - 1 : mode.SecondaryIndexBits);
            }
        }

        // Color takes the secondary set only when the index selection bit is one. Alpha takes the
        // secondary set whenever the mode has one and that bit is zero.
        var hasSecondary = mode.SecondaryIndexBits > 0;
        var colorFromSecondary = hasSecondary && indexSelection == 1;
        var alphaFromSecondary = hasSecondary && indexSelection == 0;
        var colorWeights = Weights(colorFromSecondary ? mode.SecondaryIndexBits : mode.IndexBits);
        var alphaWeights = Weights(alphaFromSecondary ? mode.SecondaryIndexBits : mode.IndexBits);

        for (var texel = 0; texel < 16; texel++)
        {
            var subset = partitionTable.IsEmpty ? 0 : partitionTable[texel];
            var e0 = endpoints.Slice(subset * 8, 4);
            var e1 = endpoints.Slice((subset * 8) + 4, 4);
            var colorWeight = colorWeights[colorFromSecondary ? secondary[texel] : primary[texel]];
            var alphaWeight = alphaWeights[alphaFromSecondary ? secondary[texel] : primary[texel]];

            var output = texels.Slice(texel * 4, 4);
            output[0] = Interpolate(e0[0], e1[0], colorWeight);
            output[1] = Interpolate(e0[1], e1[1], colorWeight);
            output[2] = Interpolate(e0[2], e1[2], colorWeight);
            output[3] = Interpolate(e0[3], e1[3], alphaWeight);

            // The rotation swaps alpha with one color channel after the interpolation.
            if (rotation != 0)
            {
                (output[rotation - 1], output[3]) = (output[3], output[rotation - 1]);
            }
        }
    }

    private static ReadOnlySpan<byte> Weights(int indexBits) => indexBits switch
    {
        2 => Bc7Tables.Weights2,
        3 => Bc7Tables.Weights3,
        _ => Bc7Tables.Weights4,
    };

    private static byte Interpolate(int e0, int e1, int weight) => (byte)((((64 - weight) * e0) + (weight * e1) + 32) >> 6);

    /// <summary>Reads fields from the 128 bits of a block, lowest bit first.</summary>
    private ref struct BitReader(ReadOnlySpan<byte> block, int position)
    {
        private readonly ulong _low = BinaryPrimitives.ReadUInt64LittleEndian(block);
        private readonly ulong _high = BinaryPrimitives.ReadUInt64LittleEndian(block[8..]);
        private int _position = position;

        /// <summary>Reads <paramref name="count"/> bits, at most 8. A count of zero gives zero.</summary>
        public int Read(int count)
        {
            ulong value;
            if (_position >= 64)
            {
                value = _high >> (_position - 64);
            }
            else if (_position + count <= 64)
            {
                value = _low >> _position;
            }
            else
            {
                value = (_low >> _position) | (_high << (64 - _position));
            }

            _position += count;
            return (int)(value & ((1UL << count) - 1));
        }
    }
}
