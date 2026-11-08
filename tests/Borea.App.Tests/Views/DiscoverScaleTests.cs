using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;
using Borea.Core.Index;
using Borea.Core.Mods;
using Borea.Network.Images;
using Borea.Storage.Images;
using Borea.Storage.Paths;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class DiscoverScaleTests
{
    private const int Listings = 300;

    /// <summary>A list short enough that the list keeps all of its rows alive.</summary>
    private const int ShortListings = 20;

    /// <summary>The window shows about six rows and the list keeps two windows more above and below, so this ceiling does not grow with the list.</summary>
    private const int MaxRealizedRows = 45;

    /// <summary>The decoded pixels that the rows and the bitmap shelf hold together, whatever the length of the list.</summary>
    private const long MaxDecodedBytes = 10L * 1024 * 1024;

    /// <summary>The icon files that the rows and the idle icons hold together, whatever the length of the list. The files of the whole list are about 14 MiB.</summary>
    private const long MaxIconBytes = 8L * 1024 * 1024;

    /// <summary>
    /// What the list may allocate for each row that it shows again while it scrolls, so that a fast scroll does not keep the garbage collector busy.
    /// The panel takes a recycled row out of the tree and puts it back, and the row then takes the styles of the app again for each of its controls.
    /// A row measured about 656 KB on Windows, and the allocation is the same on each run except for about a kilobyte that differs between the operating systems.
    /// The limit is half as much again, so it does not fail by chance, but it fails when the row builds its hidden parts again, which cost about 1,047 KB.
    /// </summary>
    private const long MaxBytesPerShownRow = 960L * 1024;

    [Fact]
    public async Task LongList_KeepsItsRowsBitmapsAndIconFilesWithTheWindow()
    {
        var files = await HeadlessApp.RunAsync(() => Task.FromResult(Enumerable.Range(0, Listings).Select(NoisyIconPng).ToArray()));
        var host = new IconHost(files.Select((png, number) => (IconUrl(number), png)).ToDictionary());

        // the image cache stays in memory here, because three hundred file writes take much of the time limit of a dispatch
        var source = new ContentImageSource(new MemoryImageCache(), host, TimeSpan.FromSeconds(30));
        using var harness = await ViewModelHarness.CreateAsync(images: source);
        var measured = await HeadlessApp.RunAsync(harness, async () =>
        {
            var viewModel = harness.ViewModel;
            await viewModel.EnsureDiscoverLoadedAsync();
            viewModel.DiscoverItems.Clear();
            for (var number = 0; number < Listings; number++)
                viewModel.DiscoverItems.Add(Row(viewModel, number, files[number]));

            var (page, window, scroller) = Show(viewModel);
            List<ListingImageView> Views() => page.GetVisualDescendants().OfType<ListingImageView>().ToList();
            bool InView(ListingImageView view) => view.TranslatePoint(default, scroller) is { } at && at.Y + view.Bounds.Height > 0 && at.Y < scroller.Viewport.Height;

            // an icon loads when its row comes near the window, so only the rows in view have to show theirs
            async Task<(int Rows, int Bitmaps, long Bytes, long IconBytes)> SettleAsync()
            {
                window.UpdateLayout();
                while (Views().Any(view => InView(view) && view.State != ListingImageView.DisplayState.Loaded))
                    await Task.Delay(5);

                // the icons are square, so a bitmap holds its width squared in pixels of four bytes
                var bitmaps = Views().Select(view => view.BitmapWidth).OfType<int>().ToList();
                var iconBytes = viewModel.DiscoverItems.Sum(item => (long)(item.Icon!.Bytes?.Length ?? 0));
                return (Views().Count, bitmaps.Count, bitmaps.Sum(width => 4L * width * width) + ListingImageView.Shelf.Size, iconBytes);
            }

            var peak = await SettleAsync();
            async Task MeasureAsync()
            {
                var step = await SettleAsync();
                peak = (Math.Max(peak.Rows, step.Rows), Math.Max(peak.Bitmaps, step.Bitmaps), Math.Max(peak.Bytes, step.Bytes), Math.Max(peak.IconBytes, step.IconBytes));
            }

            while (scroller.Offset.Y + scroller.Viewport.Height < scroller.Extent.Height - 1)
            {
                scroller.Offset = scroller.Offset.WithY(scroller.Offset.Y + scroller.Viewport.Height);
                await MeasureAsync();
            }

            var first = viewModel.DiscoverItems[0].Icon!;
            var reachedEnd = Views().Any(view => view.Image == viewModel.DiscoverItems[^1].Icon);
            var firstGaveItsBytesBack = !first.IsLoaded;

            // the way back counts too, so the rows that the jump leaves behind must give their files back as well
            scroller.Offset = default;
            await MeasureAsync();
            while (!first.IsLoaded)
                await Task.Delay(5);

            await MeasureAsync();
            var firstShowsAgain = Views().Single(view => view.Image == first).BitmapWidth is not null;
            window.Close();
            return (peak, reachedEnd, firstGaveItsBytesBack, firstShowsAgain);
        });

        Assert.True(measured.reachedEnd, "The list did not scroll to its last row.");
        Assert.True(measured.firstGaveItsBytesBack, "The icon of the first row kept its file at the end of the list.");
        Assert.True(measured.firstShowsAgain, "The first row did not show its icon again.");
        Assert.InRange(measured.peak.Rows, 1, MaxRealizedRows);
        Assert.InRange(measured.peak.Bitmaps, 1, MaxRealizedRows);
        Assert.InRange(measured.peak.Bytes, 1, MaxDecodedBytes);
        Assert.InRange(measured.peak.IconBytes, 1, MaxIconBytes);

        // each icon came from its host once, so the rows that came back read the image cache
        Assert.Equal(files.Select((_, number) => IconUrl(number)).Order(), host.Requests.Order());
    }

    [Fact]
    public async Task IconThatGaveItsBytesBack_ShowsAgainFromTheDiskCacheWithoutARequest()
    {
        var png = await HeadlessApp.RunAsync(() => Task.FromResult(NoisyIconPng(0)));
        var host = new IconHost(new Dictionary<string, byte[]> { [IconUrl(0)] = png });
        var cacheRoot = Path.Combine(Path.GetTempPath(), "BoreaAppTest_" + Guid.NewGuid());
        try
        {
            var source = new ContentImageSource(new FileContentImageCache(new GamePathProvider(null, boreaRoot: cacheRoot)), host, TimeSpan.FromSeconds(30));
            using var harness = await ViewModelHarness.CreateAsync(images: source);
            harness.ViewModel.IdleIcons = new IdleIconBytes(0);
            var shown = await HeadlessApp.RunAsync(harness, async () =>
            {
                var icon = Row(harness.ViewModel, 0, png).Icon!;
                var window = new Window { Width = 40, Height = 40 };
                window.Show();

                async Task ShowAsync()
                {
                    window.Content = new ListingImageView { Image = icon, Child = new Border() };
                    window.UpdateLayout();
                    while (!icon.IsLoaded || ((ListingImageView)window.Content).State != ListingImageView.DisplayState.Loaded)
                        await Task.Delay(5);
                }

                await ShowAsync();
                window.Content = null;
                var gaveItsBytesBack = !icon.IsLoaded;
                ListingImageView.Shelf.Take((IconImage)icon.Record)?.Dispose();

                await ShowAsync();
                window.Close();
                return gaveItsBytesBack;
            });

            Assert.True(shown, "The icon kept its file when no view showed it.");
            Assert.Equal([IconUrl(0)], host.Requests);
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
        }
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

    [Fact]
    public async Task LongList_ScrolledFromTheEndToTheTop_ReusesTheRowsItBuilt()
    {
        var scrolled = await HeadlessApp.RunAsync(() =>
        {
            var (_, page, window, scroller) = ShowLongList();
            var list = page.GetVisualDescendants().OfType<ItemsControl>().First(control => control.Classes.Contains("rows"));
            window.UpdateLayout();

            // the list learns its height while it scrolls, so the end moves until the last row is in view
            while (scroller.Offset.Y + scroller.Viewport.Height < scroller.Extent.Height - 1)
            {
                scroller.Offset = scroller.Offset.WithY(scroller.Extent.Height);
                window.UpdateLayout();
            }

            int built = 0, styled = 0;
            using var builds = TemplatedControl.TemplateAppliedEvent.AddClassHandler<Button>((button, _) =>
            {
                if (button.Classes.Contains("card-button"))
                    built++;
            });

            // a row takes the styles of the page again each time the list shows it
            list.ContainerPrepared += (_, _) => styled++;
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            while (scroller.Offset.Y > 0)
            {
                scroller.Offset = scroller.Offset.WithY(Math.Max(0, scroller.Offset.Y - scroller.Viewport.Height));
                window.UpdateLayout();
            }

            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            var top = RowsInView(page, scroller).FirstOrDefault();
            window.Close();
            return Task.FromResult((built, styled, allocated, top));
        });

        Assert.Equal("Mod 000", scrolled.top);
        Assert.InRange(scrolled.built, 0, MaxRealizedRows);
        Assert.InRange(scrolled.styled, Listings - MaxRealizedRows, Listings);
        Assert.InRange(scrolled.allocated / scrolled.styled, 1, MaxBytesPerShownRow);
    }

    [Fact]
    public async Task ShortList_ScrolledToTheEndAndBack_KeepsEveryRow()
    {
        var recycled = await HeadlessApp.RunAsync(() =>
        {
            var (_, page, window, scroller) = ShowLongList(ShortListings);
            var list = page.GetVisualDescendants().OfType<ItemsControl>().First(control => control.Classes.Contains("rows"));
            window.UpdateLayout();
            var cleared = 0;
            list.ContainerClearing += (_, _) => cleared++;
            while (scroller.Offset.Y + scroller.Viewport.Height < scroller.Extent.Height - 1)
            {
                scroller.Offset = scroller.Offset.WithY(scroller.Offset.Y + scroller.Viewport.Height);
                window.UpdateLayout();
            }

            while (scroller.Offset.Y > 0)
            {
                scroller.Offset = scroller.Offset.WithY(Math.Max(0, scroller.Offset.Y - scroller.Viewport.Height));
                window.UpdateLayout();
            }

            window.Close();
            return Task.FromResult(cleared);
        });

        Assert.Equal(0, recycled);
    }

    private static (MainViewModel ViewModel, DiscoverPage Page, Window Window, ScrollViewer Scroller) ShowLongList(int listings = Listings)
    {
        var viewModel = new MainViewModel();
        var png = IconPng();
        for (var number = 0; number < listings; number++)
        {
            var row = Row(viewModel, number, png);
            row.Icon!.Bytes = [.. png];
            viewModel.DiscoverItems.Add(row);
        }

        var (page, window, scroller) = Show(viewModel);
        return (viewModel, page, window, scroller);
    }

    private static (DiscoverPage Page, Window Window, ScrollViewer Scroller) Show(MainViewModel viewModel)
    {
        var page = new DiscoverPage { DataContext = viewModel };
        var window = new Window { Width = 1280, Height = 832, Content = page, DataContext = viewModel };
        window.Show();
        return (page, window, page.GetVisualDescendants().OfType<ScrollViewer>().First());
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
        var icon = new IconImage(IconUrl(number), Convert.ToHexString(SHA256.HashData(png)), 256, 256, png.Length);
        return new DiscoverItem(owner, listing, new ContentIndexListing(id, listing, [], indexStatus: null, images: new ContentImages(icon, [])));
    }

    private static string IconUrl(int number) => $"https://images.example/Mod{number:D3}.png";

    /// <summary>A file of its own for each number, of about 50 KiB because the noise in its top quarter does not compress.</summary>
    private static byte[] NoisyIconPng(int number)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(256, 256), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Opaque);
        using (var buffer = bitmap.Lock())
        {
            var noise = new byte[buffer.RowBytes * 64];
            new Random(number).NextBytes(noise);
            Marshal.Copy(noise, 0, buffer.Address, noise.Length);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
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

    /// <summary>Serves the icon files by their URL and records every request.</summary>
    private sealed class IconHost(IReadOnlyDictionary<string, byte[]> files) : HttpMessageHandler
    {
        public ConcurrentQueue<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Requests.Enqueue(url);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(files[url]) });
        }
    }

    private sealed class MemoryImageCache : IContentImageCache
    {
        private readonly ConcurrentDictionary<string, byte[]> _entries = new(StringComparer.OrdinalIgnoreCase);

        public Task<byte[]?> ReadAsync(string sha256, CancellationToken cancellationToken = default) =>
            Task.FromResult(_entries.TryGetValue(sha256, out var bytes) ? bytes : null);

        public Task WriteAsync(string sha256, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
        {
            _entries[sha256] = bytes.ToArray();
            return Task.CompletedTask;
        }
    }
}
