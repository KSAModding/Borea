using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Borea.App.Views;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class NavigationRailTests
{
    /// <summary>
    /// How far below the other rail icons the weight of the house may sit.
    /// The lift leaves about 1.1 pixels of it, and without the lift it is about 2.2, which is the low icon the report is about.
    /// </summary>
    private const double Tolerance = 1.5;

    [Fact]
    public async Task TheHouseIcon_CarriesItsWeightLevelWithTheOtherRailIcons()
    {
        var session = HeadlessApp.Session;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var icons = await session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var measured = window.GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.Classes.Contains("nav"))
                .Select(button => (Lifted: button.Content is StyledElement icon && icon.Classes.Contains("optical-lift"), Center: IconCenter(button)))
                .ToArray();

            window.Close();
            return measured;
        }, timeout.Token);

        Assert.Equal(5, icons.Length);
        var house = icons.Single(icon => icon.Lifted).Center;
        var others = icons.Where(icon => !icon.Lifted).Average(icon => icon.Center);
        Assert.InRange(house - others, 0, Tolerance);
    }

    /// <summary>
    /// Renders one rail button and returns the vertical center of the ink of its icon,
    /// with every pixel weighted by how much opacity the icon adds to the button below it.
    /// </summary>
    private static double IconCenter(Button button)
    {
        var width = (int)button.Bounds.Width;
        var height = (int)button.Bounds.Height;
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(button);

        // a RenderTargetBitmap cannot be locked, so its pixels are read through a buffer of our own
        var stride = width * 4;
        var pixels = Marshal.AllocHGlobal(stride * height);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height), pixels, stride * height, stride);

            // the third row holds the button alone, because no icon reaches that far up
            var background = Marshal.ReadByte(pixels, 2 * stride + width / 2 * 4 + 3);
            double weighted = 0;
            double ink = 0;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    // only the icon makes a pixel more opaque than the flat background, the rounded corners make it less
                    var alpha = Math.Max(Marshal.ReadByte(pixels, y * stride + x * 4 + 3) - background, 0);
                    weighted += (y + 0.5) * alpha;
                    ink += alpha;
                }

            Assert.True(ink > 0, "the rail button paints no icon");
            return weighted / ink;
        }
        finally
        {
            Marshal.FreeHGlobal(pixels);
        }
    }
}
