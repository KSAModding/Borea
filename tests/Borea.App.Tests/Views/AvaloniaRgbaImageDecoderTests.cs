using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Borea.App.Views.Preview;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class AvaloniaRgbaImageDecoderTests
{
    [Fact]
    public async Task APng_ReportsItsSizeFromTheHeader_AndDecodesToRgbaInRowOrder()
    {
        byte[] pixels = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 10, 20, 30, 255, 40, 50, 60, 255, 70, 80, 90, 255];
        var result = await HeadlessApp.Session.Dispatch(() =>
        {
            using var png = new MemoryStream(Encode(pixels, 3, 2, PngBitmapEncoderOptions.Default));
            var decoder = new AvaloniaRgbaImageDecoder();
            var found = decoder.TryReadSize(png, out var width, out var height);
            png.Position = 0;
            var image = decoder.Decode(png, 3);
            return (found, width, height, image.Width, image.Height, image.Rgba);
        }, CancellationToken.None);

        Assert.Equal((true, 3, 2, 3, 2), (result.found, result.width, result.height, result.Width, result.Height));
        Assert.Equal(pixels, result.Rgba);
    }

    [Fact]
    public async Task AJpeg_ReportsItsSizeFromItsFrameHeader()
    {
        var size = await HeadlessApp.Session.Dispatch(() =>
        {
            using var jpeg = new MemoryStream(Encode(new byte[5 * 7 * 4], 5, 7, JpegBitmapEncoderOptions.Default));
            var found = new AvaloniaRgbaImageDecoder().TryReadSize(jpeg, out var width, out var height);
            return (found, width, height);
        }, CancellationToken.None);

        Assert.Equal((true, 5, 7), size);
    }

    private static byte[] Encode(byte[] rgba, int width, int height, BitmapEncoderOptions options)
    {
        var handle = GCHandle.Alloc(rgba, GCHandleType.Pinned);
        try
        {
            using var bitmap = new Bitmap(PixelFormat.Rgba8888, AlphaFormat.Unpremul, handle.AddrOfPinnedObject(), new PixelSize(width, height), new Vector(96, 96), width * 4);
            using var stream = new MemoryStream();
            bitmap.Save(stream, options);
            return stream.ToArray();
        }
        finally
        {
            handle.Free();
        }
    }
}
