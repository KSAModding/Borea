using System.Buffers.Binary;
using ZstdSharp;

namespace Borea.Preview.Textures;

/// <summary>The outcome of a KTX2 read.</summary>
public enum Ktx2Status
{
    Ok,

    /// <summary>The file breaks the KTX2 layout, or a size in it does not match its format.</summary>
    Invalid,

    /// <summary>The file is valid KTX2, but the preview does not decode its kind of content.</summary>
    Unsupported,
}

/// <summary>A KTX2 read result. <see cref="Value"/> is set only when <see cref="Status"/> is Ok.</summary>
public sealed class Ktx2Result<T>
    where T : class
{
    private Ktx2Result(Ktx2Status status, T? value, string problem)
    {
        Status = status;
        Value = value;
        Problem = problem;
    }

    public Ktx2Status Status { get; }

    public T? Value { get; }

    /// <summary>Why the read failed, or an empty string when it did not.</summary>
    public string Problem { get; }

    internal static Ktx2Result<T> Ok(T value) => new(Ktx2Status.Ok, value, "");

    internal static Ktx2Result<T> Invalid(string problem) => new(Ktx2Status.Invalid, null, problem);

    internal static Ktx2Result<T> Unsupported(string problem) => new(Ktx2Status.Unsupported, null, problem);
}

/// <summary>The pixel formats that the preview can take from a KTX2 file.</summary>
public enum Ktx2PixelFormat
{
    /// <summary>BC1 without alpha, where the fourth color of the 3-color mode is opaque black.</summary>
    Bc1Rgb,

    /// <summary>BC1 with 1-bit alpha, where the fourth color of the 3-color mode is transparent black.</summary>
    Bc1Rgba,
    Bc4,
    Bc5,
    Bc7,
    Rgba8,
}

/// <summary>One entry of the KTX2 level index.</summary>
public readonly record struct Ktx2LevelIndex(long ByteOffset, long ByteLength, long UncompressedByteLength);

/// <summary>The header and the level index of a KTX2 file that the preview can read.</summary>
/// <param name="VkFormat">The Vulkan format number from the header.</param>
/// <param name="Srgb">The format stores sRGB color, so the decoded bytes are sRGB encoded.</param>
/// <param name="SupercompressionScheme">0 for none, 2 for Zstandard.</param>
public sealed record Ktx2Header(
    int VkFormat,
    Ktx2PixelFormat Format,
    bool Srgb,
    int Width,
    int Height,
    int SupercompressionScheme,
    IReadOnlyList<Ktx2LevelIndex> Levels)
{
    public int LevelWidth(int level) => Math.Max(1, Width >> level);

    public int LevelHeight(int level) => Math.Max(1, Height >> level);
}

/// <summary>The bytes of one level after supercompression is removed, in the format of the file.</summary>
public sealed record Ktx2Level(Ktx2PixelFormat Format, bool Srgb, int Level, int Width, int Height, byte[] Data);

/// <summary>
/// Reads KTX2 textures as the Khronos KTX 2.0 specification lays them out. The game loads its
/// .ktx2 files with libktx through Brutal.KtxApi (TextureReference.DoLoad), so the preview reads the
/// same container. It reads only the 2D content that the part textures use, one level at a time, and
/// it reads only the bytes of that level.
/// </summary>
public static class Ktx2Reader
{
    /// <summary>The largest level, compressed or not, that <see cref="ReadLevel"/> reads.</summary>
    public const int MaxLevelBytes = 16 * 1024 * 1024;

    private const int HeaderBytes = 80;
    private const int LevelIndexEntryBytes = 24;
    private const int MaxLevels = 32;
    private const int SchemeNone = 0;
    private const int SchemeBasisLz = 1;
    private const int SchemeZstd = 2;

