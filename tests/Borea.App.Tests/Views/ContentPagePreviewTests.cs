using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using ContentPage = Borea.App.Views.Pages.ContentPage;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Preview;
using Borea.Core.Preview;
using Borea.Preview.Rendering;
using Borea.Preview.Scene;
using static Borea.App.Tests.ViewModels.UserContentViewModelTests;

namespace Borea.App.Tests.Views;

/// <summary>The 3D preview panel on the content page of a vehicle (RFC 0085).</summary>
[Collection(HeadlessCollection.Name)]
public sealed class ContentPagePreviewTests
{
    private const double WindowWidth = 1200;

    [Fact]
    public async Task VehiclePage_DrawsTheSceneInThePanel_AndAModPageHasNoPanel()
    {
        using var harness = await RoverPageAsync();
        var drawn = await HeadlessApp.RunAsync(harness, async () =>
        {
            var (window, page, view) = await ShowAsync(harness, height: 1600);
            var background = ((ISolidColorBrush)view.Background!).Color;
            var area = BoundsIn(window, view);
            var changed = 0;
            var total = 0;
            ReadFrame(window, (x, y, color) =>
            {
                // the middle half of the panel, away from the rounded corners of the card
                if (x < area.X + area.Width / 4 || x >= area.Right - area.Width / 4 || y < area.Y + area.Height / 4 || y >= area.Bottom - area.Height / 4)
                    return;

                total++;
                if (Math.Max(Math.Abs(color.R - background.R), Math.Max(Math.Abs(color.G - background.G), Math.Abs(color.B - background.B))) > 16)
                    changed++;
            });
            window.Close();
            return (Changed: changed, Total: total, Panels: page.GetVisualDescendants().OfType<VehiclePreviewView>().Count());
        });

        Assert.Equal(1, drawn.Panels);
        Assert.True(drawn.Changed > drawn.Total / 10, $"Only {drawn.Changed} of {drawn.Total} pixels in the panel show the scene.");

        var viewModel = harness.ViewModel;
        viewModel.ShowDiscoverModsCommand.Execute(null);
        await viewModel.DiscoverItems.Single(item => item.ModId == "MeasureTools").OpenCommand.ExecuteAsync(null);
        var modPanels = await HeadlessApp.RunAsync(harness, () =>
        {
            var page = new ContentPage();
            var window = new Window { Width = WindowWidth, Height = 900, DataContext = viewModel, Content = page };
            window.Show();
            page.UpdateLayout();
            var count = page.GetVisualDescendants().OfType<VehiclePreviewView>().Count();
            window.Close();
            return Task.FromResult(count);
        });

        Assert.Null(viewModel.VehiclePreview);
        Assert.Equal(0, modPanels);
    }

    [Fact]
    public async Task TheWheel_MovesTheTargetOverThePanel_AndScrollsThePageOverTheDescription()
    {
        using var harness = await RoverPageAsync();
        var seen = await HeadlessApp.RunAsync(harness, async () =>
        {
            var (window, page, view) = await ShowAsync(harness, height: 700);
            var scroller = view.GetVisualAncestors().OfType<ScrollViewer>().First();

            // the panel comes into the window first, so the wheel is over it
            scroller.Offset = new Vector(0, Math.Max(0, BoundsIn(window, view).Y - 40));
            await HeadlessApp.FramesAsync(2);
            var offset = scroller.Offset;
            window.MouseWheel(BoundsIn(window, view).Center, new Vector(0, 2));
            await HeadlessApp.FramesAsync(20);
            var overPanel = (Moved: view.CameraAxisOffset > 0f, Offset: scroller.Offset == offset);

            // then the description, with the page scrolled down so that a wheel up can move it
            var description = page.GetVisualDescendants().OfType<TextBlock>().First(block => block.IsEffectivelyVisible && block.Text == harness.Localization.ContentNoDescription);
            scroller.Offset = new Vector(0, scroller.Offset.Y + BoundsIn(window, description).Y - 300);
            await HeadlessApp.FramesAsync(2);
            var before = scroller.Offset.Y;
            window.MouseWheel(BoundsIn(window, description).Center, new Vector(0, 1));
            await HeadlessApp.FramesAsync(40);
            var after = scroller.Offset.Y;
            window.Close();
            return (overPanel.Moved, overPanel.Offset, Before: before, After: after);
        });

        Assert.True(seen.Moved, "The wheel over the panel must move the target of the view.");
        Assert.True(seen.Offset, "The wheel over the panel must leave the page where it is.");
        Assert.True(seen.Before > 0);
        Assert.True(seen.After < seen.Before, $"The wheel over the description must scroll the page, but the offset stayed at {seen.After}.");
    }

