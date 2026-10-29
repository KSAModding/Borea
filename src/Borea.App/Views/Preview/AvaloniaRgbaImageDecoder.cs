using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Borea.Preview.Scene;
using Borea.Preview.Textures;

namespace Borea.App.Views.Preview;

/// <summary>
/// Decodes PNG and JPEG textures of the vehicle preview with the bitmap decoder of the Avalonia platform, so the
/// preview needs no image package of its own. The size comes from the PNG IHDR chunk or the JPEG frame header, which
/// the decoder reads itself, because Avalonia has no call that reads the size without the pixels.
/// </summary>
internal sealed class AvaloniaRgbaImageDecoder : IRgbaImageDecoder
{
    // A JPEG can put many segments, such as EXIF data and thumbnails, before its frame header. The limit stops a
    // file of empty segments from keeping the scan busy.
    private const int MaxJpegSegments = 1024;

    public bool TryReadSize(Stream stream, out int width, out int height)
    {
        ArgumentNullException.ThrowIfNull(stream);
        width = 0;
        height = 0;
        Span<byte> start = stackalloc byte[24];
        var read = stream.ReadAtLeast(start, start.Length, throwOnEndOfStream: false);
        start = start[..read];
        if (start.Length >= 24 && start[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            // The PNG specification puts IHDR first, with the width and the height as big-endian 32-bit numbers.
            if (!start[12..16].SequenceEqual("IHDR"u8))
            {
                return false;
            }

            var pngWidth = BinaryPrimitives.ReadUInt32BigEndian(start[16..]);
            var pngHeight = BinaryPrimitives.ReadUInt32BigEndian(start[20..]);
            if (pngWidth > int.MaxValue || pngHeight > int.MaxValue)
            {
                return false;
            }

            width = (int)pngWidth;
            height = (int)pngHeight;
            return true;
        }

        if (start.Length >= 2 && start[0] == 0xFF && start[1] == 0xD8)
        {
            stream.Position = 2;
            return TryReadJpegSize(stream, out width, out height);
        }

        return false;
    }

    public PreviewImage Decode(Stream stream, int width)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            using var bitmap = Bitmap.DecodeToWidth(stream, width);
            var size = bitmap.PixelSize;
            var rgba = new byte[size.Width * size.Height * 4];
            var handle = GCHandle.Alloc(rgba, GCHandleType.Pinned);
            try
            {
                // CopyPixels converts from the format of the decoded bitmap to the format of the target buffer.
                using var target = new LockedFramebuffer(
                    handle.AddrOfPinnedObject(), size, size.Width * 4, bitmap.Dpi, PixelFormat.Rgba8888, AlphaFormat.Unpremul, null);
                bitmap.CopyPixels(target);
            }
            finally
            {
                handle.Free();
            }

            return new PreviewImage(size.Width, size.Height, rgba);
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or IOException))
        {
            // Avalonia gives no single exception type for data it cannot decode.
            throw new InvalidDataException($"The image could not be decoded: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Walks the JPEG marker segments from the position after the SOI marker until the first frame header, whose
    /// height and width follow its sample precision as big-endian 16-bit numbers (ITU-T T.81, section B.2.2).
    /// </summary>
    private static bool TryReadJpegSize(Stream stream, out int width, out int height)
    {
        width = 0;
        height = 0;
        Span<byte> segment = stackalloc byte[5];
        for (var count = 0; count < MaxJpegSegments; count++)
        {
            var marker = ReadMarker(stream);
            if (marker < 0 || marker is 0xD9 or 0xDA)
            {
                return false;
            }

            // TEM and RST0 to RST7 have no length field.
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                continue;
            }

            if (stream.ReadAtLeast(segment[..2], 2, throwOnEndOfStream: false) < 2)
            {
                return false;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(segment);
            if (length < 2)
            {
                return false;
            }

            // SOF0 to SOF15 are frame headers, except DHT (C4), JPG (C8) and DAC (CC).
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                if (length < 7 || stream.ReadAtLeast(segment, 5, throwOnEndOfStream: false) < 5)
                {
                    return false;
                }

                height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
                width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);

                // A height of 0 means that a DNL segment after the first scan gives it, which this scan does not reach.
                return width > 0 && height > 0;
            }

            stream.Seek(length - 2, SeekOrigin.Current);
        }

        return false;
    }

    /// <summary>Reads the next marker code, after any fill bytes of 0xFF, or gives -1 at the end of the stream.</summary>
    private static int ReadMarker(Stream stream)
    {
        if (stream.ReadByte() != 0xFF)
        {
            return -1;
        }

        int code;
        do
        {
            code = stream.ReadByte();
        }
        while (code == 0xFF);

        return code;
    }
}
