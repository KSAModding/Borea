using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Borea.App.Views.Preview;
using Borea.Preview.Scene;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class VehiclePreviewViewTests
{
    private const int Width = 160;
    private const int Height = 90;
    private static readonly Point Center = new(Width / 2, Height / 2);

    [Fact]
    public async Task Render_DrawsTheSceneInTheMiddle_AndANullSceneClearsToTheBackground()
    {
        var pixels = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(PreviewTestScenes.StackedBoxes());
            await IdleAsync(view, 1);
            var drawn = Capture(window);

            view.Scene = null;
            var cleared = Capture(window);
            return (Center: Pixel(drawn, Center), Corner: Pixel(drawn, new Point(1, 1)), Cleared: Pixel(cleared, Center));
        });

        Assert.NotEqual(Colors.Black, pixels.Center);
        Assert.Equal(Colors.Black, pixels.Corner);
        Assert.Equal(Colors.Black, pixels.Cleared);
    }

    [Fact]
    public async Task Drag_TurnsTheView_AndEndsWithAFullResolutionFrame()
    {
        var result = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(PreviewTestScenes.StackedBoxes());
            await IdleAsync(view, 1);
            var before = Capture(window);

            window.MouseDown(Center, MouseButton.Left);
            window.MouseMove(Center + new Point(40, 10));
            await IdleAsync(view, 2);
            var dragWidth = view.BitmapWidth;
            window.MouseUp(Center + new Point(40, 10), MouseButton.Left);
            await IdleAsync(view, 3);
            return (Changed: !before.SequenceEqual(Capture(window)), DragWidth: dragWidth, FullWidth: view.BitmapWidth);
        });

        Assert.True(result.Changed);
        Assert.Equal(Width / 2, result.DragWidth);
        Assert.Equal(Width, result.FullWidth);
    }

    [Fact]
    public async Task Wheel_ChangesTheCameraDistance()
    {
        var distances = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(PreviewTestScenes.StackedBoxes());
            await IdleAsync(view, 1);
            var before = view.CameraDistance;
            window.MouseWheel(Center, new Avalonia.Vector(0, 2));
            return (Before: before, After: view.CameraDistance);
        });

        Assert.True(distances.After < distances.Before);
    }

    [Fact]
    public async Task ArrowKey_TurnsTheView()
    {
        var changed = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(PreviewTestScenes.StackedBoxes());
            await IdleAsync(view, 1);
            var before = Capture(window);

            view.Focus();
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            await IdleAsync(view, 2);
            return !before.SequenceEqual(Capture(window));
        });

        Assert.True(changed);
    }

    [Fact]
    public async Task ReplacingTheSceneDuringARender_ShowsOnlyTheNewScene()
    {
        var blue = Box(new Vector3(0f, 0f, 1f));
        var result = await HeadlessApp.RunAsync(async () =>
        {
            // The first frame is on the background task when the scene changes, because nothing ran the dispatcher yet.
            var (window, view) = Show(Box(new Vector3(1f, 0f, 0f)));
            view.Scene = blue;
            await IdleAsync(view, 1);
            await HeadlessApp.FramesAsync(5);
            return (view.FramesShown, view.ShownScene, Center: Pixel(Capture(window), Center));
        });

        Assert.Equal(1, result.FramesShown);
        Assert.Same(blue, result.ShownScene);
        Assert.True(result.Center.B > result.Center.R);
    }

    [Fact]
    public async Task DetachDuringARender_ShowsNothingAndThrowsNothing()
    {
        var result = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(PreviewTestScenes.StackedBoxes());
            window.Content = null;
            await HeadlessApp.FramesUntilAsync(() => view.IsIdle);
            await HeadlessApp.FramesAsync(5);
            return (view.IsIdle, view.FramesShown, view.BitmapWidth);
        });

        Assert.Equal((true, 0, (int?)null), result);
    }

    private static (Window Window, VehiclePreviewView View) Show(PreviewScene scene)
    {
        var view = new VehiclePreviewView { Scene = scene, Background = Brushes.Black };
        var window = new Window { Width = Width, Height = Height, Background = Brushes.White, Content = view };
        window.Show();
        return (window, view);
    }

    private static PreviewScene Box(Vector3 color) =>
        new([new PreviewDraw(PreviewTestScenes.CreateBox(), PreviewMaterial.Flat(color), Matrix4x4.Identity)], []);

    private static async Task IdleAsync(VehiclePreviewView view, int frames)
    {
        await HeadlessApp.FramesUntilAsync(() => view.IsIdle && view.FramesShown >= frames);
        Assert.True(view.IsIdle && view.FramesShown >= frames, $"The view showed {view.FramesShown} frames, expected {frames}.");
    }

    private static byte[] Capture(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        var pixels = new byte[Width * Height * 4];
        for (var row = 0; row < Height; row++)
            Marshal.Copy(buffer.Address + row * buffer.RowBytes, pixels, row * Width * 4, Width * 4);
        return pixels;
    }

    private static Color Pixel(byte[] pixels, Point at)
    {
        var offset = ((int)at.Y * Width + (int)at.X) * 4;
        return Color.FromRgb(pixels[offset], pixels[offset + 1], pixels[offset + 2]);
    }
}
