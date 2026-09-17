using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.Core.Index;

namespace Borea.App.Tests.Views;

public sealed class ListingImageViewTests
{
    private const string Url = "https://images.example/icon.png";

    private static readonly string Digest = new('A', 64);

    [Theory]
    [InlineData(512, 512, 100, 100, 0, 0, 100, 100)]
    [InlineData(512, 512, 200, 100, 50, 0, 100, 100)]
    [InlineData(512, 512, 171, 200, 0, 14.5, 171, 171)]
    [InlineData(1600, 900, 160, 160, 0, 35, 160, 90)]
    public void Fit_ScalesWithoutStretchingOrCropping(double imageWidth, double imageHeight, double slotWidth, double slotHeight, double x, double y, double width, double height)
    {
        Assert.Equal(new Rect(x, y, width, height), ListingImageView.Fit(new Size(imageWidth, imageHeight), new Size(slotWidth, slotHeight)));
    }

    [Theory]
    [InlineData(1280, 640, 640, 320, 160, 0, 320)]
    [InlineData(640, 1280, 320, 640, 0, 160, 320)]
    [InlineData(511, 256, 511, 256, 127, 0, 256)]
    [InlineData(512, 512, 256, 256, 0, 0, 256)]
    public void ShownPart_Icon_IsTheCenterSquareOfTheBitmap(int width, int height, double bitmapWidth, double bitmapHeight, double x, double y, double side)
    {
        Assert.Equal(new Rect(x, y, side, side), ListingImageView.ShownPart(Icon(width, height), new Size(bitmapWidth, bitmapHeight)));
    }

    [Fact]
    public void Placement_WideIcon_DrawsTheCenterSquareAsASquareInTheSlot()
    {
        var placement = ListingImageView.Placement(Icon(1280, 640), new Size(640, 320), new Size(200, 160));

        Assert.Equal((new Rect(160, 0, 320, 320), new Rect(20, 0, 160, 160)), placement);
    }

    [Fact]
    public void Placement_DescriptionImage_DrawsTheWholeBitmapInTheSlot()
    {
        var image = new DescriptionImage("shot", Url, Digest, 1600, 900, 400_000);

        var placement = ListingImageView.Placement(image, new Size(800, 450), new Size(200, 160));

        Assert.Equal((new Rect(0, 0, 800, 450), new Rect(0, 23.75, 200, 112.5)), placement);
    }

    [Theory]
    [InlineData(2048, 1024, 64, 64, 1.5, 192)]
    [InlineData(1024, 2048, 64, 64, 1.5, 96)]
    [InlineData(2048, 1024, 0, 0, 1, 2048)]
    public void DecodeWidth_Icon_DecodesEnoughPixelsForTheCenterSquare(int width, int height, double slotWidth, double slotHeight, double scaling, int decodeWidth)
    {
        Assert.Equal(decodeWidth, ListingImageView.DecodeWidth(Icon(width, height), new Size(slotWidth, slotHeight), scaling));
    }

    [Fact]
    public async Task Render_ClipsTheBackgroundAndTheImageToTheCornerRadius()
    {
        await using var session = HeadlessApp.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var pixels = await session.Dispatch(async () =>
        {
            var placeholder = new Border();
            var view = new ListingImageView
            {
                Image = new ListingImage(new MainViewModel(), Icon(256, 256)) { Bytes = LeftHalfBluePng() },
                Background = Brushes.Red,
                CornerRadius = new CornerRadius(12),
                Child = placeholder,
            };
            var window = new Window { Width = 40, Height = 40, Background = Brushes.White, Content = view };
            window.Show();

            while (placeholder.IsVisible)
                await Task.Delay(10, timeout.Token);

            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame()!;
            using var buffer = frame.Lock();
            return (Pixel(buffer, 1, 1), Pixel(buffer, 38, 1), Pixel(buffer, 10, 20), Pixel(buffer, 30, 20));
        }, timeout.Token);

        Assert.Equal((Colors.White, Colors.White, Colors.Blue, Colors.Red), pixels);
    }

    private static byte[] LeftHalfBluePng()
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(256, 256));
        using (var context = bitmap.CreateDrawingContext())
            context.FillRectangle(Brushes.Blue, new Rect(0, 0, 128, 256));

        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    private static Color Pixel(ILockedFramebuffer buffer, int x, int y)
    {
        var offset = y * buffer.RowBytes + x * 4;
        return Color.FromRgb(Marshal.ReadByte(buffer.Address, offset), Marshal.ReadByte(buffer.Address, offset + 1), Marshal.ReadByte(buffer.Address, offset + 2));
    }

    private static IconImage Icon(int width, int height) => new(Url, Digest, width, height, 1000);
}
