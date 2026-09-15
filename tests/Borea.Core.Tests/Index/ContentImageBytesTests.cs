using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Borea.Core.Index;

namespace Borea.Core.Tests.Index;

public sealed class ContentImageBytesTests
{
    private const string Url = "https://example.com/image";
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    public static TheoryData<byte[], int, int> ReadableImages => new()
    {
        { Png(640, 480), 640, 480 },
        { Jpeg(1600, 900), 1600, 900 },
        { WebP(Lossy(640, 480)), 640, 480 },
        { WebP(Lossless(300, 200)), 300, 200 },
        { WebP(Extended(1024, 768), Lossy(1024, 768)), 1024, 768 },
    };

    public static TheoryData<byte[]> AnimatedImages => new()
    {
        Png(512, 512, animated: true),
        WebP(Extended(512, 512, animated: true), WebPChunk("ANIM", new byte[6])),
        WebP(Extended(512, 512), WebPChunk("ANMF", new byte[16]), Lossy(512, 512)),
    };

    public static TheoryData<byte[]> UnreadableBytes => new()
    {
        "GIF89a"u8.ToArray().Concat(new byte[20]).ToArray(),
        "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(),
        Array.Empty<byte>(),
        Png(512, 512)[..40],
        Jpeg(512, 512)[..24],
        WebP(Lossy(512, 512))[..^4],
    };

    [Theory]
    [MemberData(nameof(ReadableImages))]
    public void Verify_ReadableFormat_LoadsTheBytes(byte[] bytes, int width, int height)
    {
        var result = ContentImageBytes.Verify(new DescriptionImage("shot", Url, Sha256Of(bytes), width, height, bytes.Length), bytes);

        Assert.True(result.IsLoaded, result.Reason);
        Assert.Null(result.Failure);
        Assert.Equal(bytes, result.Bytes.ToArray());
    }

    [Fact]
    public void Verify_IconThatMatchesItsRecord_LoadsTheBytes()
    {
        var bytes = Png(512, 512);

        var result = ContentImageBytes.Verify(new IconImage(Url, Sha256Of(bytes).ToLowerInvariant(), 512, 512, bytes.Length), bytes);

        Assert.True(result.IsLoaded, result.Reason);
    }

    [Theory]
    [MemberData(nameof(AnimatedImages))]
    public void Verify_AnimatedImage_IsAnimated(byte[] bytes)
    {
        var result = ContentImageBytes.Verify(new IconImage(Url, Sha256Of(bytes), 512, 512, bytes.Length), bytes);

        Assert.Equal(ContentImageFailure.Animated, result.Failure);
        Assert.True(result.Bytes.IsEmpty);
    }

    [Theory]
    [MemberData(nameof(UnreadableBytes))]
    public void Verify_BytesThatAreNotAReadableImage_AreUnsupported(byte[] bytes)
    {
        var result = ContentImageBytes.Verify(new DescriptionImage("shot", Url, Digest, 512, 512, 1000), bytes);

        Assert.Equal(ContentImageFailure.UnsupportedFormat, result.Failure);
    }

    [Theory]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("size")]
    [InlineData("sha256")]
    public void Verify_FactDiffersFromTheRecord_IsAMismatch(string fact)
    {
        var bytes = Png(640, 480);
        var record = new DescriptionImage(
            "shot",
            Url,
            fact == "sha256" ? Digest : Sha256Of(bytes),
            fact == "width" ? 641 : 640,
            fact == "height" ? 481 : 480,
            fact == "size" ? bytes.Length + 1 : bytes.Length);

        var result = ContentImageBytes.Verify(record, bytes);

        Assert.Equal(ContentImageFailure.FactsMismatch, result.Failure);
        Assert.StartsWith($"{fact} is", result.Reason);
    }

    [Fact]
    public void Verify_PixelsOutsideTheRoleLimits_AreOutsideTheLimits()
    {
        var small = Png(128, 128);
        var wide = Png(4096, 100);

        var icon = ContentImageBytes.Verify(new IconImage(Url, Sha256Of(small), 256, 256, small.Length), small);
        var description = ContentImageBytes.Verify(new DescriptionImage("wide", Url, Sha256Of(wide), 2048, 100, wide.Length), wide);

        Assert.Equal(ContentImageFailure.OutsideLimits, icon.Failure);
        Assert.Equal(ContentImageFailure.OutsideLimits, description.Failure);
    }

    [Fact]
    public void Verify_MoreBytesThanTheRoleCap_IsTooLarge()
    {
        var bytes = new byte[IconImage.MaxBytes + 1];

        var result = ContentImageBytes.Verify(new IconImage(Url, Sha256Of(bytes), 512, 512, IconImage.MaxBytes), bytes);

        Assert.Equal(ContentImageFailure.TooLarge, result.Failure);
    }

    [Fact]
    public void MaxBytes_IsTheCapOfTheRole()
    {
        Assert.Equal(IconImage.MaxBytes, ContentImageBytes.MaxBytes(new IconImage(Url, Digest, 512, 512, 1000)));
        Assert.Equal(DescriptionImage.MaxBytes, ContentImageBytes.MaxBytes(new DescriptionImage("shot", Url, Digest, 1600, 900, 1000)));
    }

    private static string Sha256Of(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static byte[] Png(int width, int height, bool animated = false)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 8;
        header[9] = 6;

        List<byte> bytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        AddPngChunk(bytes, "IHDR", header);
        if (animated)
            AddPngChunk(bytes, "acTL", [0, 0, 0, 2, 0, 0, 0, 0]);

        AddPngChunk(bytes, "IDAT", [0x78, 0x9C, 0x63, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01]);
        AddPngChunk(bytes, "IEND", []);
        return [.. bytes];
    }

    /// <summary>The checks read no chunk CRC, so it stays zero.</summary>
    private static void AddPngChunk(List<byte> bytes, string kind, byte[] body)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)body.Length);
        bytes.AddRange(length);
        bytes.AddRange(Encoding.ASCII.GetBytes(kind));
        bytes.AddRange(body);
        bytes.AddRange(new byte[4]);
    }

    private static byte[] Jpeg(int width, int height) =>
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
        0xFF, 0xC0, 0x00, 0x0B, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x01, 0x01, 0x11, 0x00,
        0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00,
        0x00, 0xFF, 0xD9,
    ];

    private static byte[] WebP(params byte[][] chunks)
    {
        List<byte> body = [.. "WEBP"u8];
        foreach (var chunk in chunks)
            body.AddRange(chunk);

        return [.. "RIFF"u8, .. LittleEndian(body.Count), .. body];
    }

    private static byte[] WebPChunk(string kind, byte[] body)
    {
        List<byte> chunk = [.. Encoding.ASCII.GetBytes(kind), .. LittleEndian(body.Length), .. body];
        if (body.Length % 2 == 1)
            chunk.Add(0);

        return [.. chunk];
    }

    private static byte[] Lossy(int width, int height) =>
        WebPChunk("VP8 ", [0, 0, 0, 0x9D, 0x01, 0x2A, (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8)]);

    private static byte[] Lossless(int width, int height) =>
        WebPChunk("VP8L", [0x2F, .. LittleEndian((width - 1) | ((height - 1) << 14))]);

    private static byte[] Extended(int width, int height, bool animated = false) =>
        WebPChunk("VP8X", [animated ? (byte)0x02 : (byte)0x00, 0, 0, 0, .. LittleEndian(width - 1)[..3], .. LittleEndian(height - 1)[..3]]);

    private static byte[] LittleEndian(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }
}
