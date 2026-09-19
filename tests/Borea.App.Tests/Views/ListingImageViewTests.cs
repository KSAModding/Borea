using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.Core.Index;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
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
        var session = HeadlessApp.Session;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var pixels = await session.Dispatch(async () =>
        {
            var image = new ListingImage(new MainViewModel(), Icon(256, 256));
            var view = new ListingImageView
            {
                Image = image,
                Background = Brushes.Red,
                PulseBrush = Brushes.Red,
                CornerRadius = new CornerRadius(12),
                Child = new Border(),
            };
            var window = new Window { Width = 40, Height = 40, Background = Brushes.White, Content = view };
            window.Show();

            async Task<(Color TopLeft, Color TopRight, Color Left, Color Right)> RenderFrame()
            {
                await Task.Delay(10, timeout.Token);
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame()!;
                using var buffer = frame.Lock();
                return (Pixel(buffer, 1, 1), Pixel(buffer, 38, 1), Pixel(buffer, 10, 20), Pixel(buffer, 30, 20));
            }

            var skeleton = await RenderFrame();
            image.Bytes = LeftHalfBluePng();

            // the skeleton fades out over the image, so render until the image shows through
            var loaded = await RenderFrame();
            while (loaded.Left != Colors.Blue)
                loaded = await RenderFrame();

            return (Skeleton: skeleton, Loaded: loaded);
        }, timeout.Token);

        Assert.Equal((Colors.White, Colors.White, Colors.Red, Colors.Red), pixels.Skeleton);
        Assert.Equal((Colors.White, Colors.White, Colors.Blue, Colors.Red), pixels.Loaded);
    }

    [Fact]
    public async Task Render_LoadingImage_PulsesTheSkeletonFromTheBackgroundToThePulseBrush()
    {
        var session = HeadlessApp.Session;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var first = await session.Dispatch(async () =>
        {
            var view = new ListingImageView
            {
                Image = new ListingImage(new MainViewModel(), Icon(256, 256)),
                Background = Brushes.Red,
                PulseBrush = Brushes.Blue,
                Child = new Border(),
            };
            var window = new Window { Width = 40, Height = 40, Content = view };
            window.Show();

            // the reference frame is taken before the first wait, because the pulse rises only after its delay
            var background = Frame(window);
            while (Frame(window).B == 0)
                await Task.Delay(10, timeout.Token);

            return background;
        }, timeout.Token);

        Assert.Equal(Colors.Red, first);
    }

    [Fact]
    public async Task State_ImageLoads_GoesFromThePulsingSkeletonToTheImage()
    {
        var session = HeadlessApp.Session;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var states = await session.Dispatch(async () =>
        {
            var image = new ListingImage(new MainViewModel(), Icon(256, 256));
            var view = Show(image);
            var loading = Snapshot(view);

            image.Bytes = LeftHalfBluePng();
            while (view.State == ListingImageView.DisplayState.Loading)
                await Task.Delay(10, timeout.Token);

            return (Loading: loading, Loaded: Snapshot(view));
        }, timeout.Token);

        Assert.Equal((ListingImageView.DisplayState.Loading, true, false), states.Loading);
        Assert.Equal((ListingImageView.DisplayState.Loaded, false, false), states.Loaded);
    }

    [Fact]
    public async Task State_ImageFails_GoesFromThePulsingSkeletonToThePlaceholder()
    {
        var session = HeadlessApp.Session;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var states = await session.Dispatch(() =>
        {
            var image = new ListingImage(new MainViewModel(), Icon(256, 256));
            var view = Show(image);
            var loading = Snapshot(view);

            image.Failure = ContentImageFailure.NotFound;

            return (Loading: loading, Failed: Snapshot(view));
        }, timeout.Token);

        Assert.Equal((ListingImageView.DisplayState.Loading, true, false), states.Loading);
        Assert.Equal((ListingImageView.DisplayState.Placeholder, false, true), states.Failed);
    }

    [Fact]
    public async Task State_BytesDoNotDecode_GoesFromThePulsingSkeletonToThePlaceholder()
    {
        var session = HeadlessApp.Session;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var states = await session.Dispatch(async () =>
        {
            var image = new ListingImage(new MainViewModel(), Icon(256, 256));
            var view = Show(image);

            image.Bytes = [1, 2, 3, 4];
            var decoding = Snapshot(view);
            while (view.State == ListingImageView.DisplayState.Loading)
                await Task.Delay(10, timeout.Token);

            return (Decoding: decoding, Failed: Snapshot(view));
        }, timeout.Token);

        Assert.Equal((ListingImageView.DisplayState.Loading, true, false), states.Decoding);
        Assert.Equal((ListingImageView.DisplayState.Placeholder, false, true), states.Failed);
    }

    [Fact]
    public async Task State_ImagesTurnedOff_ShowsThePlaceholderAtOnceAndACachedImageWithoutPulse()
    {
        var session = HeadlessApp.Session;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var states = await session.Dispatch(async () =>
        {
            var image = new ListingImage(new MainViewModel { LoadImagesFromAuthorHosts = false }, Icon(256, 256));
            var view = Show(image);
            var turnedOff = Snapshot(view);

            image.Bytes = LeftHalfBluePng();
            var decoding = Snapshot(view);
            while (view.State != ListingImageView.DisplayState.Loaded)
                await Task.Delay(10, timeout.Token);

            return (TurnedOff: turnedOff, Decoding: decoding, Cached: Snapshot(view));
        }, timeout.Token);

        Assert.Equal((ListingImageView.DisplayState.Placeholder, false, true), states.TurnedOff);
        Assert.Equal((ListingImageView.DisplayState.Placeholder, false, true), states.Decoding);
        Assert.Equal((ListingImageView.DisplayState.Loaded, false, false), states.Cached);
    }

    [Fact]
    public async Task IsPulsing_DetachedWhileLoading_StopsAndStartsAgainWhenAttached()
    {
        var session = HeadlessApp.Session;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var pulsing = await session.Dispatch(() =>
        {
            var view = Show(new ListingImage(new MainViewModel(), Icon(256, 256)));
            var window = (Window)TopLevel.GetTopLevel(view)!;
            var attached = view.IsPulsing;

            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            var detached = view.IsPulsing;

            window.Content = view;
            window.UpdateLayout();

            return (attached, detached, view.IsPulsing);
        }, timeout.Token);

        Assert.Equal((true, false, true), pulsing);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListRow_ComesBackOrMoves_ShowsItsImageInTheFirstFrame(bool move)
    {
        var session = HeadlessApp.Session;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var shown = await session.Dispatch(async () =>
        {
            var owner = new MainViewModel();
            var first = new ListingImage(owner, Icon(256, 256)) { Bytes = LeftHalfBluePng() };
            var second = new ListingImage(owner, Icon(256, 256)) { Bytes = LeftHalfBluePng() };
            var rows = new ObservableCollection<ListingImage> { first, second };
            var list = new ItemsControl
            {
                ItemsSource = rows,
                ItemTemplate = new FuncDataTemplate<ListingImage>((image, _) => new ListingImageView { Image = image, Background = Brushes.Red, Width = 40, Height = 40, Child = new Border() }),
            };
            var window = new Window { Width = 40, Height = 80, Content = list };
            window.Show();
            ListingImageView ViewOf(ListingImage image) => list.GetVisualDescendants().OfType<ListingImageView>().Single(view => view.Image == image);
            while (ViewOf(first).State != ListingImageView.DisplayState.Loaded || ViewOf(second).State != ListingImageView.DisplayState.Loaded)
                await Task.Delay(10, timeout.Token);
            var before = ViewOf(second);

            if (move)
            {
                rows.Move(1, 0);
            }
            else
            {
                rows.Remove(second);
                window.UpdateLayout();
                rows.Add(second);
            }

            window.UpdateLayout();
            var after = ViewOf(second);
            var at = after.TranslatePoint(new Point(10, 20), window)!.Value;
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame()!;
            using var buffer = frame.Lock();
            return (NewView: !ReferenceEquals(before, after), after.State, Pixel(buffer, (int)at.X, (int)at.Y));
        }, timeout.Token);

        Assert.Equal((true, ListingImageView.DisplayState.Loaded, Colors.Blue), shown);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task State_ImageShownAgainInANewView_TakesOnlyAnIconFromTheShelf(bool icon)
    {
        var session = HeadlessApp.Session;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var state = await session.Dispatch(async () =>
        {
            ContentImage record = icon ? Icon(256, 256) : new DescriptionImage("shot", Url, Digest, 256, 256, 1000);
            var image = new ListingImage(new MainViewModel(), record) { Bytes = LeftHalfBluePng() };
            var first = Show(image);
            while (first.State != ListingImageView.DisplayState.Loaded)
                await Task.Delay(10, timeout.Token);

            var window = (Window)TopLevel.GetTopLevel(first)!;
            window.Content = null;
            window.Content = new ListingImageView { Image = image, Child = new Border() };
            window.UpdateLayout();

            return ((ListingImageView)window.Content).State;
        }, timeout.Token);

        Assert.Equal(icon ? ListingImageView.DisplayState.Loaded : ListingImageView.DisplayState.Loading, state);
    }

    [Fact]
    public async Task Show_BeforeTheFirstLayout_WaitsAndDecodesAtTheSlotWidth()
    {
        var session = HeadlessApp.Session;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var decoded = await session.Dispatch(async () =>
        {
            var image = new ListingImage(new MainViewModel(), Icon(256, 256)) { Bytes = LeftHalfBluePng() };
            var view = new ListingImageView { Image = image, Child = new Border() };
            var slot = new Border { Width = 0, Height = 40, Child = view };
            var window = new Window { Width = 80, Height = 40, Content = slot };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var unsized = (view.State, view.BitmapWidth);

            slot.Width = 40;
            window.UpdateLayout();
            while (view.State != ListingImageView.DisplayState.Loaded)
                await Task.Delay(10, timeout.Token);

            return (Unsized: unsized, view.BitmapWidth, Expected: ListingImageView.DecodeWidth(image.Record, view.Bounds.Size, window.RenderScaling));
        }, timeout.Token);

        Assert.Equal((ListingImageView.DisplayState.Loading, (int?)null), decoded.Unsized);
        Assert.Equal(decoded.Expected, decoded.BitmapWidth);
        Assert.True(decoded.BitmapWidth < 256);
    }

    private static ListingImageView Show(ListingImage image)
    {
        var view = new ListingImageView { Image = image, Child = new Border() };
        new Window { Width = 40, Height = 40, Content = view }.Show();
        Dispatcher.UIThread.RunJobs();
        return view;
    }

    private static (ListingImageView.DisplayState State, bool IsPulsing, bool ShowsPlaceholder) Snapshot(ListingImageView view) =>
        (view.State, view.IsPulsing, view.Child!.IsVisible);

    private static byte[] LeftHalfBluePng()
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(256, 256));
        using (var context = bitmap.CreateDrawingContext())
            context.FillRectangle(Brushes.Blue, new Rect(0, 0, 128, 256));

        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    private static Color Frame(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        return Pixel(buffer, 20, 20);
    }

    private static Color Pixel(ILockedFramebuffer buffer, int x, int y)
    {
        var offset = y * buffer.RowBytes + x * 4;
        return Color.FromRgb(Marshal.ReadByte(buffer.Address, offset), Marshal.ReadByte(buffer.Address, offset + 1), Marshal.ReadByte(buffer.Address, offset + 2));
    }

    private static IconImage Icon(int width, int height) => new(Url, Digest, width, height, 1000);
}