    [Fact]
    public async Task Expand_FillsTheWindowWithTheCameraOfThePanel_AndEscapeGivesTheCameraBack()
    {
        using var harness = await RoverPageAsync();
        var seen = await HeadlessApp.RunAsync(harness, async () =>
        {
            var (window, panel) = await ShowInMainWindowAsync(harness);
            var preview = harness.ViewModel.VehiclePreview!;

            // an earlier Reset view click must not count again for the new view
            preview.ResetViewCommand.Execute(null);
            await HeadlessApp.FramesUntilAsync(() => panel.IsIdle);
            panel.Focus();
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            await HeadlessApp.FramesUntilAsync(() => panel.IsIdle);
            var turned = panel.CameraYaw;
            var pose = preview.Pose;

            preview.ExpandCommand.Execute(null);
            var expanded = ExpandedView(window);
            await HeadlessApp.FramesUntilAsync(() => expanded.IsIdle && expanded.FramesShown >= 1);
            var opened = (Turned: turned, Yaw: expanded.CameraYaw, PoseKept: preview.Pose == pose, Area: BoundsIn(window, expanded), PanelShows: panel.IsVisible);

            // the expanded view takes the focus on its own, so its keys and Escape work without a click
            await HeadlessApp.FramesUntilAsync(() => expanded.IsFocused);
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            await HeadlessApp.FramesUntilAsync(() => expanded.IsIdle);
            var turnedFurther = expanded.CameraYaw;

            var overlay = expanded.GetVisualParent()!;
            var tabStaysInside = true;
            for (var press = 0; press < 5; press++)
            {
                window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                tabStaysInside &= window.FocusManager!.GetFocusedElement() is Visual focused && focused.GetSelfAndVisualAncestors().Contains(overlay);
            }

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            await HeadlessApp.FramesUntilAsync(() => panel.IsVisible && panel.IsIdle);
            var closed = (preview.IsExpanded, Views: window.GetVisualDescendants().OfType<VehiclePreviewView>().Count(), PanelYaw: panel.CameraYaw);

            // Reset view in the expanded view frames the vehicle in both views
            preview.ExpandCommand.Execute(null);
            var again = ExpandedView(window);
            await HeadlessApp.FramesUntilAsync(() => again.IsIdle && again.FramesShown >= 1);
            preview.ResetViewCommand.Execute(null);
            await HeadlessApp.FramesUntilAsync(() => again.IsIdle && panel.IsIdle);
            var reset = (Expanded: again.CameraYaw, Panel: panel.CameraYaw);

            var size = window.ClientSize;
            window.DataContext = null;
            window.Close();
            return (Opened: opened, TurnedFurther: turnedFurther, TabStaysInside: tabStaysInside, Closed: closed, Reset: reset, Size: size);
        });

        Assert.NotEqual(OrbitCamera.DefaultYaw, seen.Opened.Turned);
        Assert.Equal(seen.Opened.Turned, seen.Opened.Yaw);
        Assert.True(seen.Opened.PoseKept, "Opening the expanded view must leave the pose alone.");
        Assert.Equal(new Rect(seen.Size), seen.Opened.Area);
        Assert.False(seen.Opened.PanelShows);
        Assert.NotEqual(seen.Opened.Yaw, seen.TurnedFurther);
        Assert.True(seen.TabStaysInside, "Tab must stay inside the expanded view.");
        Assert.False(seen.Closed.IsExpanded);
        Assert.Equal(1, seen.Closed.Views);
        Assert.Equal(seen.TurnedFurther, seen.Closed.PanelYaw);
        Assert.Equal((OrbitCamera.DefaultYaw, OrbitCamera.DefaultYaw), seen.Reset);
    }

    [Fact]
    public async Task TheExpandedView_ShowsTheLoadStatusOfThePanel()
    {
        using var harness = await RoverPageAsync();
        var seen = await HeadlessApp.RunAsync(harness, async () =>
        {
            var (window, _) = await ShowInMainWindowAsync(harness);
            var preview = harness.ViewModel.VehiclePreview!;
            preview.ExpandCommand.Execute(null);
            var overlay = ExpandedView(window).GetVisualParent()!;
            preview.State = VehiclePreviewState.Failed;
            window.UpdateLayout();
            var status = overlay.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible && text.Text == preview.StatusText);
            var cancel = overlay.GetVisualDescendants().OfType<Button>().Any(button => ReferenceEquals(button.Command, preview.CancelCommand));
            window.DataContext = null;
            window.Close();
            return (Status: status, Cancel: cancel);
        });

