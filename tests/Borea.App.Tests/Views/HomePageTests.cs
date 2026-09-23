using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class HomePageTests
{
    [Fact]
    public async Task DiscoverMods_TheTextKeepsRoomInsideTheHoverAndItsPlaceInTheRow()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        var (left, right, textRight, rowRight, rowHeight, headingHeight) = await HeadlessApp.RunAsync(harness, () =>
        {
            var page = new HomePage { DataContext = viewModel };
            var window = new Window { Width = 1280, Height = 832, Content = page, DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var text = page.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == viewModel.Localization.HomeDiscoverMods);
                var link = text.GetVisualAncestors().OfType<Button>().First();
                var row = (Grid)link.Parent!;
                var heading = row.Children.OfType<TextBlock>().Single();
                var start = Corner(text, link).X;
                return Task.FromResult((
                    start,
                    link.Bounds.Width - start - text.Bounds.Width,
                    Corner(text, page).X + text.Bounds.Width,
                    Corner(row, page).X + row.Bounds.Width,
                    row.Bounds.Height,
                    heading.Bounds.Height));
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(left >= 8, $"The text starts {left} px inside the link.");
        Assert.True(right >= 8, $"The text ends {right} px inside the link.");
        Assert.Equal(rowRight, textRight, 0.5);
        Assert.Equal(headingHeight, rowHeight, 0.5);
    }

    private static Point Corner(Visual control, Visual page) =>
        control.TranslatePoint(new Point(0, 0), page)
        ?? throw new InvalidOperationException($"{control} is not laid out inside the page.");

    [Theory]
    [InlineData(860, 8)]
    [InlineData(1280, 12)]
    [InlineData(1920, 14)]
    public async Task Tiles_ShowTwoFullRowsInsideTheBody(double window, int shown)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        while (viewModel.RecentItems.Count < MainViewModel.RecentItemCount)
            viewModel.RecentItems.Add(new RecentItem(viewModel, viewModel.RecentItems[0].Listing, DateTimeOffset.UtcNow));

        var (visible, rows, overshoot) = await RenderAsync(harness, window, page =>
        {
            var grid = page.GetVisualDescendants().OfType<TileRowPanel>().Single();
            var tiles = grid.Children.Where(tile => tile.IsEffectivelyVisible).ToList();
            var tops = tiles.Select(tile => tile.Bounds.Top).Distinct().Count();
            return (tiles.Count, tops, tiles.Max(tile => tile.Bounds.Right) - grid.Bounds.Width);
        });

        Assert.Equal(shown, visible);
        Assert.Equal(2, rows);
        Assert.True(overshoot <= 0, $"a tile ends {overshoot} past the body");
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1920)]
    public async Task TileWithoutIcon_CentresALargePlaceholderAboveALongName(double window)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var listing = new ModMetadata(1, "long", "index", "A very long mod name that wraps onto three lines", ["Someone"], "Abstract", "MIT", new Dictionary<string, string> { ["forums"] = "https://forums.example/long" }, "2026.1.1.1");
        viewModel.RecentItems.Clear();
        viewModel.RecentItems.Add(new RecentItem(viewModel, listing, DateTimeOffset.UtcNow));

        var (visible, top, bottom, centre, tileCentre, nameTop) = await RenderAsync(harness, window, page =>
        {
            var tile = page.GetVisualDescendants().OfType<TileRowPanel>().Single().Children.Single();
            var puzzle = tile.GetVisualDescendants().OfType<ListingImageView>().Single().GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single();
            var name = tile.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == listing.Name);
            var start = puzzle.TranslatePoint(default, tile)!.Value;
            var end = puzzle.TranslatePoint(new Point(puzzle.Bounds.Width, puzzle.Bounds.Height), tile)!.Value;
            return (puzzle.IsEffectivelyVisible, start.Y, end.Y, (start.X + end.X) / 2, tile.Bounds.Width / 2, name.TranslatePoint(default, tile)!.Value.Y);
        });

        Assert.True(visible);
        Assert.True(bottom - top >= 48, $"the placeholder is {bottom - top} px tall");
        Assert.Equal(tileCentre, centre, 0.5);
        Assert.True(bottom <= nameTop, $"the placeholder ends at {bottom}, the name starts at {nameTop}");
    }

    [Fact]
    public async Task GameNotSetUp_ShowsTheShortLineInTheHeaderInsteadOfTheBanner()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        Assert.True(harness.ViewModel.NeedsGameSetup);

        var (texts, banners) = await RenderAsync(harness, 1280, page => (
            page.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList(),
            page.GetVisualDescendants().OfType<Banner>().Count(banner => banner.IsEffectivelyVisible)));

        Assert.Contains(harness.Localization.HomeSetupNotSaved, texts);
        Assert.Contains(harness.Localization.SetupBannerAction, texts);
        Assert.DoesNotContain(harness.Localization.HomeNoGameVersion, texts);
        Assert.Equal(0, banners);
    }

    [Fact]
    public async Task CurrentInstall_KeepsAnInstanceSwitchedOffThere()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        await viewModel.Instances.Single().ActivateCommand.ExecuteAsync(null);
        await viewModel.ActiveInstance!.ToggleActiveCommand.ExecuteAsync(null);

        var (names, hint) = await RenderAsync(harness, 1280, page => (
            page.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList(),
            harness.ViewModel.HomeInstanceHintText));

        Assert.Contains("Alpha", names);
        Assert.DoesNotContain(hint, names);
    }

    private static Task<T> RenderAsync<T>(ViewModelHarness harness, double window, Func<Control, T> read) =>
        HeadlessApp.RunAsync(harness, () =>
        {
            var page = new HomePage { DataContext = harness.ViewModel };
            var host = new Window { Width = window - PageBodyPanel.NavigationRailWidth, Height = 832, Content = page, DataContext = harness.ViewModel };
            host.Show();
            try
            {
                host.UpdateLayout();
                return Task.FromResult(read(page));
            }
            finally
            {
                host.Close();
            }
        });
}
