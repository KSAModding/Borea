using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Borea.App.Views;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class SmoothScrollTests
{
    [Fact]
    public async Task TheWheel_DoesNotMoveTheOffsetAtOnce_AndThenEasesItToWhereTheNotchesAsked()
    {
        var seen = await HeadlessApp.RunAsync(async () =>
        {
            var (window, scroller) = ShowLongList();
            var before = scroller.Offset.Y;

            window.MouseWheel(new Point(200, 150), new Vector(0, -3));
            var onTheWheel = scroller.Offset.Y;
            // a frame moves the scroll by at most the longest frame of SmoothScroll, so the offset after each frame
            // shows the way there however slow the frames come
            var steps = new List<double>();
            for (var frame = 0; frame < 60; frame++)
            {
                await HeadlessApp.FramesAsync(1);
                steps.Add(scroller.Offset.Y);
            }

            window.Close();
            return (before, onTheWheel, Steps: steps, Notch: SmoothScroll.Notch);
        });

        var target = seen.before + 3 * seen.Notch;
        Assert.True(seen.onTheWheel < target, "The offset must still be on its way rather than already there.");
        Assert.Contains(seen.Steps, offset => offset > seen.before && offset < target);
        Assert.Equal(seen.Steps.Order(), seen.Steps);
        Assert.Equal(target, seen.Steps[^1]);
    }

    [Fact]
    public async Task NotchesTakenApart_ArriveWhereAllOfThemInOneWheelArrive()
    {
        var apart = await WheelTo(new Vector(0, -1), new Vector(0, -1), new Vector(0, -1), new Vector(0, -1));
        var together = await WheelTo(new Vector(0, -4));

        Assert.Equal(4 * SmoothScroll.Notch, together);
        Assert.Equal(apart, together);
    }

    [Fact]
    public async Task NotchesThatWouldPassTheEndOfTheList_StopAtTheEndOfIt()
    {
        var seen = await HeadlessApp.RunAsync(async () =>
        {
            var (window, scroller) = ShowLongList();
            window.MouseWheel(new Point(200, 150), new Vector(0, -100));
            await HeadlessApp.FramesAsync();
            var atTheEnd = (scroller.Offset.Y, Maximum: scroller.ScrollBarMaximum.Y);
            window.Close();
            return atTheEnd;
        });

        Assert.True(seen.Maximum > 0, "The list must be long enough to scroll, or the test proves nothing.");
        Assert.Equal(seen.Maximum, seen.Y);
    }

    [Fact]
    public async Task AScrollFromAnywhereElse_TakesTheOffsetOverAndStopsTheEasing()
    {
        var seen = await HeadlessApp.RunAsync(async () =>
        {
            var (window, scroller) = ShowLongList();
            window.MouseWheel(new Point(200, 150), new Vector(0, -20));
            var eased = scroller.Offset.Y;
            await HeadlessApp.FramesAsync(2);

            var takenOver = scroller.Offset.Y + 300;
            scroller.Offset = new Vector(0, takenOver);
            await HeadlessApp.FramesAsync();
            var after = scroller.Offset.Y;
            window.Close();
            return (eased, takenOver, after);
        });

        Assert.InRange(seen.eased, 0, 20 * SmoothScroll.Notch);
        Assert.Equal(seen.takenOver, seen.after);
    }

    [Fact]
    public async Task AViewerThatIsGivenTheSmoothScrollOff_MovesWithTheWheelAtOnceAgain()
    {
        var seen = await HeadlessApp.RunAsync(() =>
        {
            var (window, scroller) = ShowLongList();
            SmoothScroll.SetIsEnabled(scroller, false);
            window.UpdateLayout();
            var before = scroller.Offset.Y;
            window.MouseWheel(new Point(200, 150), new Vector(0, -1));
            var after = scroller.Offset.Y;
            window.Close();
            return Task.FromResult((before, after));
        });

        Assert.Equal(seen.before + SmoothScroll.Notch, seen.after);
    }

    [Fact]
    public async Task TheWheelOverAnOpenComboBox_MovesItsList_AndLeavesThePageBehindItAlone()
    {
        var seen = await HeadlessApp.RunAsync(async () =>
        {
            var box = new ComboBox { ItemsSource = Enumerable.Range(0, 200).Select(item => $"Item {item}").ToArray(), Width = 120 };
            var page = new ScrollViewer { Content = new StackPanel { Children = { box, new Border { Height = 5000 } } } };
            var window = new Window { Width = 400, Height = 300, Content = page };
            window.Show();
            window.UpdateLayout();
            page.Offset = new Vector(0, 200);
            var before = page.Offset.Y;

            box.IsDropDownOpen = true;
            await HeadlessApp.FramesAsync();
            var panel = box.GetVisualDescendants().OfType<Popup>().Single().Child!;
            var list = panel.GetVisualDescendants().OfType<ScrollViewer>().Single();
            TopLevel.GetTopLevel(panel)!.MouseWheel(new Point(20, 20), new Vector(0, -3));
            await HeadlessApp.FramesAsync();
            var result = (List: list.Offset.Y, Before: before, Page: page.Offset.Y);
            window.Close();
            return result;
        });

        Assert.True(seen.List > 0, "A dropdown long enough to scroll must take the wheel under the pointer.");
        Assert.Equal(seen.Before, seen.Page);
    }

    [Fact]
    public async Task TheWheelOverAnOpenMenu_LeavesThePageBehindItAlone()
    {
        var seen = await HeadlessApp.RunAsync(async () =>
        {
            var flyout = new MenuFlyout();
            for (var item = 0; item < 60; item++)
                flyout.Items.Add(new MenuItem { Header = $"Item {item}" });
            var button = new Button { Content = "Open", Width = 100, Height = 30 };
            Flyout.SetAttachedFlyout(button, flyout);
            var page = new ScrollViewer { Content = new StackPanel { Children = { button, new Border { Height = 5000 } } } };
            var window = new Window { Width = 400, Height = 300, Content = page };
            window.Show();
            window.UpdateLayout();
            page.Offset = new Vector(0, 200);
            var before = page.Offset.Y;

            flyout.ShowAt(button);
            await HeadlessApp.FramesAsync();
            var panel = flyout.Popup.Child!;
            TopLevel.GetTopLevel(panel)!.MouseWheel(new Point(20, 20), new Vector(0, -3));
            await HeadlessApp.FramesAsync();
            var after = page.Offset.Y;
            window.Close();
            return (before, after);
        });

        Assert.Equal(seen.before, seen.after);
    }

    private static Task<double> WheelTo(params Vector[] notches) => HeadlessApp.RunAsync(async () =>
    {
        var (window, scroller) = ShowLongList();
        foreach (var notch in notches)
        {
            window.MouseWheel(new Point(200, 150), notch);
            await HeadlessApp.FramesAsync(2);
        }

        await HeadlessApp.FramesAsync();
        window.Close();
        return scroller.Offset.Y;
    });

    private static (Window Window, ScrollViewer Scroller) ShowLongList()
    {
        var scroller = new ScrollViewer { Content = new Border { Height = 5000 } };
        var window = new Window { Width = 400, Height = 300, Content = scroller };
        window.Show();
        window.UpdateLayout();
        return (window, scroller);
    }
}
