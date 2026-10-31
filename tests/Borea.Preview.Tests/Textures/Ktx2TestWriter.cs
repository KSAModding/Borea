using System.Buffers.Binary;
using ZstdSharp;

namespace Borea.Preview.Tests.Textures;

/// <summary>
/// Writes small KTX2 files for the tests: the header, the level index, and the levels with the
/// smallest level first, as the KTX2 specification orders them. There is no data format descriptor,
/// because the reader does not use it.
/// </summary>
internal static class Ktx2TestWriter
{
    public const int LevelIndexOffset = 80;

    public static byte[] Write(uint vkFormat, int width, int height, IReadOnlyList<byte[]> levels, uint scheme = 0, uint layers = 0, uint faces = 1)
    {
        var stored = levels.Select(level => scheme == 2 ? Compress(level) : level).ToArray();
        var dataStart = LevelIndexOffset + (24 * levels.Count);
        var file = new byte[dataStart + stored.Sum(level => level.Length)];
        var header = file.AsSpan();
        ReadOnlySpan<byte> identifier = [0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A];
        identifier.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], vkFormat);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header[20..], (uint)width);
        BinaryPrimitives.WriteUInt32LittleEndian(header[24..], (uint)height);
        BinaryPrimitives.WriteUInt32LittleEndian(header[32..], layers);
        BinaryPrimitives.WriteUInt32LittleEndian(header[36..], faces);
        BinaryPrimitives.WriteUInt32LittleEndian(header[40..], (uint)levels.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(header[44..], scheme);

        var offset = dataStart;
        for (var level = levels.Count - 1; level >= 0; level--)
        {
            stored[level].CopyTo(file, offset);
            var entry = header[(LevelIndexOffset + (24 * level))..];
            BinaryPrimitives.WriteUInt64LittleEndian(entry, (ulong)offset);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[8..], (ulong)stored[level].Length);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[16..], (ulong)levels[level].Length);
            offset += stored[level].Length;
        }

        return file;
    }

    public static byte[] Compress(byte[] data)
    {
        using var compressor = new Compressor();
        return compressor.Wrap(data).ToArray();
    }

    /// <summary>Overwrites one 64-bit field of a level index entry: 0 is the offset, 1 the length, 2 the uncompressed length.</summary>
    public static void SetLevelField(byte[] file, int level, int field, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(LevelIndexOffset + (24 * level) + (8 * field)), value);
}
