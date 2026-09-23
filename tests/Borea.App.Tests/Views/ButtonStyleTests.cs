using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class ButtonStyleTests
{
    private static readonly string[] ButtonClasses =
        ["nav", "text", "cta", "add", "tab", "filter", "chip-button", "plain", "link", "dropdown", "icon", "menu", "splitButton", "info"];

    [Fact]
    public async Task EveryButtonAndSwitch_ShowsTheHand_AndTheArrowWhileOff()
    {
        var cursors = await HeadlessApp.RunAsync(() =>
        {
            var controls = ButtonClasses.Select(name => (Control)new Button { Classes = { name }, Content = name }).ToList();
            controls.Add(new ToggleSwitch { Classes = { "switch" } });
            controls.Add(new ComboBox());
            controls.Add(new Button { Classes = { "plain" }, Content = "off", IsEnabled = false });
            controls.Add(new ToggleSwitch { Classes = { "switch" }, IsEnabled = false });
            var panel = new StackPanel();
            panel.Children.AddRange(controls);
            var window = new Window { Width = 400, Height = 1200, Content = panel };
            window.Show();
            var result = controls.Select(control => control.Cursor?.ToString()).ToArray();
            window.Close();
            return Task.FromResult(result);
        });

        Assert.All(cursors[..^2], cursor => Assert.Equal("Hand", cursor));
        Assert.All(cursors[^2..], cursor => Assert.Equal("Arrow", cursor));
    }

    [Fact]
    public async Task PlainTitle_TurnsToTheAccentUnderThePointer_AndARowKeepsTheHoverOfItsCard()
    {
        var hover = await HeadlessApp.RunAsync(() =>
        {
            var title = new TextBlock { Classes = { "label-lg" }, Text = "Library" };
            var plain = new Button { Classes = { "plain" }, Content = title };
            var rowTitle = new TextBlock { Text = "Alpha" };
            var card = new Border { Classes = { "card" }, Width = 200, Height = 40, Child = rowTitle };
            var row = new Button { Classes = { "plain", "card-button" }, Content = card };
            var window = new Window { Width = 400, Height = 300, Content = new StackPanel { Children = { plain, row } } };
            window.Show();
            window.UpdateLayout();

            window.MouseMove(Center(plain, window));
            var titleOver = title.Foreground;
            window.MouseMove(Center(row, window));
            var result = (TitleOver: titleOver, TitleAfter: title.Foreground, RowTitleOver: rowTitle.Foreground, CardOver: card.Background,
                Accent: window.FindResource("Brush.Accent"), Raised: window.FindResource("Brush.SurfaceRaised"));
            window.Close();
            return Task.FromResult(result);
        });

        Assert.Same(hover.Accent, hover.TitleOver);
        Assert.NotSame(hover.Accent, hover.TitleAfter);
        Assert.NotSame(hover.Accent, hover.RowTitleOver);
        Assert.Same(hover.Raised, hover.CardOver);
    }

    [Fact]
    public async Task Switch_TakesTheSizeOfItsTrackOnly_AndMovesItsKnobWhenOn()
    {
        var (size, knobOff, knobOn) = await HeadlessApp.RunAsync(() =>
        {
            var toggle = new ToggleSwitch { Classes = { "switch" } };
            var window = new Window { Width = 400, Height = 300, Content = new StackPanel { Children = { toggle } } };
            window.Show();
            window.UpdateLayout();
            var knobs = toggle.GetVisualDescendants().OfType<Control>().Single(control => control.Name == "PART_MovingKnobs");
            var off = Canvas.GetLeft(knobs);
            toggle.IsChecked = true;
            window.UpdateLayout();
            var result = (toggle.Bounds.Size, off, Canvas.GetLeft(knobs));
            window.Close();
            return Task.FromResult(result);
        });

        Assert.Equal(new Size(40, 20), size);
        Assert.Equal(0, knobOff);
        Assert.Equal(20, knobOn);
    }

    [Theory]
    [InlineData(typeof(LoaderPromptModal))]
    [InlineData(typeof(FoundGameModal))]
    [InlineData(typeof(CloseNowModal))]
    [InlineData(typeof(GitHubSignInModal))]
    [InlineData(typeof(LaunchFailureModal))]
    public async Task TextButtonBesideACta_TakesTheHeightOfTheCta(Type modalType)
    {
        using var harness = await ViewModelHarness.CreateAsync();

        var (ctaHeight, heights) = await HeadlessApp.RunAsync(harness, () =>
        {
            var modal = (Control)Activator.CreateInstance(modalType)!;
            var window = new Window { Width = 1280, Height = 832, DataContext = harness.ViewModel, Content = modal };
            window.Show();
            window.UpdateLayout();
            var ctas = modal.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("cta")).ToList();
            var besides = ctas
                .SelectMany(cta => cta.GetVisualParent()!.GetVisualChildren().OfType<Button>())
                .Distinct()
                .Where(button => button.Classes.Contains("text") && button.IsEffectivelyVisible)
                .Select(button => button.Bounds.Height)
                .ToList();
            var ctaHeight = ctas[0].Height;
            window.Close();
            return Task.FromResult((ctaHeight, besides));
        });

        Assert.NotEmpty(heights);
        Assert.All(heights, height => Assert.Equal(ctaHeight, height));
    }

    private static Point Center(Visual target, Window window)
        => target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
}
