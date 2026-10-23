using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Borea.App.Views.Preview;
using Borea.Preview.Rendering;
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

    [Theory]
    [InlineData(MouseButton.Left, RawInputModifiers.None)]
    [InlineData(MouseButton.Right, RawInputModifiers.None)]
    [InlineData(MouseButton.Left, RawInputModifiers.Shift)]
    [InlineData(MouseButton.Right, RawInputModifiers.Shift)]
    public async Task ALeftOrARightDrag_TurnsTheView_AtDragResolution_AndOpensNoContextMenu(MouseButton button, RawInputModifiers modifiers)
    {
        var result = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(PreviewTestScenes.StackedBoxes());
            var requests = 0;
            window.ContextRequested += (_, _) => requests++;
            await IdleAsync(view, 1);
            var before = Capture(window);
            var distance = view.CameraDistance;

            // The move goes past the right edge of the window, which only a captured pointer still sends to the view.
            window.MouseDown(Center, button, modifiers);
            window.MouseMove(Center + new Point(100, 10), modifiers);
            await IdleAsync(view, 2);
            var dragWidth = view.BitmapWidth;
            window.MouseUp(Center + new Point(100, 10), button, modifiers);
            await IdleAsync(view, 3);
            return (
                Changed: !before.SequenceEqual(Capture(window)),
                view.CameraYaw,
                Kept: (view.CameraAxisOffset, view.CameraDistance == distance),
                DragWidth: dragWidth,
                FullWidth: view.BitmapWidth,
                Requests: requests);
        });

        Assert.True(result.Changed);
        Assert.Equal(OrbitCamera.DefaultYaw + 100f * OrbitCamera.RadiansPerPixel, result.CameraYaw, 1e-5f);
        Assert.Equal((0f, true), result.Kept);
        Assert.Equal(Width / 2, result.DragWidth);
        Assert.Equal(Width, result.FullWidth);
        Assert.Equal(0, result.Requests);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(-30)]
    public async Task AMiddleDrag_ZoomsAsTheGameDoes_AwayForADragDown(int dragY)
    {
        var result = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(PreviewTestScenes.StackedBoxes());
            await IdleAsync(view, 1);
            var framed = view.CameraDistance;
            window.MouseDown(Center, MouseButton.Middle);
            window.MouseMove(Center + new Point(40, dragY));
            await IdleAsync(view, 2);
            var dragWidth = view.BitmapWidth;
            window.MouseUp(Center + new Point(40, dragY), MouseButton.Middle);
            await IdleAsync(view, 3);
            return (Framed: framed, view.CameraDistance, view.CameraYaw, view.CameraAxisOffset, DragWidth: dragWidth, FullWidth: view.BitmapWidth);
        });

        Assert.Equal(result.Framed * MathF.Pow(1.1f, dragY * 0.1f), result.CameraDistance, result.Framed * 1e-4f);
        Assert.Equal((OrbitCamera.DefaultYaw, 0f), (result.CameraYaw, result.CameraAxisOffset));
        Assert.Equal(Width / 2, result.DragWidth);
        Assert.Equal(Width, result.FullWidth);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task TheWheel_MovesTheTargetAlongTheAxis_ByATenthOfTheViewHeight(int notches)
    {
        var scene = PreviewTestScenes.StackedBoxes();
        var result = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(scene);
            await IdleAsync(view, 1);
            window.MouseWheel(Center, new Avalonia.Vector(0, notches));
            return (view.CameraAxisOffset, view.CameraDistance, view.CameraYaw);
        });

        var framed = Framed(scene);
        Assert.Equal(notches * framed.ViewHeight * VehiclePreviewView.AxisStepFraction, result.CameraAxisOffset, 1e-5f);
        Assert.Equal((framed.Distance, OrbitCamera.DefaultYaw), (result.CameraDistance, result.CameraYaw));
    }

    [Fact]
    public async Task TheWheel_StopsTheTargetAtBothEndsOfTheVehicle_AndAStepBackRespondsAtOnce()
    {
        var result = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(PreviewTestScenes.StackedBoxes());
            await IdleAsync(view, 1);
            window.MouseWheel(Center, new Avalonia.Vector(0, 200));
            var top = view.CameraAxisOffset;
            window.MouseWheel(Center, new Avalonia.Vector(0, -1));
            var back = view.CameraAxisOffset;
            window.MouseWheel(Center, new Avalonia.Vector(0, -400));
            return (Reach: view.CameraAxisReach, Top: top, Back: back, Bottom: view.CameraAxisOffset);
        });

        Assert.True(result.Reach > 0f);
        Assert.Equal(result.Reach, result.Top);
        Assert.InRange(result.Back, 0f, result.Reach * 0.99f);
        Assert.Equal(-result.Reach, result.Bottom);
    }

    [Fact]
    public async Task TheWheelWithShift_Zooms_AndAShiftedSidewaysWheelZoomsToo()
    {
        var result = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(PreviewTestScenes.StackedBoxes());
            await IdleAsync(view, 1);
            var framed = view.CameraDistance;
            window.MouseWheel(Center, new Avalonia.Vector(0, 2), RawInputModifiers.Shift);
            var closer = view.CameraDistance;
            window.MouseWheel(Center, new Avalonia.Vector(-3, 0), RawInputModifiers.Shift);
            return (Framed: framed, Closer: closer, Farther: view.CameraDistance, view.CameraAxisOffset);
        });

        Assert.Equal(result.Framed * OrbitCamera.ZoomStepFactor * OrbitCamera.ZoomStepFactor, result.Closer, result.Framed * 1e-5f);
        Assert.Equal(result.Framed / OrbitCamera.ZoomStepFactor, result.Farther, result.Framed * 1e-5f);
        Assert.Equal(0f, result.CameraAxisOffset);
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
    public async Task ATexturedCopyOfTheScene_KeepsThePoseAndTheShownFrame()
    {
        var textured = Box(new Vector3(0f, 0f, 1f));
        var result = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(Box(new Vector3(1f, 0f, 0f)));
            await IdleAsync(view, 1);
            view.Focus();
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            window.MouseWheel(Center, new Avalonia.Vector(0, 2), RawInputModifiers.Shift);
            window.MouseWheel(Center, new Avalonia.Vector(0, 1));
            await IdleAsync(view, 2);
            var pose = (view.CameraYaw, view.CameraDistance, view.CameraAxisOffset);
            var shown = view.FramesShown;

            // the same box with another material has the same bounds, as the textured scene after the flat one has
            view.Scene = textured;
            var keptFrame = view.BitmapWidth is not null;
            await IdleAsync(view, shown + 1);
            return (Before: pose, After: (view.CameraYaw, view.CameraDistance, view.CameraAxisOffset), KeptFrame: keptFrame, view.ShownScene);
        });

        Assert.Equal(result.Before, result.After);
        Assert.NotEqual(OrbitCamera.DefaultYaw, result.After.CameraYaw);
        Assert.NotEqual(0f, result.After.CameraAxisOffset);
        Assert.True(result.KeptFrame);
        Assert.Same(textured, result.ShownScene);
    }

    [Fact]
    public async Task ATexturedCopyAfterAHighFrame_ShowsNoStandardFrame_AndEndsAtHigh()
    {
        var textured = Box(new Vector3(0f, 0f, 1f));
        var result = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(Box(new Vector3(1f, 0f, 0f)));
            view.RefineDelay = TimeSpan.FromMilliseconds(20);
            await HeadlessApp.FramesUntilAsync(() => view.IsIdle && view.ShownQuality == RenderQuality.High);
            var (shown, high) = (view.FramesShown, view.HighFramesShown);

            view.Scene = textured;
            await HeadlessApp.FramesUntilAsync(() => view.IsIdle && view.ShownScene == textured);
            await HeadlessApp.FramesAsync(5);
            return (Standard: view.FramesShown - shown, High: view.HighFramesShown - high, view.ShownQuality, view.ShownScene, Center: Pixel(Capture(window), Center));
        });

        Assert.Equal((0, 1, RenderQuality.High), (result.Standard, result.High, result.ShownQuality));
        Assert.Same(textured, result.ShownScene);
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

    [Fact]
    public async Task AFullFrameAtRest_IsRefinedToHigh()
    {
        var result = await HeadlessApp.RunAsync(async () =>
        {
            var (_, view) = Show(PreviewTestScenes.StackedBoxes());
            view.RefineDelay = TimeSpan.FromMilliseconds(20);
            await HeadlessApp.FramesUntilAsync(() => view.IsIdle && view.ShownQuality == RenderQuality.High);
            return (view.ShownQuality, view.FramesShown, view.HighFramesShown, view.BitmapWidth);
        });

        Assert.Equal((RenderQuality.High, 1, 1, (int?)Width), result);
    }

    [Fact]
    public async Task APressBeforeTheRefine_KeepsStandard_AndInputAfterAHighFrameShowsStandardFrames()
    {
        var result = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(PreviewTestScenes.StackedBoxes());
            view.RefineDelay = TimeSpan.FromMilliseconds(30);
            await IdleAsync(view, 1);

            // Nothing runs the dispatcher between the first frame and the press, so the timer cannot fire before it.
            window.MouseDown(Center, MouseButton.Left);
            window.MouseMove(Center + new Point(20, 0));
            await IdleAsync(view, 2);
            await HeadlessApp.FramesAsync(10);
            var held = (view.ShownQuality, view.HighFramesShown, view.BitmapWidth);

            window.MouseUp(Center + new Point(20, 0), MouseButton.Left);
            await HeadlessApp.FramesUntilAsync(() => view.IsIdle && view.ShownQuality == RenderQuality.High);
            var refined = view.ShownQuality;
            var shown = view.FramesShown;

            // The press is away from the first one, so it is not a double click, which would reset the view.
            var from = Center + new Point(-30, -10);
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(from + new Point(-20, 5));
            window.MouseWheel(from, new Avalonia.Vector(0, 1));
            await IdleAsync(view, shown + 1);
            return (Held: held, Refined: refined, After: (view.ShownQuality, view.BitmapWidth));
        });

        Assert.Equal((RenderQuality.Standard, 0, (int?)(Width / 2)), result.Held);
        Assert.Equal(RenderQuality.High, result.Refined);
        Assert.Equal((RenderQuality.Standard, (int?)(Width / 2)), result.After);
    }

    [Fact]
    public async Task ADragThatKeepsMoving_ShowsFrames_AndRecordsEachOne()
    {
        var result = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(PreviewTestScenes.StackedBoxes());
            view.RefineDelay = TimeSpan.FromMilliseconds(10);
            await IdleAsync(view, 1);
            var before = view.FramesShown;

            // Each move makes a new camera, most of them while the frame of the last one still renders.
            window.MouseDown(Center, MouseButton.Left);
            for (var step = 1; step <= 30; step++)
            {
                window.MouseMove(Center + new Point(step, 0));
                await HeadlessApp.FramesAsync(1);
            }

            await HeadlessApp.FramesUntilAsync(() => view.IsIdle);
            var during = view.FramesShown - before;
            var recorded = view.DragFramesRecorded;
            return (During: during, Recorded: recorded, view.ShownQuality);
        });

        Assert.True(result.During > 1, $"The drag showed {result.During} frames.");
        Assert.Equal(result.During, result.Recorded);
        Assert.Equal(RenderQuality.Standard, result.ShownQuality);
    }

    [Theory]
    [InlineData(PhysicalKey.Q, 1f)]
    [InlineData(PhysicalKey.E, -1f)]
    [InlineData(PhysicalKey.A, 0f)]
    [InlineData(PhysicalKey.D, 0f)]
    public async Task QAndE_MoveTheTargetAlongTheAxis_AndAAndDDoNothing(PhysicalKey key, float steps)
    {
        var scene = PreviewTestScenes.StackedBoxes();
        var result = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(scene);
            await IdleAsync(view, 1);
            view.Focus();
            window.KeyPressQwerty(key, RawInputModifiers.None);
            await HeadlessApp.FramesUntilAsync(() => view.IsIdle);
            return (view.CameraAxisOffset, view.CameraDistance, view.CameraYaw, view.FramesShown);
        });

        var framed = Framed(scene);
        Assert.Equal(steps * framed.ViewHeight * VehiclePreviewView.AxisStepFraction, result.CameraAxisOffset, 1e-5f);
        Assert.Equal((framed.Distance, OrbitCamera.DefaultYaw), (result.CameraDistance, result.CameraYaw));
        Assert.Equal(steps == 0f ? 1 : 2, result.FramesShown);
    }

    [Fact]
    public async Task WAndS_ZoomOneStep_AndAKeyWithAModifierDoesNothing()
    {
        var result = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(PreviewTestScenes.StackedBoxes());
            await IdleAsync(view, 1);
            var framed = view.CameraDistance;
            view.Focus();
            window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.None);
            var closer = view.CameraDistance;
            window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.None);
            var farther = view.CameraDistance;
            window.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.Control);
            return (Framed: framed, Closer: closer, Farther: farther, view.CameraAxisOffset);
        });

        Assert.Equal(result.Framed * OrbitCamera.ZoomStepFactor, result.Closer, result.Framed * 1e-5f);
        Assert.Equal(result.Framed / OrbitCamera.ZoomStepFactor, result.Farther, result.Framed * 1e-5f);
        Assert.Equal(0f, result.CameraAxisOffset);
    }

    [Theory]
    [InlineData(PhysicalKey.F)]
    [InlineData(PhysicalKey.Home)]
    [InlineData(PhysicalKey.None)]
    public async Task FHomeAndADoubleClick_FrameTheVehicleAgain(PhysicalKey key)
    {
        var result = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(PreviewTestScenes.StackedBoxes());
            await IdleAsync(view, 1);
            var framed = view.CameraDistance;
            view.Focus();
            window.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
            var moved = view.CameraAxisOffset != 0f && view.CameraDistance < framed && view.CameraYaw != OrbitCamera.DefaultYaw;

            // PhysicalKey.None stands for the double click with the left button.
            if (key == PhysicalKey.None)
            {
                window.MouseDown(Center, MouseButton.Left);
                window.MouseUp(Center, MouseButton.Left);
                window.MouseDown(Center, MouseButton.Left);
                window.MouseUp(Center, MouseButton.Left);
            }
            else
            {
                window.KeyPressQwerty(key, RawInputModifiers.None);
            }

            await HeadlessApp.FramesUntilAsync(() => view.IsIdle);
            return (Moved: moved, view.CameraAxisOffset, Framed: view.CameraDistance == framed, view.CameraYaw);
        });

        Assert.True(result.Moved);
        Assert.Equal((0f, true, OrbitCamera.DefaultYaw), (result.CameraAxisOffset, result.Framed, result.CameraYaw));
    }

    [Fact]
    public async Task ALeftClickAfterARightClick_IsNoDoubleClick_ButASecondLeftClickIs()
    {
        var result = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(PreviewTestScenes.StackedBoxes());
            await IdleAsync(view, 1);
            var framed = view.CameraDistance;
            view.Focus();
            window.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.None);
            var (offset, distance) = (view.CameraAxisOffset, view.CameraDistance);

            // Avalonia counts the presses of all buttons in one click count, so the left press has a count of 2.
            window.MouseDown(Center, MouseButton.Right);
            window.MouseUp(Center, MouseButton.Right);
            window.MouseDown(Center, MouseButton.Left);
            window.MouseUp(Center, MouseButton.Left);
            var kept = view.CameraAxisOffset == offset && view.CameraDistance == distance;

            window.MouseDown(Center, MouseButton.Left);
            window.MouseUp(Center, MouseButton.Left);
            await HeadlessApp.FramesUntilAsync(() => view.IsIdle);
            return (Kept: kept, Moved: offset != 0f && distance < framed, view.CameraAxisOffset, Framed: view.CameraDistance == framed);
        });

        Assert.True(result.Moved);
        Assert.True(result.Kept);
        Assert.Equal((0f, true), (result.CameraAxisOffset, result.Framed));
    }

    [Fact]
    public async Task TheWheelWithShift_ZoomsToTheNearLimit_AndAStepBackRespondsAtOnce()
    {
        var result = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(PreviewTestScenes.StackedBoxes());
            await IdleAsync(view, 1);
            var framed = view.CameraDistance;
            window.MouseWheel(Center, new Avalonia.Vector(0, 200), RawInputModifiers.Shift);
            var nearest = view.CameraDistance;
            window.MouseWheel(Center, new Avalonia.Vector(0, -1), RawInputModifiers.Shift);
            return (Framed: framed, Nearest: nearest, Back: view.CameraDistance);
        });

        Assert.Equal(result.Framed * OrbitCamera.MinZoomFactor, result.Nearest, result.Framed * 1e-5f);
        Assert.Equal(result.Nearest / OrbitCamera.ZoomStepFactor, result.Back, result.Framed * 1e-5f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AMoveOfTheTargetAfterAHighFrame_ShowsAStandardFrame_AndRefinesAgain(bool wheel)
    {
        var result = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(PreviewTestScenes.StackedBoxes());
            view.RefineDelay = TimeSpan.FromMilliseconds(20);
            await HeadlessApp.FramesUntilAsync(() => view.IsIdle && view.ShownQuality == RenderQuality.High);
            var (shown, high) = (view.FramesShown, view.HighFramesShown);

            view.Focus();
            if (wheel)
                window.MouseWheel(Center, new Avalonia.Vector(0, -1));
            else
                window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.None);

            await IdleAsync(view, shown + 1);
            await HeadlessApp.FramesUntilAsync(() => view.IsIdle && view.ShownQuality == RenderQuality.High);
            return (Standard: view.FramesShown - shown, High: view.HighFramesShown - high, view.ShownQuality);
        });

        // one Standard frame of the moved camera, then one High frame of it; the refine delay is too short on a slow
        // machine to read the quality in between, so the counts show the order
        Assert.Equal((1, 1, RenderQuality.High), result);
    }

    [Fact]
    public async Task AMovedTarget_StaysForTheSameVehicle_AndResetsForAResetCountOrAnotherVehicle()
    {
        var textured = Box(new Vector3(0f, 0f, 1f));
        var other = PreviewTestScenes.StackedBoxes();
        var result = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(Box(new Vector3(1f, 0f, 0f)));
            await IdleAsync(view, 1);
            view.Focus();
            window.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.None);
            var moved = view.CameraAxisOffset;

            // the same box with another material has the same bounds, as the textured scene after the flat one has
            view.Scene = textured;
            var kept = view.CameraAxisOffset;
            view.ResetCount++;
            var afterReset = view.CameraAxisOffset;

            window.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.None);
            view.Scene = other;
            var afterOther = view.CameraAxisOffset;
            await HeadlessApp.FramesUntilAsync(() => view.IsIdle);
            return (Moved: moved, Kept: kept, AfterReset: afterReset, AfterOther: afterOther);
        });

        Assert.NotEqual(0f, result.Moved);
        Assert.Equal(result.Moved, result.Kept);
        Assert.Equal(0f, result.AfterReset);
        Assert.Equal(0f, result.AfterOther);
    }

    /// <summary>The camera that the view builds for <paramref name="scene"/> before any input.</summary>
    private static OrbitCamera Framed(PreviewScene scene)
    {
        var camera = new OrbitCamera();
        camera.Frame(scene.Bounds, (float)Width / Height);
        return camera;
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
