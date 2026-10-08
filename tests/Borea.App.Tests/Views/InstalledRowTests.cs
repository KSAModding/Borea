using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;

namespace Borea.App.Tests.Views;

/// <summary>An installed mod or pack row on Discover shows a green disc with a black check and leads its tags with Installed.</summary>
[Collection(HeadlessCollection.Name)]
public sealed class InstalledRowTests
{
    private sealed record RowLook(
        bool DiscShown,
        Color? Disc,
        Color? DiscOver,
        bool Hovered,
        Color? Check,
        string? FirstChip,
        bool FirstChipPositive,
        Color Card,
        Color Surface,
        Color Compatible,
        Color OnCompatible,
        string? DiscName,
        int InstalledChips);

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public async Task InstalledModRow_ShowsTheGreenDisc_AndInstalledAsTheFirstTag(string theme)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        var row = harness.ViewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");
        row.IsInstalled = true;

        var look = await LookAsync(harness, theme, row);

        AssertInstalled(harness, look, row.Name);
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public async Task InstalledPackRow_ShowsTheGreenDisc_AndInstalledAsTheFirstTag(string theme)
    {
        using var harness = await PackHarnessAsync();
        var pack = harness.ViewModel.DiscoverPacks.Single(item => item.PackId == "armory-pack");
        pack.IsInstalled = true;

        var look = await LookAsync(harness, theme, pack);

        AssertInstalled(harness, look, pack.Name);
    }

    [Fact]
    public async Task ModRowThatIsNotInstalled_ShowsNeitherTheDiscNorTheChip()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        var row = harness.ViewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");
        Assert.False(row.IsInstalled);

        var look = await LookAsync(harness, "Dark", row);

        Assert.False(look.DiscShown);
        Assert.NotEqual(harness.Localization.DiscoverInstalled, look.FirstChip);
        Assert.Equal(0, look.InstalledChips);
    }

    [Fact]
    public async Task PackRowThatIsNotInstalled_ShowsNeitherTheDiscNorTheChip()
    {
        using var harness = await PackHarnessAsync();
        var pack = harness.ViewModel.DiscoverPacks.Single(item => item.PackId == "armory-pack");
        Assert.False(pack.IsInstalled);

        var look = await LookAsync(harness, "Dark", pack);

        Assert.False(look.DiscShown);
        Assert.NotEqual(harness.Localization.DiscoverInstalled, look.FirstChip);
        Assert.Equal("Starter", look.FirstChip);
        Assert.Equal(0, look.InstalledChips);
    }

    private static void AssertInstalled(ViewModelHarness harness, RowLook look, string name)
    {
        Assert.True(look.DiscShown);
        Assert.Equal(harness.Localization.FormatDiscoverInstalledMod(name), look.DiscName);
        Assert.Equal(look.Compatible, look.Disc);
        Assert.True(look.Hovered);
        Assert.Equal(look.Compatible, look.DiscOver);
        Assert.Equal(Colors.Black, look.OnCompatible);
        Assert.Equal(look.OnCompatible, look.Check);
        Assert.Equal(harness.Localization.DiscoverInstalled, look.FirstChip);
        Assert.True(look.FirstChipPositive);

        // the row shows Installed once, in its tags, and no longer as a chip of its own on the right
        Assert.Equal(1, look.InstalledChips);
        Assert.Equal(look.Surface, look.Card);
    }

    private static async Task<ViewModelHarness> PackHarnessAsync()
    {
        var harness = await ViewModelHarness.CreateAsync(editSnapshot: PackViewModelTests.WithPacks(
            PackViewModelTests.Pack("armory-pack", "Armory Pack", PackViewModelTests.Version("1.0.0", PackViewModelTests.Pin("KSArmory", "0.8.44")))));
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        harness.ViewModel.ShowDiscoverModpacksCommand.Execute(null);
        return harness;
    }

    /// <summary>Renders Discover in the theme and reads the row of <paramref name="item"/>, then its disc again under the pointer.</summary>
    private static Task<RowLook> LookAsync(ViewModelHarness harness, string theme, object item) =>
        HeadlessApp.RunAsync(harness, () =>
        {
            Application.Current!.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var page = new DiscoverPage();
            var window = new Window { Width = 1280, Height = 900, DataContext = harness.ViewModel, Content = page };
            window.Show();
            try
            {
                window.UpdateLayout();
                var row = page.GetVisualDescendants().OfType<Button>()
                    .Single(button => button.Classes.Contains("card-button") && ReferenceEquals(button.DataContext, item));
                var card = row.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("card"));
                var disc = row.GetVisualDescendants().OfType<Button>()
                    .Single(button => button.Classes.Contains("add") && button.Classes.Contains("installed"));
                var tags = row.GetVisualDescendants().OfType<TagChipRow>().Single();
                var first = tags.Children.OfType<Border>()
                    .Where(chip => chip.Bounds.Width > 0)
                    .OrderBy(chip => chip.Bounds.X)
                    .FirstOrDefault();
                var shown = disc.IsEffectivelyVisible;
                var discColor = shown ? Fill(Presenter(disc).Background) : null;
                var check = shown ? Fill(disc.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single().Stroke) : null;
                var cardColor = Fill(card.Background)!.Value;
                var installedChips = row.GetVisualDescendants().OfType<Border>()
                    .Count(chip => chip.IsEffectivelyVisible && chip.Bounds.Width > 0 && chip.Classes.Contains("positive")
                        && (chip.Child as TextBlock)?.Text == harness.Localization.DiscoverInstalled);

                Color? over = null;
                var hovered = false;
                if (shown)
                {
                    window.MouseMove(disc.TranslatePoint(new Point(disc.Bounds.Width / 2, disc.Bounds.Height / 2), window)!.Value);
                    window.UpdateLayout();
                    over = Fill(Presenter(disc).Background);
                    hovered = disc.IsPointerOver;
                }

                return Task.FromResult(new RowLook(
                    shown,
                    discColor,
                    over,
                    hovered,
                    check,
                    (first?.Child as TextBlock)?.Text,
                    first?.Classes.Contains("positive") == true,
                    cardColor,
                    Token(page, "Color.Surface"),
                    Token(page, "Color.Compatible"),
                    Token(page, "Color.OnCompatible"),
                    shown ? AutomationProperties.GetName(disc) : null,
                    installedChips));
            }
            finally
            {
                window.Close();
            }
        });

    private static ContentPresenter Presenter(Button button) =>
        button.GetVisualDescendants().OfType<ContentPresenter>().First(presenter => presenter.Name == "PART_ContentPresenter");

    private static Color? Fill(IBrush? brush) => (brush as ISolidColorBrush)?.Color;

    private static Color Token(StyledElement element, string key)
    {
        Assert.True(element.TryFindResource(key, element.ActualThemeVariant, out var value), $"The theme has no {key}.");
        return (Color)value!;
    }
}
