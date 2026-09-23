using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class PackNewerReleasesViewTests
{
    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task ModpacksRow_ShowsTheCountInsideTheRow(double windowWidth)
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var text = viewModel.DiscoverPacks.Single().NewerReleasesText!;

        var outside = await RenderAsync(harness, () => new DiscoverPage(), windowWidth, page =>
        {
            var row = page.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("card"));
            return [.. Outside(row, Shown(page, text))];
        });

        Assert.Empty(outside);
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task PackPage_ShowsTheCountTheNewerVersionsAndCopyForumListInsideThePage(double windowWidth)
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.DiscoverPacks.Single().OpenCommand.ExecuteAsync(null);
        viewModel.ShowPackModsCommand.Execute(null);
        var count = viewModel.SelectedPack!.NewerReleasesText!;
        var newer = viewModel.PackMembers.Select(member => member.NewerText).OfType<string>().ToList();

        var outside = await RenderAsync(harness, () => new PackPage(), windowWidth, page =>
        {
            var body = page.GetVisualDescendants().OfType<PageBodyPanel>().Single().GetVisualChildren().OfType<StackPanel>().Single();
            var panel = page.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("side-panel"));
            return
            [
                .. Outside(body, Shown(page, count)),
                .. newer.SelectMany(text => Outside(body, Shown(page, text))),
                .. Outside(panel, Shown(page, harness.Localization.PackCopyForumList)),
            ];
        });

        Assert.Empty(outside);
    }

    /// <summary>A pack of three mods where two have a newer release.</summary>
    private static async Task<ViewModelHarness> CreateAsync()
    {
        var harness = await ViewModelHarness.CreateAsync(editSnapshot: PackViewModelTests.WithPacks(PackViewModelTests.Pack(
            "starter-pack",
            "Starter Pack",
            PackViewModelTests.Version("1.0.0", PackViewModelTests.Pin("AdvancedFlightComputer", "0.7.4"), PackViewModelTests.Pin("KSArmory", "0.8.44"), PackViewModelTests.Pin("MeasureTools", "1.1.9")))));
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        harness.ViewModel.ShowDiscoverModpacksCommand.Execute(null);
        return harness;
    }

    /// <summary>Renders the page next to a navigation rail, as the main window does.</summary>
    private static Task<List<string>> RenderAsync(ViewModelHarness harness, Func<Control> createPage, double windowWidth, Func<Control, List<string>> read) =>
        HeadlessApp.RunAsync(harness, () =>
        {
            var page = createPage();
            page.DataContext = harness.ViewModel;
            Grid.SetColumn(page, 1);
            var body = new Grid { ColumnDefinitions = new ColumnDefinitions($"{PageBodyPanel.NavigationRailWidth},*"), Children = { page } };
            var window = new Window { Width = windowWidth, Height = 832, Content = body, DataContext = harness.ViewModel };
            window.Show();
            window.UpdateLayout();
            try
            {
                return Task.FromResult(read(page));
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>The one visible text block that shows <paramref name="text"/>, or fails.</summary>
    private static TextBlock Shown(Control page, string text)
        => page.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == text && block.IsEffectivelyVisible);

    private static IEnumerable<string> Outside(Control container, TextBlock text)
    {
        var corner = text.TranslatePoint(new Point(0, 0), container) ?? throw new InvalidOperationException($"{text.Text} is not laid out inside {container}.");
        var fits = corner.X >= 0 && corner.Y >= 0
            && corner.X + text.Bounds.Width <= container.Bounds.Width + 0.5
            && corner.Y + text.Bounds.Height <= container.Bounds.Height + 0.5
            && !text.TextLayout.TextLines.Any(line => line.HasCollapsed);
        return fits ? [] : [text.Text ?? string.Empty];
    }
}
