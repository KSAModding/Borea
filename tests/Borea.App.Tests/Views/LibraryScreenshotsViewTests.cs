using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;
using Borea.Core.Screenshots;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class LibraryScreenshotsViewTests
{
    private const int Screenshots = 3000;

    /// <summary>The window shows about five rows of six tiles and the list keeps two windows more above and below, so this ceiling does not grow with the folder.</summary>
    private const int MaxRealizedTiles = 200;

    [Fact]
    public async Task ThousandsOfScreenshots_BuildOnlyTheTilesNearTheWindow_AndTheNewestShowsItsPicture()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var folder = Directory.CreateDirectory(Path.Combine(harness.Root, "screenshots")).FullName;
        var (tiles, hasPicture) = await HeadlessApp.RunAsync(harness, async () =>
        {
            var picture = Path.Combine(folder, "ksa_20261120_100000_64x36.png");
            using (var bitmap = new WriteableBitmap(new PixelSize(64, 36), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Opaque))
                bitmap.Save(picture, PngBitmapEncoderOptions.Default);

            var viewModel = harness.ViewModel;
            viewModel.LibraryTab = LibraryTab.Screenshots;
            viewModel.ShownScreenshots = Enumerable.Range(0, Screenshots)
                .Select(number => number == 0 ? Screenshot.FromFile(picture, DateTime.Now, 1) : new Screenshot(Path.Combine(folder, $"gone{number}.png"), DateTime.Now, null, null, 1))
                .Select(screenshot => new ScreenshotItem(viewModel, screenshot, new ScreenshotSource(null), () => "Main"))
                .ToList();
            var page = new LibraryPage { DataContext = viewModel };
            var window = new Window { Width = 1280, Height = 832, Content = page, DataContext = viewModel };
            window.Show();

            // the grid learns its width in the first pass and builds its rows in the next
            window.UpdateLayout();
            window.UpdateLayout();
            var thumbnails = page.GetVisualDescendants().OfType<ScreenshotThumbnail>().ToList();
            await Task.WhenAll(thumbnails.Select(thumbnail => thumbnail.Loading));
            var newest = thumbnails.Single(thumbnail => thumbnail.Screenshot?.Path == picture);
            var shown = (thumbnails.Count, newest.HasPicture);
            window.Close();
            return shown;
        });

        Assert.InRange(tiles, 1, MaxRealizedTiles);
        Assert.True(hasPicture);
    }

    [Fact]
    public async Task TileMenu_LanguageChangedAfterTheFirstOpen_OpensInTheNewLanguage()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var localization = harness.Localization;
        var (german, shown) = await HeadlessApp.RunAsync(harness, () =>
        {
            var viewModel = harness.ViewModel;
            var screenshot = new Screenshot(Path.Combine(harness.Root, "ksa_20261120_100000_16x9.png"), DateTime.Now, 16, 9, 1);
            var tile = new ScreenshotTile { DataContext = new ScreenshotItem(viewModel, screenshot, new ScreenshotSource(null), () => "Main") };
            var window = new Window { Width = 400, Height = 300, Content = tile, DataContext = viewModel };
            window.Show();
            try
            {
                tile.RaiseEvent(new ContextRequestedEventArgs());
                var menu = Assert.IsType<MenuFlyout>(tile.ContextFlyout);
                menu.Hide();
                localization.TrySetCulture("de");
                tile.RaiseEvent(new ContextRequestedEventArgs());
                List<object?> german = [localization.ScreenshotOpen, localization.ScreenshotShowInFolder, localization.ScreenshotCopyPath];
                return Task.FromResult((german, menu.Items.Cast<MenuItem>().Select(item => item.Header).ToList()));
            }
            finally
            {
                localization.TrySetCulture("en");
                window.Close();
            }
        });

        Assert.Equal(german, shown);
        Assert.DoesNotContain(localization.ScreenshotOpen, german);
    }
}
