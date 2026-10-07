using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Borea.App.Views;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class DropdownAnimationTests
{
    [Fact]
    public async Task AMenuOnAButton_FadesInFromItsClosedLook_AndClosesAtOnce()
    {
        var seen = await HeadlessApp.RunAsync(async () =>
        {
            var (window, button, flyout) = ShowButtonWithMenu();
            var looks = LooksOnOpen(flyout);
            Click(window, button);
            var panel = PanelOf(flyout);
            var open = flyout.IsOpen;

            await HeadlessApp.FramesUntilAsync(() => panel.RenderTransform is null);
            var arrived = (Opacity: panel.Opacity, Transform: panel.RenderTransform);

            flyout.Hide();
            var result = (onArrival: looks.Single(), open, arrived, Closed: !flyout.IsOpen);
            window.Close();
            return result;
        });

        Assert.True(seen.open);
        Assert.Equal(0, seen.onArrival.Opacity);
        Assert.True(seen.onArrival.HasTransform);
        Assert.Equal(1, seen.arrived.Opacity);
        Assert.Null(seen.arrived.Transform);
        Assert.True(seen.Closed, "A flyout must close at once; only the opening is animated.");
    }

    [Fact]
    public async Task AClickOnAMenuItem_ClosesTheMenuAndLeavesNoPopupBehind()
    {
        var seen = await HeadlessApp.RunAsync(async () =>
        {
            var (window, button, flyout) = ShowButtonWithMenu();
            Click(window, button);
            var panel = PanelOf(flyout);
            await HeadlessApp.FramesUntilAsync(() => panel.RenderTransform is null);

            window.UpdateLayout();
            Click(window, flyout.Items.OfType<MenuItem>().First());
            await HeadlessApp.FramesUntilAsync(() => !flyout.IsOpen);
            var result = (FlyoutOpen: flyout.IsOpen, PopupOpen: flyout.Popup.IsOpen);
            window.Close();
            return result;
        });

        Assert.False(seen.FlyoutOpen);
        Assert.False(seen.PopupOpen);
    }

    [Fact]
    public async Task AMenuThatOpensAgain_StartsFromItsClosedLook()
    {
        var seen = await HeadlessApp.RunAsync(async () =>
        {
            var (window, button, flyout) = ShowButtonWithMenu();
            var looks = LooksOnOpen(flyout);
            Click(window, button);
            var panel = PanelOf(flyout);
            await HeadlessApp.FramesUntilAsync(() => panel.RenderTransform is null);
            flyout.Hide();

            Click(window, button);
            window.Close();
            return looks;
        });

        Assert.Equal(2, seen.Count);
        Assert.Equal(0, seen[1].Opacity);
        Assert.True(seen[1].HasTransform);
    }

    [Fact]
    public async Task AComboBox_FadesItsPanelIn_AndClosesWithoutBeingPutBackUp()
    {
        var seen = await HeadlessApp.RunAsync(async () =>
        {
            var (window, box) = ShowComboBox();
            var panel = PanelOf(box);
            box.IsDropDownOpen = true;
            var onArrival = (Opacity: panel.Opacity, Transform: panel.RenderTransform, Open: box.IsDropDownOpen);
            await HeadlessApp.FramesUntilAsync(() => panel.RenderTransform is null);
            var arrived = (Opacity: panel.Opacity, Transform: panel.RenderTransform);

            box.IsDropDownOpen = false;

            var putBackUp = 0;
            for (var frame = 0; frame < 8; frame++)
            {
                if (box.IsDropDownOpen)
                    putBackUp++;
                await HeadlessApp.FramesAsync(1);
            }

            var result = (onArrival, arrived, PutBackUp: putBackUp, Closed: box.IsDropDownOpen);
            window.Close();
            return result;
        });

        Assert.True(seen.onArrival.Open);
        Assert.Equal(0, seen.onArrival.Opacity);
        Assert.NotNull(seen.onArrival.Transform);
        Assert.Equal(1, seen.arrived.Opacity);
        Assert.Null(seen.arrived.Transform);
        Assert.False(seen.Closed);
        Assert.Equal(0, seen.PutBackUp);
    }

    [Fact]
    public async Task ADropdown_CarriesRoundedCorners_AndClipsToThem()
    {
        var (menu, combo) = await HeadlessApp.RunAsync(async () =>
        {
            var (window, button, flyout) = ShowButtonWithMenu();
            Click(window, button);
            var presenter = (MenuFlyoutPresenter)PanelOf(flyout);
            var menu = (Corner: presenter.CornerRadius,
                Clipped: presenter.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "LayoutRoot").ClipToBounds);
            flyout.Hide();
            window.Close();

            var (comboWindow, box) = ShowComboBox();
            box.IsDropDownOpen = true;
            var border = PanelOf(box);
            var combo = (Corner: border.CornerRadius, Clipped: border.ClipToBounds);
            box.IsDropDownOpen = false;
            comboWindow.Close();
            return await Task.FromResult((menu, combo));
        });

        Assert.Equal(new CornerRadius(12), menu.Corner);
        Assert.True(menu.Clipped);
        Assert.Equal(new CornerRadius(12), combo.Corner);
        Assert.True(combo.Clipped);
    }

    [Theory]
    [InlineData(PlacementMode.Top)]
    [InlineData(PlacementMode.Bottom)]
    [InlineData(PlacementMode.Left)]
    [InlineData(PlacementMode.Right)]
    public void WhereAFlyoutOpens_TellsItWhichWayItSlidesIn(PlacementMode placement)
    {
        var edge = DropdownAnimation.Edge(placement);

        Assert.Equal(placement is PlacementMode.Bottom, edge.Travel.Y > 0);
        Assert.Equal(placement is PlacementMode.Top, edge.Travel.Y < 0);
        Assert.Equal(placement is PlacementMode.Right, edge.Travel.X > 0);
        Assert.Equal(placement is PlacementMode.Left, edge.Travel.X < 0);
        Assert.Equal(DropdownAnimation.Travel, Math.Max(Math.Abs(edge.Travel.X), Math.Abs(edge.Travel.Y)));
    }

    // <Button.Flyout>, opened by a click
    private static (Window Window, Button Button, MenuFlyout Flyout) ShowButtonWithMenu()
    {
        var flyout = new MenuFlyout();
        flyout.Items.Add(new MenuItem { Header = "First" });
        flyout.Items.Add(new MenuItem { Header = "Second" });
        var button = new Button { Classes = { "dropdown" }, Content = "Open", Width = 100, Height = 30, Flyout = flyout };
        var window = new Window { Width = 400, Height = 300, Content = button };
        window.Show();
        window.UpdateLayout();
        return (window, button, flyout);
    }

    private static (Window Window, ComboBox Box) ShowComboBox()
    {
        var box = new ComboBox { Width = 120, Height = 30 };
        var window = new Window { Width = 400, Height = 300, Content = box };
        window.Show();
        window.UpdateLayout();
        return (window, box);
    }

    private static void Click(Window window, Control control)
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
    }

    /// <summary>
    /// The look of the panel each time the flyout opens. The handler that starts the fade was added when the button
    /// got its flyout, so this one runs after it and sees the closed look before any frame of the fade moves it.
    /// </summary>
    private static List<(double Opacity, bool HasTransform)> LooksOnOpen(MenuFlyout flyout)
    {
        var looks = new List<(double Opacity, bool HasTransform)>();
        flyout.Opened += (_, _) => looks.Add((PanelOf(flyout).Opacity, PanelOf(flyout).RenderTransform is not null));
        return looks;
    }

    private static Control PanelOf(PopupFlyoutBase flyout) => flyout.Popup.Child!;
    private static Border PanelOf(ComboBox box) =>
        (Border)box.GetVisualDescendants().OfType<Popup>().Single().Child!;
}
