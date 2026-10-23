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
    private const int PanZoomSteps = 15;
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
    public async Task ATexturedCopyOfTheScene_KeepsThePoseAndTheShownFrame()
    {
        var textured = Box(new Vector3(0f, 0f, 1f));
        var result = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(Box(new Vector3(1f, 0f, 0f)));
            await IdleAsync(view, 1);
            view.Focus();
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            window.MouseWheel(Center, new Avalonia.Vector(0, 2));
            await IdleAsync(view, 2);
            var pose = (view.CameraYaw, view.CameraDistance);
            var shown = view.FramesShown;

            // the same box with another material has the same bounds, as the textured scene after the flat one has
            view.Scene = textured;
            var keptFrame = view.BitmapWidth is not null;
            await IdleAsync(view, shown + 1);
            return (Before: pose, After: (view.CameraYaw, view.CameraDistance), KeptFrame: keptFrame, view.ShownScene);
        });

        Assert.Equal(result.Before, result.After);
        Assert.NotEqual(OrbitCamera.DefaultYaw, result.After.CameraYaw);
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
    [InlineData(MouseButton.Right, RawInputModifiers.None)]
    [InlineData(MouseButton.Middle, RawInputModifiers.None)]
    [InlineData(MouseButton.Left, RawInputModifiers.Shift)]
    public async Task APanDrag_MovesTheTargetWithThePointer_AtDragResolution_AndOpensNoContextMenu(MouseButton button, RawInputModifiers modifiers)
    {
        var scene = PreviewTestScenes.StackedBoxes();
        var result = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(scene);
            var requests = 0;
            window.ContextRequested += (_, _) => requests++;
            await IdleAsync(view, 1);

            // The zoom keeps the shift inside the sphere. The move goes past the right edge of the window, which only
            // a captured pointer still sends to the view.
            window.MouseWheel(Center, new Avalonia.Vector(0, PanZoomSteps));
            window.MouseDown(Center, button, modifiers);
            window.MouseMove(Center + new Point(100, -10), modifiers);
            await IdleAsync(view, 2);
            var dragWidth = view.BitmapWidth;
            window.MouseUp(Center + new Point(100, -10), button, modifiers);
            await IdleAsync(view, 3);
            return (Shift: view.CameraShift, view.CameraYaw, DragWidth: dragWidth, FullWidth: view.BitmapWidth, Requests: requests);
        });

        var camera = Framed(scene);
        camera.Zoom(PanZoomSteps);
        var expected = camera.PanOffset(100f, -10f, Height);
        Assert.True(Vector3.Distance(expected, result.Shift) < expected.Length() * 1e-3f, $"The shift is {result.Shift}, expected {expected}.");
        Assert.Equal(OrbitCamera.DefaultYaw, result.CameraYaw);
        Assert.Equal(Width / 2, result.DragWidth);
        Assert.Equal(Width, result.FullWidth);
        Assert.Equal(0, result.Requests);
    }

    [Theory]
    [InlineData(PhysicalKey.Q, 1f, 0f)]
    [InlineData(PhysicalKey.E, -1f, 0f)]
    [InlineData(PhysicalKey.A, 0f, -1f)]
    [InlineData(PhysicalKey.D, 0f, 1f)]
    public async Task AMoveKey_MovesTheTargetByATenthOfTheViewHeight(PhysicalKey key, float up, float right)
    {
        var scene = PreviewTestScenes.StackedBoxes();
        var result = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(scene);
            await IdleAsync(view, 1);
            view.Focus();
            window.KeyPressQwerty(key, RawInputModifiers.None);
            await IdleAsync(view, 2);
            return (view.CameraShift, view.CameraDistance);
        });

        var framed = Framed(scene);
        var step = framed.ViewHeight * VehiclePreviewView.KeyMoveFraction;
        var expected = (Vector3.UnitX * up + framed.ScreenRight * right) * step;
        Assert.True(Vector3.Distance(expected, result.CameraShift) < step * 1e-3f, $"The shift is {result.CameraShift}, expected {expected}.");
        Assert.Equal(framed.Distance, result.CameraDistance);
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
            return (Framed: framed, Closer: closer, Farther: farther, view.CameraShift);
        });

        Assert.Equal(result.Framed * OrbitCamera.ZoomStepFactor, result.Closer, result.Framed * 1e-5f);
        Assert.Equal(result.Framed / OrbitCamera.ZoomStepFactor, result.Farther, result.Framed * 1e-5f);
        Assert.Equal(Vector3.Zero, result.CameraShift);
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
            var moved = view.CameraShift != Vector3.Zero && view.CameraDistance < framed && view.CameraYaw != OrbitCamera.DefaultYaw;

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
            return (Moved: moved, view.CameraShift, Framed: view.CameraDistance == framed, view.CameraYaw);
        });

        Assert.True(result.Moved);
        Assert.Equal((Vector3.Zero, true, OrbitCamera.DefaultYaw), (result.CameraShift, result.Framed, result.CameraYaw));
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
            var (shift, distance) = (view.CameraShift, view.CameraDistance);

            // Avalonia counts the presses of all buttons in one click count, so the left press has a count of 2.
            window.MouseDown(Center, MouseButton.Right);
            window.MouseUp(Center, MouseButton.Right);
            window.MouseDown(Center, MouseButton.Left);
            window.MouseUp(Center, MouseButton.Left);
            var kept = view.CameraShift == shift && view.CameraDistance == distance;

            window.MouseDown(Center, MouseButton.Left);
            window.MouseUp(Center, MouseButton.Left);
            await HeadlessApp.FramesUntilAsync(() => view.IsIdle);
            return (Kept: kept, Moved: shift != Vector3.Zero && distance < framed, view.CameraShift, Framed: view.CameraDistance == framed);
        });

        Assert.True(result.Moved);
        Assert.True(result.Kept);
        Assert.Equal((Vector3.Zero, true), (result.CameraShift, result.Framed));
    }

    [Fact]
    public async Task TheWheel_ZoomsToTheNearLimit_AndAStepBackRespondsAtOnce()
    {
        var result = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(PreviewTestScenes.StackedBoxes());
            await IdleAsync(view, 1);
            var framed = view.CameraDistance;
            window.MouseWheel(Center, new Avalonia.Vector(0, 200));
            var nearest = view.CameraDistance;
            window.MouseWheel(Center, new Avalonia.Vector(0, -1));
            return (Framed: framed, Nearest: nearest, Back: view.CameraDistance);
        });

        Assert.Equal(result.Framed * OrbitCamera.MinZoomFactor, result.Nearest, result.Framed * 1e-5f);
        Assert.Equal(result.Nearest / OrbitCamera.ZoomStepFactor, result.Back, result.Framed * 1e-5f);
    }

    [Fact]
    public async Task AMoveKeyAfterAHighFrame_ShowsAStandardFrame_AndRefinesAgain()
    {
        var result = await HeadlessApp.RunAsync(async () =>
        {
            var (window, view) = Show(PreviewTestScenes.StackedBoxes());
            view.RefineDelay = TimeSpan.FromMilliseconds(20);
            await HeadlessApp.FramesUntilAsync(() => view.IsIdle && view.ShownQuality == RenderQuality.High);
            var (shown, high) = (view.FramesShown, view.HighFramesShown);

            view.Focus();
            window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.None);
            await IdleAsync(view, shown + 1);
            var afterKey = view.ShownQuality;
            await HeadlessApp.FramesUntilAsync(() => view.IsIdle && view.ShownQuality == RenderQuality.High);
            return (AfterKey: afterKey, Standard: view.FramesShown - shown, High: view.HighFramesShown - high, view.ShownQuality);
        });

        Assert.Equal((RenderQuality.Standard, 1, 1, RenderQuality.High), result);
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
            var moved = view.CameraShift;

            // the same box with another material has the same bounds, as the textured scene after the flat one has
            view.Scene = textured;
            var kept = view.CameraShift;
            view.ResetCount++;
            var afterReset = view.CameraShift;

            window.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.None);
            view.Scene = other;
            var afterOther = view.CameraShift;
            await HeadlessApp.FramesUntilAsync(() => view.IsIdle);
            return (Moved: moved, Kept: kept, AfterReset: afterReset, AfterOther: afterOther);
        });

        Assert.NotEqual(Vector3.Zero, result.Moved);
        Assert.Equal(result.Moved, result.Kept);
        Assert.Equal(Vector3.Zero, result.AfterReset);
        Assert.Equal(Vector3.Zero, result.AfterOther);
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