    private static ReadOnlySpan<byte> Identifier => [0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Reads the header and the level index from the current position, which must be the start of
    /// the file. Truncated or inconsistent data gives Invalid, never an exception.
    /// </summary>
    /// <exception cref="IOException">The stream failed for another reason than its end.</exception>
    public static Ktx2Result<Ktx2Header> ReadHeader(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> header = stackalloc byte[HeaderBytes];
        try
        {
            stream.ReadExactly(header);
        }
        catch (EndOfStreamException)
        {
            return Ktx2Result<Ktx2Header>.Invalid("The file is shorter than the KTX2 header.");
        }

        if (!header[..12].SequenceEqual(Identifier))
        {
            return Ktx2Result<Ktx2Header>.Invalid("The file does not start with the KTX2 identifier.");
        }

        var vkFormat = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
        var width = BinaryPrimitives.ReadUInt32LittleEndian(header[20..]);
        var height = BinaryPrimitives.ReadUInt32LittleEndian(header[24..]);
        var depth = BinaryPrimitives.ReadUInt32LittleEndian(header[28..]);
        var layers = BinaryPrimitives.ReadUInt32LittleEndian(header[32..]);
        var faces = BinaryPrimitives.ReadUInt32LittleEndian(header[36..]);
        var levelCount = BinaryPrimitives.ReadUInt32LittleEndian(header[40..]);
        var scheme = BinaryPrimitives.ReadUInt32LittleEndian(header[44..]);

        if (width == 0 || width > int.MaxValue || height > int.MaxValue)
        {
            return Ktx2Result<Ktx2Header>.Invalid($"The size {width}x{height} is not valid.");
        }

        // A level count of 0 asks the loader to make the mip chain, and the file then holds level 0 only.
        var levels = (int)Math.Max(1u, Math.Min(levelCount, MaxLevels + 1u));
        if (levels > MaxLevels || levels > 32 - int.LeadingZeroCount((int)Math.Max(width, height)))
        {
            return Ktx2Result<Ktx2Header>.Invalid($"{levelCount} levels do not fit the size {width}x{height}.");
        }

        if (faces != 1 && faces != 6)
        {
            return Ktx2Result<Ktx2Header>.Invalid($"The face count {faces} is not 1 or 6.");
        }

        if (scheme == SchemeBasisLz || vkFormat == 0)
        {
            return Ktx2Result<Ktx2Header>.Unsupported("Basis Universal content (BasisLZ or UASTC) is not decoded.");
        }

        if (scheme != SchemeNone && scheme != SchemeZstd)
        {
            return Ktx2Result<Ktx2Header>.Unsupported($"The supercompression scheme {scheme} is not decoded.");
        }

        if (height == 0 || depth != 0)
        {
            return Ktx2Result<Ktx2Header>.Unsupported("Only 2D textures are decoded.");
        }

        if (layers > 1 || faces == 6)
        {
            return Ktx2Result<Ktx2Header>.Unsupported("Array textures and cubemaps are not decoded.");
        }

        if (!TryGetFormat(vkFormat, out var format, out var srgb))
        {
            return Ktx2Result<Ktx2Header>.Unsupported($"The Vulkan format {vkFormat} is not decoded.");
        }

        var index = new byte[levels * LevelIndexEntryBytes];
        try
        {
            stream.ReadExactly(index);
        }
        catch (EndOfStreamException)
        {
            return Ktx2Result<Ktx2Header>.Invalid("The file is shorter than its level index.");
        }

        var entries = new Ktx2LevelIndex[levels];
        for (var level = 0; level < levels; level++)
        {
            var entry = index.AsSpan(level * LevelIndexEntryBytes, LevelIndexEntryBytes);
            var offset = BinaryPrimitives.ReadUInt64LittleEndian(entry);
            var length = BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]);
            var uncompressed = BinaryPrimitives.ReadUInt64LittleEndian(entry[16..]);
            if (offset > long.MaxValue || length > long.MaxValue || uncompressed > long.MaxValue)
            {
                return Ktx2Result<Ktx2Header>.Invalid($"The level index entry of level {level} is out of range.");
            }

            entries[level] = new Ktx2LevelIndex((long)offset, (long)length, (long)uncompressed);
        }

