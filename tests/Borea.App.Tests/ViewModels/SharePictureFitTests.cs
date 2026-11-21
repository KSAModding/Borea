using System.Runtime.InteropServices;
using Borea.App.ViewModels;
using Borea.Core.Index;
using SkiaSharp;

namespace Borea.App.Tests.ViewModels;

public sealed class SharePictureFitTests
{
    [Fact]
    public void Fit_PictureWithinTheLimit_GivesTheSameFile()
    {
        var file = new PickedBinaryFile("shot.png", Noise(64, 36, opaque: true));

        Assert.Same(file, SharePictureFit.Fit(file, file.Bytes.Length));
    }

    [Fact]
    public void Fit_OpaqueNoiseAboveTheLimit_GivesASmallerJpegThatDecodes()
    {
        var file = new PickedBinaryFile("shot.png", Noise(640, 360, opaque: true));
        const int limit = 100_000;

        var fitted = SharePictureFit.Fit(file, limit);

        Assert.NotNull(fitted);
        Assert.Equal("shot.jpg", fitted.Name);
        Assert.InRange(fitted.Bytes.Length, 1, limit);
        var facts = Assert.IsType<ContentImageFacts>(ContentImageBytes.Inspect(fitted.Bytes));
        Assert.Equal("JPEG", facts.Format);
        Assert.InRange(facts.Width, 1, 639);
        Assert.Equal(facts.Width * 360 / 640, facts.Height, tolerance: 1);
        using var decoded = SKBitmap.Decode(fitted.Bytes);
        Assert.Equal((facts.Width, facts.Height), (decoded.Width, decoded.Height));
    }

    [Fact]
    public void Fit_TransparentNoiseAboveTheLimit_StaysASmallerPng()
    {
        var file = new PickedBinaryFile("logo.png", Noise(320, 180, opaque: false));
        const int limit = 100_000;

        var fitted = SharePictureFit.Fit(file, limit);

        Assert.NotNull(fitted);
        Assert.Equal("logo.png", fitted.Name);
        Assert.InRange(fitted.Bytes.Length, 1, limit);
        var facts = Assert.IsType<ContentImageFacts>(ContentImageBytes.Inspect(fitted.Bytes));
        Assert.Equal("PNG", facts.Format);
        Assert.InRange(facts.Width, 1, 319);
    }

    /// <summary>A PNG of random pixels, which a PNG encoder can hardly compress, so an opaque one takes about 3.5 bytes for each pixel.</summary>
    internal static byte[] Noise(int width, int height, bool opaque)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var pixels = new byte[width * height * 4];
        new Random(width * height).NextBytes(pixels);
        if (opaque)
        {
            for (var alpha = 3; alpha < pixels.Length; alpha += 4)
                pixels[alpha] = 0xFF;
        }

        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
