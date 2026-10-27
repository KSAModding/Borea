using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class MiddleClickScrollTests
{
    [Fact]
    public void Speed_IsZeroInsideTheDeadZone_AndGrowsPastIt_TowardThePointer()
    {
        Assert.Equal(0, MiddleClickScroll.Speed(MiddleClickScroll.DeadZone));
        Assert.Equal(0, MiddleClickScroll.Speed(-MiddleClickScroll.DeadZone));
        Assert.True(MiddleClickScroll.Speed(100) > MiddleClickScroll.Speed(50));
        Assert.True(MiddleClickScroll.Speed(50) > 0);
        Assert.Equal(-MiddleClickScroll.Speed(80), MiddleClickScroll.Speed(-80));
        Assert.Equal(MiddleClickScroll.MaxSpeed, MiddleClickScroll.Speed(100_000));
    }

    [Fact]
    public async Task HeldMiddleButton_ScrollsTowardThePointer_AndEndsOnRelease()
    {
        var (down, up, released, afterRelease, cursorWhileHeld, cursorAfter, captureAfter) = await OnPageAsync(async page =>
        {
            var start = page.Point(page.Spacer, 10);
            page.Window.MouseDown(start, MouseButton.Middle);
            page.Window.MouseMove(start + new Point(0, 100), RawInputModifiers.MiddleMouseButton);
            await FramesAsync(page.Window);
            var down = page.Viewer.Offset.Y;
            var cursorWhileHeld = page.Viewer.Cursor;

            page.Window.MouseMove(start - new Point(0, 100), RawInputModifiers.MiddleMouseButton);
            await FramesAsync(page.Window);
            var up = page.Viewer.Offset.Y;

            page.Window.MouseUp(start - new Point(0, 100), MouseButton.Middle);
            var released = page.Viewer.Offset.Y;
            page.Window.MouseMove(start + new Point(0, 200));
            await FramesAsync(page.Window);
            return (down, up, released, page.Viewer.Offset.Y, cursorWhileHeld, page.Viewer.Cursor, page.Pointer?.Captured);
        });

        Assert.True(down > 0);
        Assert.True(up < down);
        Assert.Equal(released, afterRelease);
        Assert.NotNull(cursorWhileHeld);
        Assert.Null(cursorAfter);
        Assert.Null(captureAfter);
    }

    [Fact]
    public async Task PointerInsideTheDeadZone_ScrollsNothing()
    {
        var offset = await OnPageAsync(async page =>
        {
            var start = page.Point(page.Spacer, 10);
            page.Window.MouseDown(start, MouseButton.Middle);
            page.Window.MouseMove(start + new Point(0, MiddleClickScroll.DeadZone - 2), RawInputModifiers.MiddleMouseButton);
            await FramesAsync(page.Window);
            var offset = page.Viewer.Offset.Y;
            page.Window.MouseUp(start, MouseButton.Middle);
            return offset;
        });

        Assert.Equal(0, offset);
    }

    [Fact]
    public async Task Escape_EndsTheScroll_AndGoesNoFurther()
    {
        var (before, ended, after, escapesSeen, captureAfter) = await OnPageAsync(async page =>
        {
            var escapes = 0;
            page.Window.KeyDown += (_, args) => escapes += args.Key == Key.Escape ? 1 : 0;
            var start = page.Point(page.Spacer, 10);
            page.Window.MouseDown(start, MouseButton.Middle);
            page.Window.MouseMove(start + new Point(0, 100), RawInputModifiers.MiddleMouseButton);
            await FramesAsync(page.Window);
            var before = page.Viewer.Offset.Y;

            page.Window.KeyPress(Key.Escape, RawInputModifiers.MiddleMouseButton, PhysicalKey.Escape, null);
            var ended = page.Viewer.Offset.Y;
            var capture = page.Pointer?.Captured;
            await FramesAsync(page.Window);
            var after = page.Viewer.Offset.Y;
            page.Window.MouseUp(start, MouseButton.Middle);
            return (before, ended, after, escapes, capture);
        });

        Assert.True(before > 0);
        Assert.Equal(ended, after);
        Assert.Equal(0, escapesSeen);
        Assert.Null(captureAfter);
    }

    [Fact]
    public async Task CaptureTakenByAnotherControl_EndsTheScrollAtOnce()
    {
        var (before, cursorAtOnce, after, ended) = await OnPageAsync(async page =>
        {
            var start = page.Point(page.Spacer, 10);
            page.Window.MouseDown(start, MouseButton.Middle);
            page.Window.MouseMove(start + new Point(0, 100), RawInputModifiers.MiddleMouseButton);
            await FramesAsync(page.Window);
            var before = page.Viewer.Offset.Y;

            // no input and no frame runs between the capture and the check
            page.Pointer!.Capture(page.Outside);
            var cursorAtOnce = page.Viewer.Cursor;
            var ended = page.Viewer.Offset.Y;
            await FramesAsync(page.Window);
            var after = page.Viewer.Offset.Y;
            page.Window.MouseUp(start, MouseButton.Middle);
            return (before, cursorAtOnce, after, ended);
        });

        Assert.True(before > 0);
        Assert.Null(cursorAtOnce);
        Assert.Equal(ended, after);
    }

    [Fact]
    public async Task CaptureTakenByAControlInsideTheViewer_EndsTheScroll()
    {
        var (before, after, ended) = await OnPageAsync(async page =>
        {
            var steal = false;
            page.Window.AddHandler(InputElement.PointerMovedEvent, (_, args) =>
            {
                if (steal)
                    args.Pointer.Capture(page.Spacer);
            }, RoutingStrategies.Tunnel);

            var start = page.Point(page.Spacer, 10);
            page.Window.MouseDown(start, MouseButton.Middle);
            page.Window.MouseMove(start + new Point(0, 100), RawInputModifiers.MiddleMouseButton);
            await FramesAsync(page.Window);
            var before = page.Viewer.Offset.Y;

            steal = true;
            page.Window.MouseMove(start + new Point(0, 120), RawInputModifiers.MiddleMouseButton);
            await FramesAsync(page.Window, 1);
            var ended = page.Viewer.Offset.Y;
            await FramesAsync(page.Window);
            var after = page.Viewer.Offset.Y;
            page.Window.MouseUp(start, MouseButton.Middle);
            return (before, after, ended);
        });

        Assert.True(before > 0);
        Assert.Equal(ended, after);
    }

    [Theory]
    [InlineData("link")]
    [InlineData("hyperlink-button")]
    [InlineData("hyperlink-automation")]
    public async Task MiddleClickOnALink_ScrollsNothing(string link)
    {
        var offset = await OnPageAsync(page => DragAsync(page, link switch
        {
            "link" => page.Link,
            "hyperlink-button" => page.HyperlinkButton,
            _ => page.LinkChip,
        }));

        Assert.Equal(0, offset);
    }

    [Fact]
    public async Task LinkChipsOfTheContentPage_AreLinks()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);

        var types = await HeadlessApp.RunAsync(harness, () =>
        {
            var page = new Borea.App.Views.Pages.ContentPage();
            var window = new Window { Width = 1280, Height = 900, DataContext = viewModel, Content = page };
            window.Show();
            try
            {
                page.UpdateLayout();
                return Task.FromResult(page.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.CommandParameter is ContentLink)
                    .Select(AutomationProperties.GetControlTypeOverride)
                    .ToList());
            }
            finally
            {
                window.Close();
            }
        });

        Assert.NotEmpty(types);
        Assert.All(types, type => Assert.Equal(AutomationControlType.Hyperlink, type));
    }

    [Fact]
    public async Task ControlThatUsesTheMiddleButton_KeepsThePress()
    {
        var (offset, presses) = await OnPageAsync(async page => (await DragAsync(page, page.MiddleButtonUser), page.MiddlePresses));

        Assert.Equal(0, offset);
        Assert.Equal(1, presses);
    }

    [Fact]
    public async Task MiddleDragOnARowButton_Scrolls_WithoutAClick()
    {
        var (offset, clicks) = await OnPageAsync(async page => (await DragAsync(page, page.Row), page.RowClicks));

        Assert.True(offset > 0);
        Assert.Equal(0, clicks);
    }

    [Fact]
    public async Task MiddleDragOnASwitchedOffControlInsideARow_Scrolls_WithoutAClick()
    {
        var (offset, clicks) = await OnPageAsync(async page => (await DragAsync(page, page.SwitchedOff), page.RowClicks));

        Assert.True(offset > 0);
        Assert.Equal(0, clicks);
    }

    [Fact]
    public async Task LeftDrag_ScrollsNothing()
    {
        var offset = await OnPageAsync(async page =>
        {
            var start = page.Point(page.Spacer, 10);
            page.Window.MouseDown(start, MouseButton.Left);
            page.Window.MouseMove(start + new Point(0, 100), RawInputModifiers.LeftMouseButton);
            await FramesAsync(page.Window);
            var offset = page.Viewer.Offset.Y;
            page.Window.MouseUp(start, MouseButton.Left);
            return offset;
        });

        Assert.Equal(0, offset);
    }

    [Fact]
    public async Task InnerViewerThatCannotScroll_LeavesThePressToTheOuterOne()
    {
        var offset = await OnPageAsync(page => DragAsync(page, page.InnerContent));

        Assert.True(offset > 0);
    }

    [Fact]
    public async Task AnotherButton_EndsTheScroll()
    {
        var (before, ended, after) = await OnPageAsync(async page =>
        {
            var start = page.Point(page.Spacer, 10);
            var below = start + new Point(0, 100);
            page.Window.MouseDown(start, MouseButton.Middle);
            page.Window.MouseMove(below, RawInputModifiers.MiddleMouseButton);
            await FramesAsync(page.Window);
            var before = page.Viewer.Offset.Y;

            page.Window.MouseDown(below, MouseButton.Left, RawInputModifiers.MiddleMouseButton);
            var ended = page.Viewer.Offset.Y;
            await FramesAsync(page.Window);
            var after = page.Viewer.Offset.Y;
            page.Window.MouseUp(below, MouseButton.Left, RawInputModifiers.MiddleMouseButton);
            page.Window.MouseUp(below, MouseButton.Middle);
            return (before, ended, after);
        });

        Assert.True(before > 0);
        Assert.Equal(ended, after);
    }

    [Fact]
    public async Task EveryScrollViewerOfThePages_HasTheBehavior_ThroughTheAppStyle()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        var viewers = await HeadlessApp.RunAsync(harness, async () =>
        {
            var window = new MainWindow { DataContext = viewModel };
            window.Show();
            try
            {
                var found = new List<(string Page, bool Enabled)>();
                foreach (var (page, open) in new (string, Func<Task>)[]
                {
                    ("Home", () => Task.CompletedTask),
                    ("Discover", async () =>
                    {
                        viewModel.SetMainWindowDiscover();
                        await viewModel.EnsureDiscoverLoadedAsync();
                    }),
                    ("Library", () =>
                    {
                        viewModel.SetMainWindowLibrary();
                        return Task.CompletedTask;
                    }),
                })
                {
                    await open();
                    window.UpdateLayout();
                    found.AddRange(window.GetVisualDescendants().OfType<ScrollViewer>()
                        .Where(viewer => viewer.IsEffectivelyVisible)
                        .Select(viewer => (page, MiddleClickScroll.GetIsEnabled(viewer))));
                }

                return found;
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains(viewers, viewer => viewer.Page == "Discover");
        Assert.All(viewers, viewer => Assert.True(viewer.Enabled, viewer.Page));
    }

    /// <summary>Holds the middle button on <paramref name="target"/>, drags it down and gives back the offset before the release.</summary>
    private static async Task<double> DragAsync(TestPage page, Control target)
    {
        var start = page.Point(target, 5);
        page.Window.MouseDown(start, MouseButton.Middle);
        page.Window.MouseMove(start + new Point(0, 100), RawInputModifiers.MiddleMouseButton);
        await FramesAsync(page.Window);
        var offset = page.Viewer.Offset.Y;
        page.Window.MouseUp(start + new Point(0, 100), MouseButton.Middle);
        return offset;
    }

    /// <summary>
    /// Waits for <paramref name="count"/> animation frames that Avalonia delivered with real time between them, because the
    /// scroll moves by the time between frames and asks for its next frame before this does, so it has moved on each frame
    /// this waits for.
    /// </summary>
    private static async Task FramesAsync(TopLevel window, int count = 5)
    {
        for (var i = 0; i < count; i++)
        {
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
            var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.RequestAnimationFrame(_ => frame.SetResult());
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await frame.Task;
        }
    }

    private static Task<T> OnPageAsync<T>(Func<TestPage, Task<T>> body) =>
        HeadlessApp.RunAsync(async () =>
        {
            var page = new TestPage();
            page.Window.Show();
            try
            {
                page.Window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                return await body(page);
            }
            finally
            {
                page.Window.Close();
            }
        });

    /// <summary>A window whose scroll viewer holds the kinds of controls a page has above a tall area.</summary>
    private sealed class TestPage
    {
        public TestPage()
        {
            Link = new Button { Classes = { "link" }, Content = "Retry" };
            HyperlinkButton = new HyperlinkButton { Content = "Site" };
            LinkChip = new Button { Classes = { "filter" }, Content = "Forums" };
            AutomationProperties.SetControlTypeOverride(LinkChip, AutomationControlType.Hyperlink);
            Row = new Button { Height = 40, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Command = new RelayCommand(() => RowClicks++) };
            SwitchedOff = new Button { Content = "Play", IsEnabled = false };
            RowWithSwitchedOff = new Button
            {
                Height = 40,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Command = new RelayCommand(() => RowClicks++),
                Content = SwitchedOff,
            };
            RowButton.SetIgnoresDisabled(RowWithSwitchedOff, true);
            MiddleButtonUser = new Border { Height = 30, Background = Brushes.Transparent };
            MiddleButtonUser.PointerPressed += (_, args) =>
            {
                if (args.GetCurrentPoint(MiddleButtonUser).Properties.IsMiddleButtonPressed)
                {
                    MiddlePresses++;
                    args.Handled = true;
                }
            };
            InnerContent = new Border { Height = 20, Background = Brushes.Transparent };
            Spacer = new Border { Height = 2000, Background = Brushes.Transparent };
            Viewer = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Children =
                    {
                        Link, HyperlinkButton, LinkChip, Row, RowWithSwitchedOff, MiddleButtonUser,
                        new ScrollViewer { Height = 60, Content = InnerContent },
                        Spacer,
                    },
                },
            };
            Outside = new Border { Height = 40, Background = Brushes.Transparent };
            DockPanel.SetDock(Outside, Dock.Top);
            Window = new Window { Width = 400, Height = 600, Content = new DockPanel { Children = { Outside, Viewer } } };
            Window.AddHandler(InputElement.PointerPressedEvent, (_, args) => Pointer ??= args.Pointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        public Window Window { get; }

        public ScrollViewer Viewer { get; }

        public Border Outside { get; }

        public Button Link { get; }

        public HyperlinkButton HyperlinkButton { get; }

        public Button LinkChip { get; }

        public Button Row { get; }

        public Button RowWithSwitchedOff { get; }

        public Button SwitchedOff { get; }

        public Border MiddleButtonUser { get; }

        public Border InnerContent { get; }

        public Border Spacer { get; }

        public IPointer? Pointer { get; private set; }

        public int RowClicks { get; private set; }

        public int MiddlePresses { get; private set; }

        /// <summary>A point <paramref name="down"/> pixels below the top of <paramref name="control"/>, in the middle of its width.</summary>
        public Point Point(Control control, double down) =>
            control.TranslatePoint(new Point(Math.Min(control.Bounds.Width / 2, 100), down), Window)!.Value;
    }
}
