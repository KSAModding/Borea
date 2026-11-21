using System;
using System.IO;
using Borea.Core.Index;
using SkiaSharp;

namespace Borea.App.ViewModels;

/// <summary>
/// Makes a still picture fit the size limit of a share host, because the game saves its screenshots as PNG files that are mostly larger than a host takes.
/// An opaque picture goes as a JPEG, first at full size and then smaller in steps. A picture with transparent pixels stays a PNG and only gets smaller, because a JPEG would put a color behind them.
/// It decodes and encodes on the calling thread, so the caller runs it off the UI thread.
/// </summary>
internal static class SharePictureFit
{
    /// <summary>The largest file that Borea reads to fit. A screenshot of the game at 7680 by 4320 pixels stays well below it.</summary>
    internal const long MaxSourceBytes = 128 * 1024 * 1024;

    /// <summary>The most pixels that Borea decodes, because a small file can hold a very large picture that would fill the memory.</summary>
    internal const long MaxSourcePixels = 64 * 1024 * 1024;

    private const int JpegQuality = 90;

    /// <summary>
    /// Gives the file itself when it is within <paramref name="maxBytes"/>, a smaller copy that fits, or null when no copy can fit.
    /// A copy as a JPEG takes the name of the file with the extension .jpg.
    /// An animated picture, a picture that does not decode completely and a picture that a camera turned gives null, because a copy would lose the motion, show broken pixels or stand on its side.
    /// </summary>
    internal static PickedBinaryFile? Fit(PickedBinaryFile file, long maxBytes)
    {
        if (file.Bytes.Length <= maxBytes)
            return file;

        if (ContentImageBytes.Inspect(file.Bytes) is not { Animated: false } facts || (long)facts.Width * facts.Height > MaxSourcePixels)
            return null;

        using var source = Decode(file.Bytes);
        if (source is null)
            return null;

        var isOpaque = IsOpaque(source);
        var format = isOpaque ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png;
        var name = isOpaque ? Path.ChangeExtension(file.Name, ".jpg") : file.Name;
        var scale = 1.0;
        while (true)
        {
            var width = (int)(source.Width * scale);
            var height = (int)(source.Height * scale);
            if (width < 1 || height < 1)
                return null;

            var bytes = Encode(source, width, height, format);
            if (bytes is null)
                return null;

            if (bytes.Length <= maxBytes)
                return new PickedBinaryFile(name, bytes);

            // The size of a picture grows about with its pixel count, and the margin and the cap on each step keep the next try from landing just above the limit again.
            scale *= Math.Min(0.9, Math.Sqrt((double)maxBytes / bytes.Length) * 0.95);
        }
    }

    private static SKBitmap? Decode(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedOrigin != SKEncodedOrigin.TopLeft)
            return null;

        var bitmap = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) == SKCodecResult.Success)
            return bitmap;

        bitmap.Dispose();
        return null;
    }

    /// <summary>The game writes an alpha channel in which every pixel is opaque, so only the pixels tell whether a picture has transparency.</summary>
    private static bool IsOpaque(SKBitmap bitmap)
    {
        var pixels = bitmap.GetPixelSpan();
        for (var alpha = 3; alpha < pixels.Length; alpha += 4)
        {
            if (pixels[alpha] != 0xFF)
                return false;
        }

        return true;
    }

    private static byte[]? Encode(SKBitmap source, int width, int height, SKEncodedImageFormat format)
    {
        if (width == source.Width && height == source.Height)
            return EncodeBitmap(source, format);

        using var scaled = source.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return scaled is null ? null : EncodeBitmap(scaled, format);
    }

    private static byte[]? EncodeBitmap(SKBitmap bitmap, SKEncodedImageFormat format)
    {
        using var data = bitmap.Encode(format, JpegQuality);
        return data?.ToArray();
    }
}
