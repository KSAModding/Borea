using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Borea.Core.Index;

/// <summary>Checks image bytes against their record by the rules of RFC 0058 and RFC 0065, without decoding the image.</summary>
public static class ContentImageBytes
{
    public static long MaxBytes(ContentImage image) => image switch
    {
        IconImage => IconImage.MaxBytes,
        DescriptionImage => DescriptionImage.MaxBytes,
        null => throw new ArgumentNullException(nameof(image)),
        _ => throw new ArgumentException($"The image role {image.GetType().Name} has no limits.", nameof(image)),
    };

    public static ContentImageResult Verify(ContentImage image, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var maxBytes = MaxBytes(image);
        if (bytes.Length > maxBytes)
            return ContentImageResult.Failed(ContentImageFailure.TooLarge, $"The image is {bytes.Length} bytes, above the cap of {maxBytes}.");

        if (Inspect(bytes) is not { } facts)
            return ContentImageResult.Failed(ContentImageFailure.UnsupportedFormat, "The bytes are not a PNG, JPEG or WebP image that can be read.");

        if (facts.Animated)
            return ContentImageResult.Failed(ContentImageFailure.Animated, $"The {facts.Format} is animated.");

        if (PixelRule(image, Math.Min(facts.Width, facts.Height), Math.Max(facts.Width, facts.Height)) is { } rule)
            return ContentImageResult.Failed(ContentImageFailure.OutsideLimits, $"{rule}, but the image is {facts.Width} by {facts.Height} pixels.");

        var differences = new List<string>();
        if (facts.Width != image.Width)
            differences.Add($"width is {image.Width} and the bytes show {facts.Width}");

        if (facts.Height != image.Height)
            differences.Add($"height is {image.Height} and the bytes show {facts.Height}");

        if (bytes.Length != image.SizeBytes)
            differences.Add($"size is {image.SizeBytes} and the bytes show {bytes.Length}");

        var sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(sha256, image.Sha256, StringComparison.OrdinalIgnoreCase))
            differences.Add($"sha256 is {image.Sha256} and the bytes show {sha256}");

        return differences.Count == 0
            ? ContentImageResult.Loaded(bytes)
            : ContentImageResult.Failed(ContentImageFailure.FactsMismatch, string.Join("; ", differences) + ".");
    }

    /// <summary>The pixel rule of the role that the sides break, or null when they keep it.</summary>
    private static string? PixelRule(ContentImage image, int shorter, int longer) => image switch
    {
        IconImage when shorter is < IconImage.MinShorterSidePixels or > IconImage.MaxShorterSidePixels || longer > IconImage.MaxSideRatio * shorter
            => $"The shorter side must be {IconImage.MinShorterSidePixels} to {IconImage.MaxShorterSidePixels} pixels and the longer side at most {IconImage.MaxSideRatio} times the shorter side",
        DescriptionImage when shorter < 1 || longer > DescriptionImage.MaxPixels
            => $"Each side must be 1 to {DescriptionImage.MaxPixels} pixels",
        _ => null,
    };

    /// <summary>The format, pixel size and animation the bytes show, or null when they are not a PNG, JPEG or WebP image that can be read.</summary>
    public static ContentImageFacts? Inspect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            return InspectPng(bytes);

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
            return InspectJpeg(bytes);

        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
            return InspectWebP(bytes);

        return null;
    }

    private static ContentImageFacts? InspectPng(ReadOnlySpan<byte> bytes)
    {
        var position = 8L;
        (int Width, int Height)? size = null;
        var animated = false;
        var pixels = false;

        while (position + 12 <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes[(int)position..]);
            if (position + 12 + length > bytes.Length)
                return null;

            var kind = bytes.Slice((int)position + 4, 4);
            var body = bytes.Slice((int)position + 8, (int)length);
            if (size is null)
            {
                if (!kind.SequenceEqual("IHDR"u8) || length != 13)
                    return null;

                var width = BinaryPrimitives.ReadUInt32BigEndian(body);
                var height = BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
                if (width > int.MaxValue || height > int.MaxValue)
                    return null;

                size = ((int)width, (int)height);
            }
            else if (kind.SequenceEqual("acTL"u8))
            {
                animated = true;
            }
            else if (kind.SequenceEqual("IDAT"u8))
            {
                pixels = true;
            }
            else if (kind.SequenceEqual("IEND"u8))
            {
                return pixels ? new ContentImageFacts("PNG", size.Value.Width, size.Value.Height, animated) : null;
            }

            position += 12 + length;
        }

        return null;
    }

    private static ContentImageFacts? InspectJpeg(ReadOnlySpan<byte> bytes)
    {
        var position = 2;
        while (position < bytes.Length)
        {
            if (bytes[position] != 0xFF)
                return null;

            while (position < bytes.Length && bytes[position] == 0xFF)
                position++;

            if (position >= bytes.Length)
                return null;

            var marker = bytes[position++];
            if (marker is 0x01 or (>= 0xD0 and <= 0xD8))
                continue;

            if (marker is 0xD9 or 0xDA || position + 2 > bytes.Length)
                return null;

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[position..]);
            if (length < 2)
                return null;

            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                if (position + 7 > bytes.Length)
                    return null;

                var height = BinaryPrimitives.ReadUInt16BigEndian(bytes[(position + 3)..]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(bytes[(position + 5)..]);
                return new ContentImageFacts("JPEG", width, height, false);
            }

            position += length;
        }

        return null;
    }

    private static ContentImageFacts? InspectWebP(ReadOnlySpan<byte> bytes)
    {
        var end = 8L + BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        if (end > bytes.Length)
            return null;

        var position = 12L;
        (int Width, int Height)? canvas = null;
        (int Width, int Height)? frame = null;
        var animated = false;

        while (position + 8 <= end)
        {
            var kind = bytes.Slice((int)position, 4);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes[((int)position + 4)..]);
            if (position + 8 + length > end)
                return null;

            var body = bytes.Slice((int)position + 8, (int)length);
            if (kind.SequenceEqual("VP8X"u8))
            {
                if (length < 10)
                    return null;

                animated |= (body[0] & 0x02) != 0;
                canvas = (1 + ReadUInt24LittleEndian(body[4..]), 1 + ReadUInt24LittleEndian(body[7..]));
            }
            else if (kind.SequenceEqual("ANIM"u8) || kind.SequenceEqual("ANMF"u8))
            {
                animated = true;
            }
            else if (kind.SequenceEqual("VP8 "u8) && frame is null)
            {
                if (length < 10 || !body.Slice(3, 3).SequenceEqual((ReadOnlySpan<byte>)[0x9D, 0x01, 0x2A]))
                    return null;

                frame = (BinaryPrimitives.ReadUInt16LittleEndian(body[6..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(body[8..]) & 0x3FFF);
            }
            else if (kind.SequenceEqual("VP8L"u8) && frame is null)
            {
                if (length < 5 || body[0] != 0x2F)
                    return null;

                var bits = BinaryPrimitives.ReadUInt32LittleEndian(body[1..]);
                frame = (1 + (int)(bits & 0x3FFF), 1 + (int)((bits >> 14) & 0x3FFF));
            }

            position += 8 + length + (length & 1);
        }

        if (frame is null && !animated)
            return null;

        var (frameWidth, frameHeight) = canvas ?? frame ?? (0, 0);
        return new ContentImageFacts("WebP", frameWidth, frameHeight, animated);
    }

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> bytes) => bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
}

public readonly record struct ContentImageFacts(string Format, int Width, int Height, bool Animated);
