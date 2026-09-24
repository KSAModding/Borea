using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class DiscoverPageTests
{
    private sealed record GameVersionControls(GameVersionOption? Min, GameVersionOption? Max, int ClearButtons, bool CanClearMin, bool CanClearMax);

    [Fact]
    public async Task GameVersion_EachBoxClearsItsOwnBound()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var older = viewModel.GameVersionOptions.Single(build => build.Revision == 5261);
        var newer = viewModel.GameVersionOptions.Single(build => build.Revision == 5402);

        var (maxCleared, bothCleared) = await RenderAsync(harness, 1280, page =>
        {
            viewModel.DiscoverGameMin = older;
            viewModel.DiscoverGameMax = newer;
            Click(SidePanelButton(page, viewModel.ClearDiscoverGameMaxCommand));
            var maxCleared = ReadGameVersionControls(viewModel, page);

            Click(SidePanelButton(page, viewModel.ClearDiscoverGameMinCommand));
            return (maxCleared, ReadGameVersionControls(viewModel, page));
        });

        Assert.Equal(new GameVersionControls(older, null, 1, true, false), maxCleared);
        Assert.Equal(new GameVersionControls(null, null, 0, false, false), bothCleared);
    }

    [Fact]
    public async Task License_TheChosenOneIsMarkedUntilItIsChosenAgain()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        var marks = await RenderAsync(harness, 1280, page =>
        {
            var license = page.GetVisualDescendants().OfType<Button>().Single(button => button.Content is "MIT");
            var before = license.Classes.Contains("active");
            Click(license);
            var chosen = license.Classes.Contains("active");
            Click(license);
            return (before, chosen, license.Classes.Contains("active"));
        });

        Assert.Equal((false, true, false), marks);
        Assert.Null(viewModel.SelectedLicense);
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task ActiveFilters_EveryChipStaysInsideTheFilterRow(double windowWidth)
    {
        var tags = ViewModelHarness.CuratedTags(("control", "Flight control and autopilots"), ("parts", "Parts"), ("weapons", "Weapons and armament"), ("physics", "Physics and simulation"), ("information", "Information and readouts"));
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: tags);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        foreach (var category in viewModel.CategoryOptions)
            viewModel.ToggleCategoryCommand.Execute(category);
        viewModel.HideInstalled = true;
        viewModel.HideIncompatible = true;
        viewModel.FavoritesOnly = true;
        viewModel.SelectOsCommand.Execute("windows");
        viewModel.SelectLicenseCommand.Execute("MIT");
        viewModel.DiscoverGameMin = viewModel.GameVersionOptions.Single(build => build.Revision == 5261);
        viewModel.DiscoverGameMax = viewModel.GameVersionOptions.Single(build => build.Revision == 5402);

        var (chips, outside) = await RenderAsync(harness, windowWidth, page =>
        {
            var body = page.GetVisualDescendants().OfType<PageBodyPanel>().Single().Children.Single();
            var sort = page.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("dropdown"));
            var left = Corner(body, page).X;
            var row = new Rect(left, 0, Corner(sort, page).X - left, page.Bounds.Height);
            var chips = page.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("chip-button") && button.IsEffectivelyVisible).ToList();
            var outside = chips
                .Where(chip => !row.Contains(new Rect(Corner(chip, page), chip.Bounds.Size)))
                .Select(chip => string.Concat(chip.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text)))
                .ToList();
            return (chips.Count, outside);
        });

        Assert.Equal(viewModel.CategoryOptions.Count + 7, chips);
        Assert.Empty(outside);
    }

    [Fact]
    public async Task ListYourMod_TheTextKeepsRoomInsideTheHoverAndLinesUpWithTheSection()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        var (left, right, offset) = await RenderAsync(harness, 1280, page =>
        {
            var link = SidePanelButton(page, viewModel.OpenListingCommand);
            var text = link.GetVisualDescendants().OfType<TextBlock>().Single();
            var heading = SidePanel(page).GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == viewModel.Localization.DiscoverForModAuthors);
            var start = Corner(text, link).X;
            return (start, link.Bounds.Width - start - text.Bounds.Width, Corner(text, page).X - Corner(heading, page).X);
        });

        Assert.True(left >= 8, $"The text starts {left} px inside the link.");
        Assert.True(right >= 8, $"The text ends {right} px inside the link.");
        Assert.Equal(0, offset, 0.5);
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task Count_StaysAtTheFootOfTheSidePanelWhileItsFiltersScroll(double windowWidth)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.SearchText = "advanced flight";

        var (overflows, text, before, after) = await RenderAsync(harness, windowWidth, page =>
        {
            var panel = SidePanel(page);
            var filters = panel.GetVisualDescendants().OfType<ScrollViewer>().First();
            var count = panel.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == viewModel.DiscoverCountText);
            var before = new Rect(Corner(count, panel), count.Bounds.Size);
            filters.Offset = new Vector(0, filters.Extent.Height);
            page.UpdateLayout();
            var after = new Rect(Corner(count, panel), count.Bounds.Size);
            return (filters.Extent.Height > filters.Viewport.Height, count.Text, before, after);
        }, windowHeight: 500);

        Assert.True(overflows, "The filters must be taller than the panel, or the test proves nothing.");
        Assert.Equal("1 of 3 mods", text);
        Assert.Equal(before, after);
        Assert.True(after.Bottom <= 500, $"The count ends at {after.Bottom} px, below the window.");
    }

    /// <summary>Renders Discover next to a navigation rail, as the main window does.</summary>
    private static Task<T> RenderAsync<T>(ViewModelHarness harness, double windowWidth, Func<DiscoverPage, T> read, double windowHeight = 832) =>
        HeadlessApp.RunAsync(harness, () =>
        {
            var page = new DiscoverPage { DataContext = harness.ViewModel };
            Grid.SetColumn(page, 1);
            var body = new Grid { ColumnDefinitions = new ColumnDefinitions($"{PageBodyPanel.NavigationRailWidth},*"), Children = { page } };
            var window = new Window { Width = windowWidth, Height = windowHeight, Content = body, DataContext = harness.ViewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                return Task.FromResult(read(page));
            }
            finally
            {
                window.Close();
            }
        });

    private static GameVersionControls ReadGameVersionControls(MainViewModel viewModel, Control page)
    {
        page.UpdateLayout();
        return new GameVersionControls(
            viewModel.DiscoverGameMin,
            viewModel.DiscoverGameMax,
            page.GetVisualDescendants().OfType<Button>().Count(button => button.Command == viewModel.ClearDiscoverGameVersionRangeCommand && button.IsEffectivelyVisible),
            SidePanelButton(page, viewModel.ClearDiscoverGameMinCommand).IsEffectivelyEnabled,
            SidePanelButton(page, viewModel.ClearDiscoverGameMaxCommand).IsEffectivelyEnabled);
    }

    private static Border SidePanel(Visual page) =>
        page.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("side-panel"));

    private static Button SidePanelButton(Visual page, ICommand command) =>
        SidePanel(page).GetVisualDescendants().OfType<Button>().Single(button => button.Command == command);

    private static void Click(Button button)
    {
        var window = (Window)TopLevel.GetTopLevel(button)!;
        window.UpdateLayout();
        var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
    }

    private static Point Corner(Visual control, Visual page) =>
        control.TranslatePoint(new Point(0, 0), page)
        ?? throw new InvalidOperationException($"{control} is not laid out inside the page.");
}
