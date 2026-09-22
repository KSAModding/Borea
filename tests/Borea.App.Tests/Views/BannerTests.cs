using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Borea.App.Views;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class BannerTests
{
    private sealed record Parts(bool Title, bool Message, bool SecondaryAction, bool Action, bool Dismiss);

    private static Parts VisibleParts(Banner banner)
    {
        bool Visible(string name) => banner.GetVisualDescendants().OfType<Control>().Single(control => control.Name == name).IsEffectivelyVisible;

        return new Parts(Visible("PART_Title"), Visible("PART_Message"), Visible("PART_SecondaryAction"), Visible("PART_Action"), Visible("PART_Dismiss"));
    }

    /// <summary>Builds the banner on the UI thread, which owns every control.</summary>
    private static async Task<T> RenderAsync<T>(Func<Banner> create, Func<Banner, T> read)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await HeadlessApp.Session.Dispatch(() =>
        {
            var banner = create();
            var window = new Window { Width = 800, Height = 300, Content = banner };
            window.Show();
            banner.UpdateLayout();
            var result = read(banner);
            window.Close();
            return result;
        }, timeout.Token);
    }

    [Fact]
    public async Task MessageOnly_ShowsOnlyTheMessage()
    {
        var parts = await RenderAsync(() => new Banner { Message = "A short message." }, VisibleParts);

        Assert.Equal(new Parts(Title: false, Message: true, SecondaryAction: false, Action: false, Dismiss: false), parts);
    }

    [Fact]
    public async Task MessageAndAction_ShowsTheActionWithoutTheCloseButton()
    {
        var parts = await RenderAsync(() => new Banner { Message = "A short message.", ActionText = "Take action", ActionCommand = new RelayCommand(() => { }) }, VisibleParts);

        Assert.Equal(new Parts(Title: false, Message: true, SecondaryAction: false, Action: true, Dismiss: false), parts);
    }

    [Fact]
    public async Task EveryPart_ShowsTitleMessageBothActionsAndCloseButton()
    {
        Banner Create() => new()
        {
            Title = "A title",
            Message = "The body.",
            SecondaryActionText = "Do it now",
            SecondaryActionCommand = new RelayCommand(() => { }),
            ActionText = "Take action",
            ActionCommand = new RelayCommand(() => { }),
            DismissCommand = new RelayCommand(() => { }),
        };

        var parts = await RenderAsync(Create, VisibleParts);

        Assert.Equal(new Parts(Title: true, Message: true, SecondaryAction: true, Action: true, Dismiss: true), parts);
    }

    [Fact]
    public async Task ActionAndClose_RunTheirCommands()
    {
        object? actionParameter = null;
        var secondaryRan = false;
        var dismissed = false;
        Banner Create() => new()
        {
            Message = "A short message.",
            SecondaryActionText = "Do it now",
            SecondaryActionCommand = new RelayCommand(() => secondaryRan = true),
            ActionText = "Take action",
            ActionCommand = new RelayCommand<object?>(parameter => actionParameter = parameter),
            ActionCommandParameter = "https://example.com",
            DismissCommand = new RelayCommand(() => dismissed = true),
        };

        await RenderAsync(Create, rendered =>
        {
            foreach (var button in rendered.GetVisualDescendants().OfType<Button>())
                button.Command!.Execute(button.CommandParameter);
            return true;
        });

        Assert.Equal("https://example.com", actionParameter);
        Assert.True(secondaryRan);
        Assert.True(dismissed);
    }

    [Theory]
    [InlineData(BannerKind.Neutral, "Brush.SurfaceHeader", "Brush.SurfaceHeader", "Brush.TextSecondary")]
    [InlineData(BannerKind.Accent, "Brush.Accent", "Brush.Accent", "Brush.OnAccent")]
    [InlineData(BannerKind.Outline, "Brush.SurfaceHeader", "Brush.Accent", "Brush.TextSecondary")]
    [InlineData(BannerKind.Error, "Brush.DangerSoft", "Brush.Danger", "Brush.Danger")]
    [InlineData(BannerKind.Success, "Brush.PositiveSoft", "Brush.Positive", "Brush.Positive")]
    public async Task Kind_TakesTheColorsFromTheThemeTokens(BannerKind kind, string background, string border, string text)
    {
        var colors = await RenderAsync(() => new Banner { Kind = kind, Message = "A short message." }, banner =>
        {
            IBrush? Token(string key) => banner.TryFindResource(key, out var value) ? value as IBrush : null;
            return (
                Background: banner.Background == Token(background),
                Border: banner.BorderBrush == Token(border),
                Text: banner.Foreground == Token(text));
        });

        Assert.Equal((true, true, true), colors);
    }

    [Theory]
    [InlineData(BannerKind.Accent, "Brush.OnAccent")]
    [InlineData(BannerKind.Error, "Brush.Text")]
    [InlineData(BannerKind.Success, "Brush.Text")]
    [InlineData(BannerKind.Neutral, "Brush.Accent")]
    public async Task Action_UsesTheBrushOfItsKind(BannerKind kind, string brushKey)
    {
        var (actual, expected) = await RenderAsync(
            () => new Banner { Kind = kind, Message = "A short message.", ActionText = "Take action", ActionCommand = new RelayCommand(() => { }), DismissCommand = new RelayCommand(() => { }) },
            banner =>
            {
                var text = banner.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Name == "PART_ActionText");
                banner.TryFindResource(brushKey, banner.ActualThemeVariant, out var brush);
                return (((ISolidColorBrush)text.Foreground!).Color, ((ISolidColorBrush)brush!).Color);
            });

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(BannerKind.Neutral, "Brush.TextSecondary")]
    [InlineData(BannerKind.Outline, "Brush.TextSecondary")]
    [InlineData(BannerKind.Accent, "Brush.OnAccent")]
    [InlineData(BannerKind.Error, "Brush.Text")]
    [InlineData(BannerKind.Success, "Brush.Text")]
    public async Task CloseIcon_UsesTheBrushOfItsKind(BannerKind kind, string brushKey)
    {
        var (actual, expected) = await RenderAsync(
            () => new Banner { Kind = kind, Message = "A short message.", DismissCommand = new RelayCommand(() => { }) },
            banner =>
            {
                var icon = banner.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(path => path.Name == "PART_DismissIcon");
                banner.TryFindResource(brushKey, banner.ActualThemeVariant, out var brush);
                return (((ISolidColorBrush)icon.Stroke!).Color, ((ISolidColorBrush)brush!).Color);
            });

        Assert.Equal(expected, actual);
    }
}
