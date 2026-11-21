using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Borea.App.Views;
using Borea.Core.Screenshots;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class ScreenshotThumbnailCacheTests
{
    [Fact]
    public async Task Release_ThumbnailAboveTheSize_LeavesOnlyWhenNoTileShowsIt()
    {
        var counts = await HeadlessApp.Session.Dispatch(() =>
        {
            var cache = new ScreenshotThumbnailCache(capacity: 0);
            var screenshot = new Screenshot("ksa_20261120_100000_8x8.png", new DateTime(2026, 11, 20, 10, 0, 0), 8, 8, 100);
            cache.Add(screenshot, new WriteableBitmap(new PixelSize(8, 8), new Vector(96, 96), PixelFormat.Rgba8888));
            cache.Acquire(screenshot);
            cache.Release(screenshot);
            var shownByOne = cache.Count;
            cache.Release(screenshot);

            return (shownByOne, cache.Count, cache.Size);
        }, CancellationToken.None);

        Assert.Equal((1, 0, 0L), counts);
    }
}
