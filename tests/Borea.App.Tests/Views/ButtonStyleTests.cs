using System.Xml;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Headless;
using Avalonia.Layout;
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
        var hover = await HeadlessApp.RunAsync(async () =>
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
            await HeadlessApp.FramesAsync();
            var titleOver = title.Foreground;
            window.MouseMove(Center(row, window));
            await HeadlessApp.FramesAsync();
            var result = (TitleOver: titleOver, TitleAfter: title.Foreground, RowTitleOver: rowTitle.Foreground, CardOver: card.Background,
                Accent: window.FindResource("Brush.Accent"), Raised: window.FindResource("Brush.SurfaceRaised"));
            window.Close();
            return (result);
        });

        Assert.Same(hover.Accent, hover.TitleOver);
        Assert.NotSame(hover.Accent, hover.TitleAfter);
        Assert.NotSame(hover.Accent, hover.RowTitleOver);
        Assert.Same(hover.Raised, hover.CardOver);
    }

    [Fact]
    public async Task AHover_FadesIntoItsColour_AndNotSwitchesToItAtOnce()
    {
        var seen = await HeadlessApp.RunAsync(async () =>
        {
            var title = new TextBlock { Classes = { "label-lg" }, Text = "Library" };
            var plain = new Button { Classes = { "plain" }, Content = title, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var window = new Window { Width = 400, Height = 300, Content = plain };
            window.Show();
            window.UpdateLayout();

            window.MouseMove(new Point(390, 290));
            var accent = Colour(window.FindResource("Brush.Accent"));
            await HeadlessApp.FramesUntilAsync(() => Colour(title.Foreground) != accent);
            var before = Colour(title.Foreground);

            window.MouseMove(Center(plain, window));
            var onArrival = Colour(title.Foreground);
            await HeadlessApp.FramesUntilAsync(() => Colour(title.Foreground) != before && Colour(title.Foreground) != accent);
            var partWay = Colour(title.Foreground);
            await HeadlessApp.FramesUntilAsync(() => Colour(title.Foreground) == accent);
            var settled = Colour(title.Foreground);
            window.Close();
            return (before, onArrival, partWay, settled, accent);
        });

        Assert.NotEqual(seen.accent, seen.before);
        Assert.NotEqual(seen.accent, seen.onArrival);
        Assert.NotEqual(seen.before, seen.partWay);
        Assert.NotEqual(seen.accent, seen.partWay);
        Assert.Equal(seen.accent, seen.settled);
    }
    [Fact]
    public async Task AFadeInFromNothing_KeepsTheColourItIsFadingTo_AndOnlyCountsTheTransparencyUp()
    {
        var seen = await HeadlessApp.RunAsync(async () =>
        {
            var button = new Button { Classes = { "icon" }, Content = "Library", HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var window = new Window { Width = 400, Height = 300, Content = button };
            window.Show();
            window.UpdateLayout();
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().Single();
            var raised = Colour(window.FindResource("Brush.SurfaceRaised"));

            window.MouseMove(new Point(390, 290));
            await HeadlessApp.FramesAsync();
            window.MouseMove(button.TranslatePoint(new Point(5, 5), window)!.Value);
            var steps = new List<Color>();
            for (var frame = 0; frame < 12; frame++)
            {
                await HeadlessApp.FramesAsync(1);
                steps.Add(Colour(presenter.Background));
            }

            window.Close();
            return (Steps: steps, Raised: raised);
        });

        Assert.All(seen.Steps, colour =>
            Assert.True(Near(colour.R, seen.Raised.R) && Near(colour.G, seen.Raised.G) && Near(colour.B, seen.Raised.B),
                $"The fade passed through {colour} instead of fading {seen.Raised} in from nothing."));
        Assert.Equal(seen.Steps.Select(step => step.A).Order(), seen.Steps.Select(step => step.A));
        Assert.Equal(byte.MaxValue, seen.Steps[^1].A);
    }

    private static Color Colour(object? brush) => brush is ISolidColorBrush solid ? solid.Color : default;
    private static bool Near(byte mixed, byte target) => Math.Abs(mixed - target) <= 1;

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
    [InlineData(typeof(RuntimePromptModal))]
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

    [Fact]
    public async Task TextOnlyCta_CentersItsText()
    {
        var (left, right) = await HeadlessApp.RunAsync(() =>
        {
            var text = new TextBlock { Classes = { "action-lg" }, Text = "Use it" };
            var cta = new Button { Classes = { "cta" }, Content = text, HorizontalAlignment = HorizontalAlignment.Left };
            var window = new Window { Width = 400, Height = 300, Content = new StackPanel { Children = { cta } } };
            window.Show();
            window.UpdateLayout();
            var start = text.TranslatePoint(new Point(0, 0), cta)!.Value.X;
            var result = (start, cta.Bounds.Width - start - text.Bounds.Width);
            window.Close();
            return Task.FromResult(result);
        });

        Assert.InRange(right - left, -1, 1);
    }

    [Fact]
    public async Task CtaWithAnIcon_KeepsLessRoomOnTheLeft()
    {
        var padding = await HeadlessApp.RunAsync(() =>
        {
            var cta = new Button { Classes = { "cta", "with-icon" }, Content = "Play" };
            var window = new Window { Width = 400, Height = 300, Content = new StackPanel { Children = { cta } } };
            window.Show();
            window.UpdateLayout();
            var result = cta.Padding;
            window.Close();
            return Task.FromResult(result);
        });

        Assert.Equal(new Thickness(20, 0, 24, 0), padding);
    }

    [Fact]
    public void EveryCtaThatStartsWithAnIcon_CarriesWithIcon_AndNoOtherCtaDoes()
    {
        var app = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Borea.App"));
        var ctas = Directory.EnumerateFiles(app, "*.axaml", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(app, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .SelectMany(path => XDocument.Load(path, LoadOptions.SetLineInfo).Descendants()
                .Where(element => element.Name.LocalName == "Button" && ClassesOf(element).Contains("cta"))
                .Select(button => (Where: $"{Path.GetFileName(path)} line {((IXmlLineInfo)button).LineNumber}", Button: button)))
            .ToList();

        Assert.NotEmpty(ctas);
        Assert.All(ctas, cta => Assert.True(StartsWithAnIcon(cta.Button) == ClassesOf(cta.Button).Contains("with-icon"), cta.Where));
    }

    private static string[] ClassesOf(XElement element)
        => ((string?)element.Attribute("Classes") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static XElement? FirstContent(XElement element)
        => element.Elements().FirstOrDefault(child => !child.Name.LocalName.Contains('.'));

    private static bool StartsWithAnIcon(XElement button)
    {
        var content = FirstContent(button);
        if (content?.Name.LocalName == "StackPanel")
            content = FirstContent(content);
        return content?.Name.LocalName is "Path" or "PathIcon";
    }

    private static Point Center(Visual target, Window window)
        => target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
}
