using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Borea.App.Views;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class BitmapShelfTests
{
    [Fact]
    public async Task Take_HandsTheBitmapOverOnce()
    {
        var taken = await HeadlessApp.Session.Dispatch(() =>
        {
            var shelf = new BitmapShelf(1024 * 1024);
            byte[] bytes = [1];
            var bitmap = Bitmap(8);
            shelf.Put(bytes, bitmap);

            return (First: ReferenceEquals(bitmap, shelf.Take(bytes)), SecondEmpty: shelf.Take(bytes) is null, shelf.Count, shelf.Size);
        }, CancellationToken.None);

        Assert.Equal((true, true, 0, 0L), taken);
    }

    [Fact]
    public async Task Put_SameBytesTwice_KeepsTheWiderBitmap()
    {
        var kept = await HeadlessApp.Session.Dispatch(() =>
        {
            var shelf = new BitmapShelf(1024 * 1024);
            byte[] bytes = [1];
            var wide = Bitmap(16);
            shelf.Put(bytes, wide);
            shelf.Put(bytes, Bitmap(8));

            return (shelf.Count, Wider: ReferenceEquals(wide, shelf.Take(bytes)));
        }, CancellationToken.None);

        Assert.Equal((1, true), kept);
    }

    [Fact]
    public async Task Put_PastTheCapacity_DropsTheLeastRecentBitmaps()
    {
        var left = await HeadlessApp.Session.Dispatch(() =>
        {
            // each entry is 8 x 8 pixels of 4 bytes, so two fit
            var shelf = new BitmapShelf(2 * 256);
            byte[] oldest = [1];
            byte[] middle = [2];
            byte[] newest = [3];
            shelf.Put(oldest, Bitmap(8));
            shelf.Put(middle, Bitmap(8));
            shelf.Put(newest, Bitmap(8));

            return (shelf.Count, shelf.Size, Oldest: shelf.Take(oldest) is null, Middle: shelf.Take(middle) is not null, Newest: shelf.Take(newest) is not null);
        }, CancellationToken.None);

        Assert.Equal((2, 2 * 256L, true, true, true), left);
    }

    private static WriteableBitmap Bitmap(int side) => new(new PixelSize(side, side), new Vector(96, 96), PixelFormat.Rgba8888);
}
