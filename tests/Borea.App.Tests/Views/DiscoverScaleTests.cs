using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;
using Borea.Core.Index;
using Borea.Core.Mods;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class DiscoverScaleTests
{
    private const int Listings = 300;

    /// <summary>The window shows about five rows and the list keeps one window more above and below, so this ceiling does not grow with the list.</summary>
    private const int MaxRealizedRows = 30;

    /// <summary>The decoded pixels that the rows and the bitmap shelf hold together, whatever the length of the list.</summary>
    private const long MaxDecodedBytes = 10L * 1024 * 1024;

    [Fact]
    public async Task LongList_KeepsItsRowsAndBitmapsWithTheWindow()
    {
        var measured = await HeadlessApp.RunAsync(async () =>
        {
            var (viewModel, page, window, scroller) = ShowLongList();
            List<ListingImageView> Views() => page.GetVisualDescendants().OfType<ListingImageView>().ToList();

            async Task<(int Rows, int Bitmaps, long Bytes)> SettleAsync()
            {
                window.UpdateLayout();
                while (Views().Any(view => view.State != ListingImageView.DisplayState.Loaded))
                    await Task.Delay(5);

                // the icons are square, so a bitmap holds its width squared in pixels of four bytes
                var bitmaps = Views().Select(view => view.BitmapWidth).OfType<int>().ToList();
                return (Views().Count, bitmaps.Count, bitmaps.Sum(width => 4L * width * width) + ListingImageView.Shelf.Size);
            }

            var peak = await SettleAsync();
            while (scroller.Offset.Y + scroller.Viewport.Height < scroller.Extent.Height - 1)
            {
                scroller.Offset = scroller.Offset.WithY(scroller.Offset.Y + scroller.Viewport.Height);
                var step = await SettleAsync();
                peak = (Math.Max(peak.Rows, step.Rows), Math.Max(peak.Bitmaps, step.Bitmaps), Math.Max(peak.Bytes, step.Bytes));
            }

            var reachedEnd = Views().Any(view => view.Image == viewModel.DiscoverItems[^1].Icon);
            scroller.Offset = default;
            await SettleAsync();
            var firstShowsAgain = Views().Single(view => view.Image == viewModel.DiscoverItems[0].Icon).BitmapWidth is not null;
            window.Close();
            return (peak, reachedEnd, firstShowsAgain);
        });

        Assert.True(measured.reachedEnd, "The list did not scroll to its last row.");
        Assert.True(measured.firstShowsAgain, "The first row did not show its icon again.");
        Assert.InRange(measured.peak.Rows, 1, MaxRealizedRows);
        Assert.InRange(measured.peak.Bitmaps, 1, MaxRealizedRows);
        Assert.InRange(measured.peak.Bytes, 1, MaxDecodedBytes);
    }

    [Fact]
    public async Task LongList_ShownAgain_KeepsItsScrollPosition()
    {
        var kept = await HeadlessApp.RunAsync(() =>
        {
            var (_, page, window, scroller) = ShowLongList();
            scroller.Offset = new Vector(0, 20000);
            window.UpdateLayout();
            var before = (scroller.Offset.Y, Rows: RowsInView(page, scroller));

            // the main window hides a page that it leaves and shows it again on the way back
            page.IsVisible = false;
            window.UpdateLayout();
            page.IsVisible = true;
            window.UpdateLayout();
            var after = (scroller.Offset.Y, Rows: RowsInView(page, scroller));
            window.Close();
            return Task.FromResult((before, after));
        });

        Assert.Equal(20000, kept.before.Y);
        Assert.Equal(kept.before.Y, kept.after.Y);
        Assert.NotEmpty(kept.before.Rows);
        Assert.Equal(kept.before.Rows, kept.after.Rows);
    }

    private static (MainViewModel ViewModel, DiscoverPage Page, Window Window, ScrollViewer Scroller) ShowLongList()
    {
        var viewModel = new MainViewModel();
        var png = IconPng();
        for (var number = 0; number < Listings; number++)
            viewModel.DiscoverItems.Add(Row(viewModel, number, png));

        var page = new DiscoverPage { DataContext = viewModel };
        var window = new Window { Width = 1280, Height = 832, Content = page, DataContext = viewModel };
        window.Show();
        return (viewModel, page, window, page.GetVisualDescendants().OfType<ScrollViewer>().First());
    }

    /// <summary>The names of the rows that the viewport of the page shows, top to bottom.</summary>
    private static List<string> RowsInView(DiscoverPage page, ScrollViewer scroller) =>
        page.GetVisualDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains("card") && border.DataContext is DiscoverItem)
            .Select(border => (Top: border.TranslatePoint(default, scroller)!.Value.Y, border.Bounds.Height, ((DiscoverItem)border.DataContext!).Name))
            .Where(row => row.Top + row.Height > 0 && row.Top < scroller.Viewport.Height)
            .OrderBy(row => row.Top)
            .Select(row => row.Name)
            .ToList();

    private static DiscoverItem Row(MainViewModel owner, int number, byte[] png)
    {
        var id = $"Mod{number:D3}";
        var listing = new ModMetadata(1, id, "index", $"Mod {number:D3}", ["Maxi"], "A mod of a long list.", "MIT", new Dictionary<string, string> { ["forums"] = $"https://forums.example/{id}" }, "2026.8.19.5261");
        var icon = new IconImage($"https://images.example/{id}.png", new string('A', 64), 256, 256, png.Length);
        var row = new DiscoverItem(owner, listing, new ContentIndexListing(id, listing, [], indexStatus: null, images: new ContentImages(icon, [])));
        row.Icon!.Bytes = [.. png];
        return row;
    }

    private static byte[] IconPng()
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(256, 256));
        using (var context = bitmap.CreateDrawingContext())
            context.FillRectangle(Brushes.Blue, new Rect(0, 0, 256, 256));

        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }
    [Fact]
    public async Task LongList_KeepsTheGapBetweenItsRows()
    {
        var gaps = await HeadlessApp.RunAsync(() =>
        {
            var (_, page, window, scroller) = ShowLongList();
            window.UpdateLayout();
            var cards = page.GetVisualDescendants().OfType<Border>()
                .Where(border => border.Classes.Contains("card") && border.DataContext is DiscoverItem)
                .Select(border => (Top: border.TranslatePoint(default, scroller)!.Value.Y, border.Bounds.Height))
                .OrderBy(row => row.Top)
                .Take(4)
                .ToList();
            window.Close();
            return Task.FromResult(cards.Zip(cards.Skip(1), (above, below) => below.Top - (above.Top + above.Height)).ToList());
        });

        Assert.Equal([8.0, 8.0, 8.0], gaps);
    }
}