        Assert.True(seen.Status, "The expanded view must show the status text.");
        Assert.True(seen.Cancel, "The expanded view must have the Cancel button of the load.");
    }

    [Fact]
    public async Task TheWheel_OverThePanelWithoutAScene_ScrollsThePage()
    {
        using var harness = await VehicleHarnessAsync();
        harness.ViewModel.VehiclePreviewServiceOf = _ => new FakeVehiclePreview { PlanState = VehiclePreviewPlanState.NoGameFolder };
        await VehiclePreviewViewModelTests.OpenRoverAsync(harness.ViewModel);
        Assert.Equal(VehiclePreviewState.NoGameFolder, harness.ViewModel.VehiclePreview!.State);

        var offsets = await HeadlessApp.RunAsync(harness, async () =>
        {
            var page = new ContentPage();
            var window = new Window { Width = WindowWidth, Height = 700, DataContext = harness.ViewModel, Content = page };
            window.Show();
            page.UpdateLayout();
            var view = page.GetVisualDescendants().OfType<VehiclePreviewView>().Single();
            var scroller = view.GetVisualAncestors().OfType<ScrollViewer>().First();
            scroller.Offset = new Vector(0, Math.Max(0, BoundsIn(window, view).Y - 40));
            await HeadlessApp.FramesAsync(2);
            var before = scroller.Offset.Y;

            // a corner of the panel, away from the status text and the buttons in its middle
            var area = BoundsIn(window, view);
            window.MouseWheel(new Point(area.X + 20, area.Y + 20), new Vector(0, 1));
            await HeadlessApp.FramesAsync(40);
            var after = scroller.Offset.Y;
            window.Close();
            return (Before: before, After: after);
        });

        Assert.True(offsets.Before > 0);
        Assert.True(offsets.After < offsets.Before, $"The wheel over the empty panel must scroll the page, but the offset stayed at {offsets.After}.");
    }

    /// <summary>The page of Example Rover, whose preview builds an empty scene that the test then replaces with boxes.</summary>
    private static async Task<ViewModelHarness> RoverPageAsync()
    {
        var harness = await VehicleHarnessAsync();
        harness.ViewModel.VehiclePreviewServiceOf = _ => new FakeVehiclePreview();
        await VehiclePreviewViewModelTests.OpenRoverAsync(harness.ViewModel);
        harness.ViewModel.VehiclePreview!.Scene = PreviewTestScenes.StackedBoxes();
        return harness;
    }

    private static async Task<(Window Window, ContentPage Page, VehiclePreviewView View)> ShowAsync(ViewModelHarness harness, double height)
    {
        var page = new ContentPage();
        var window = new Window { Width = WindowWidth, Height = height, DataContext = harness.ViewModel, Content = page };
        window.Show();
        page.UpdateLayout();
        var view = page.GetVisualDescendants().OfType<VehiclePreviewView>().Single();
        await HeadlessApp.FramesUntilAsync(() => view.IsIdle && view.FramesShown >= 1);
        Assert.True(view.FramesShown >= 1, "The panel showed no frame.");
        return (window, page, view);
    }

    private static async Task<(MainWindow Window, VehiclePreviewView Panel)> ShowInMainWindowAsync(ViewModelHarness harness)
    {
        var window = new MainWindow { Width = WindowWidth, Height = 700, DataContext = harness.ViewModel };
        window.Show();
        window.UpdateLayout();
        var panel = window.GetVisualDescendants().OfType<VehiclePreviewView>().Single();
        await HeadlessApp.FramesUntilAsync(() => panel.IsIdle && panel.FramesShown >= 1);
        return (window, panel);
    }

    private static VehiclePreviewView ExpandedView(Window window) =>
        window.GetVisualDescendants().OfType<VehiclePreviewView>().Single(view => view.IsVisible);

    internal static Rect BoundsIn(Visual window, Visual control) => new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);

    /// <param name="shotName">The start of the PNG file name when BOREA_PREVIEW_SHOTS is set.</param>
    internal static void ReadFrame(Window window, Action<int, int, Color> pixel, string shotName = "content-page")
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame()!;

        // BOREA_PREVIEW_SHOTS names a folder that keeps the frame as a PNG for a person to look at.
        if (Environment.GetEnvironmentVariable("BOREA_PREVIEW_SHOTS") is { Length: > 0 } shots)
        {
            Directory.CreateDirectory(shots);
            frame.Save(Path.Combine(shots, $"{shotName}-{window.Height:0}.png"), PngBitmapEncoderOptions.Default);
        }

        using var buffer = frame.Lock();
        var bgra = buffer.Format == PixelFormat.Bgra8888;
        var row = new byte[buffer.Size.Width * 4];
        for (var y = 0; y < buffer.Size.Height; y++)
        {
            Marshal.Copy(buffer.Address + y * buffer.RowBytes, row, 0, row.Length);
            for (var x = 0; x < buffer.Size.Width; x++)
            {
                var (r, g, b) = bgra ? (row[x * 4 + 2], row[x * 4 + 1], row[x * 4]) : (row[x * 4], row[x * 4 + 1], row[x * 4 + 2]);
                pixel(x, y, Color.FromRgb(r, g, b));
            }
        }
    }
}