        return Ktx2Result<Ktx2Header>.Ok(new Ktx2Header((int)vkFormat, format, srgb, (int)width, (int)height, (int)scheme, entries));
    }

    /// <summary>
    /// The largest level whose larger side is at most <paramref name="target"/> pixels, else the
    /// smallest level of the file. This follows the idea of the ClosestMipLevel reduction that
    /// TextureReference.Bind selects for block-compressed textures, without its GPU upload.
    /// </summary>
    public static int ChooseLevel(Ktx2Header header, int target)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentOutOfRangeException.ThrowIfLessThan(target, 1);
        for (var level = 0; level < header.Levels.Count; level++)
        {
            if (Math.Max(header.LevelWidth(level), header.LevelHeight(level)) <= target)
            {
                return level;
            }
        }

        return header.Levels.Count - 1;
    }

    /// <summary>
    /// Seeks to one level and reads only its bytes, then removes the Zstandard supercompression when
    /// the file has it. The result must have exactly the size that the format gives for the level.
    /// </summary>
    /// <exception cref="ArgumentException">The stream cannot seek.</exception>
    /// <exception cref="IOException">The stream failed for another reason than its end.</exception>
    public static Ktx2Result<Ktx2Level> ReadLevel(Stream stream, Ktx2Header header, int level)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(level, header.Levels.Count);
        if (!stream.CanSeek)
        {
            throw new ArgumentException("The stream must be able to seek.", nameof(stream));
        }

        var entry = header.Levels[level];
        var width = header.LevelWidth(level);
        var height = header.LevelHeight(level);
        var expected = LevelByteCount(header.Format, width, height);
        if (entry.UncompressedByteLength != expected)
        {
            return Ktx2Result<Ktx2Level>.Invalid($"Level {level} declares {entry.UncompressedByteLength} bytes, but {width}x{height} {header.Format} needs {expected}.");
        }

        if (expected > MaxLevelBytes || entry.ByteLength > MaxLevelBytes)
        {
            return Ktx2Result<Ktx2Level>.Unsupported($"Level {level} is larger than {MaxLevelBytes} bytes.");
        }

        if (header.SupercompressionScheme == SchemeNone && entry.ByteLength != expected)
        {
            return Ktx2Result<Ktx2Level>.Invalid($"Level {level} stores {entry.ByteLength} bytes, but {width}x{height} {header.Format} needs {expected}.");
        }

        if (entry.ByteLength == 0 || entry.ByteOffset > stream.Length - entry.ByteLength)
        {
            return Ktx2Result<Ktx2Level>.Invalid($"Level {level} lies outside the file.");
        }

        var stored = new byte[entry.ByteLength];
        try
        {
            stream.Seek(entry.ByteOffset, SeekOrigin.Begin);
            stream.ReadExactly(stored);
        }
        catch (EndOfStreamException)
        {
            return Ktx2Result<Ktx2Level>.Invalid($"The file ends inside level {level}.");
        }

        var data = stored;
        if (header.SupercompressionScheme == SchemeZstd)
        {
            data = new byte[expected];
            try
            {
                using var decompressor = new Decompressor();
                if (decompressor.Unwrap(stored, data) != expected)
                {
                    return Ktx2Result<Ktx2Level>.Invalid($"Level {level} inflates to fewer bytes than {expected}.");
                }
            }
            catch (ZstdException exception)
            {
                // A frame that inflates to more than the declared size also ends here, because the
                // destination is exactly as large as the declared size.
                return Ktx2Result<Ktx2Level>.Invalid($"Level {level} is not valid Zstandard data for {expected} bytes: {exception.Message}");
            }
        }

        return Ktx2Result<Ktx2Level>.Ok(new Ktx2Level(header.Format, header.Srgb, level, width, height, data));
    }

    /// <summary>The bytes of one level in <paramref name="format"/>, with partial 4x4 blocks counted whole.</summary>
    public static long LevelByteCount(Ktx2PixelFormat format, int width, int height)
    {
        if (format == Ktx2PixelFormat.Rgba8)
        {
            return (long)width * height * 4;
        }

        var blockBytes = format is Ktx2PixelFormat.Bc1Rgb or Ktx2PixelFormat.Bc1Rgba or Ktx2PixelFormat.Bc4 ? 8 : 16;
        return ((width + 3L) / 4) * ((height + 3L) / 4) * blockBytes;
    }

    private static bool TryGetFormat(uint vkFormat, out Ktx2PixelFormat format, out bool srgb)
    {
        // The numbers are the VkFormat values of the Vulkan specification. The signed BC4 and BC5
        // variants are left out because they need another decode.
        (Ktx2PixelFormat? found, srgb) = vkFormat switch
        {
            37 => (Ktx2PixelFormat.Rgba8, false),
            43 => (Ktx2PixelFormat.Rgba8, true),
            131 => (Ktx2PixelFormat.Bc1Rgb, false),
            132 => (Ktx2PixelFormat.Bc1Rgb, true),
            133 => (Ktx2PixelFormat.Bc1Rgba, false),
            134 => (Ktx2PixelFormat.Bc1Rgba, true),
            139 => (Ktx2PixelFormat.Bc4, false),
            141 => (Ktx2PixelFormat.Bc5, false),
            145 => (Ktx2PixelFormat.Bc7, false),
            146 => (Ktx2PixelFormat.Bc7, true),
            _ => ((Ktx2PixelFormat?)null, false),
        };
        format = found.GetValueOrDefault();
        return found.HasValue;
    }
}
