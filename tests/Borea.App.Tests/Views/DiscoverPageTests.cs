using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;
using Borea.Core.Instances;

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
    public async Task InstalledInOtherInstances_IsOfferedOnlyWithAnotherInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        bool Offered(DiscoverPage page) => page.GetVisualDescendants().OfType<ToggleSwitch>()
            .Single(toggle => AutomationProperties.GetName(toggle) == harness.Localization.DiscoverInstalledInOtherInstances).IsEffectivelyVisible;
        await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        Assert.Empty(viewModel.OtherInstances);
        var alone = await RenderAsync(harness, 1280, Offered);

        await harness.Services.Instances.CreateAsync("Other", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        Assert.Single(viewModel.OtherInstances);
        var withAnother = await RenderAsync(harness, 1280, Offered);

        Assert.False(alone);
        Assert.True(withAnother);
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
        Assert.Empty(viewModel.SelectedLicenses);
    }

    [Fact]
    public async Task License_SeveralStayChosen_AndEachChipTakesOneAway()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: DiscoverViewModelTests.WithMixedLicenses);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        var (active, chips, left) = await RenderAsync(harness, 1280, page =>
        {
            Button Option(string license) => SidePanel(page).GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == license);
            List<string?> Chips()
            {
                page.UpdateLayout();
                return page.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.Classes.Contains("chip-button") && button.IsEffectivelyVisible && button.Command == viewModel.ToggleLicenseCommand)
                    .Select(button => button.GetVisualDescendants().OfType<TextBlock>().Single().Text)
                    .ToList();
            }

            Click(Option("MIT"));
            Click(Option("CC-BY-SA-4.0"));
            var active = viewModel.LicenseOptions.Where(license => Option(license.Value).Classes.Contains("active")).Select(license => license.Value).ToList();
            var chips = Chips();

            Click(page.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("chip-button") && button.CommandParameter as string == "CC-BY-SA-4.0"));
            return (active, chips, Chips());
        });

        Assert.Equal(["MIT", "CC-BY-SA-4.0"], active);
        Assert.Equal(["MIT", "CC-BY-SA-4.0"], chips);
        Assert.Equal(["MIT"], left);
        Assert.Equal(["AdvancedFlightComputer", "MeasureTools"], viewModel.DiscoverItems.Select(item => item.ModId));
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
        viewModel.InstalledInOtherInstances = true;
        viewModel.SelectOsCommand.Execute("windows");
        viewModel.ToggleLicenseCommand.Execute("MIT");
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

        Assert.Equal(viewModel.CategoryOptions.Count + 8, chips);
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

    [Fact]
    public async Task BackToTop_ShowsPastOneViewport_AndAClickTakesTheListUpWithItsSearchFiltersAndRows()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => ViewModelHarness.WithCopies(json, "KSArmory", 60));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.SearchText = "KSArmory";
        viewModel.HideInstalled = true;
        var rows = viewModel.DiscoverItems.ToList();

        var seen = await RenderAsync(harness, 1280, async page =>
        {
            var (scroller, button) = BackToTop(page);
            var atTop = button.IsEffectivelyVisible;
            scroller.Offset = new Vector(0, scroller.Viewport.Height / 2);
            var nearTop = button.IsEffectivelyVisible;
            scroller.Offset = new Vector(0, 2 * scroller.Viewport.Height);
            var farDown = (button.IsEffectivelyVisible, scroller.Offset.Y);
            Click(button);
            await HeadlessApp.FramesAsync();
            return (atTop, nearTop, farDown, AfterClick: button.IsEffectivelyVisible, Offset: scroller.Offset.Y);
        });

        Assert.False(seen.atTop);
        Assert.False(seen.nearTop);
        Assert.True(seen.farDown.IsEffectivelyVisible);
        Assert.True(seen.farDown.Y > 0, "The list must be long enough to scroll two viewports, or the test proves nothing.");
        Assert.False(seen.AfterClick);
        Assert.Equal(0, seen.Offset);
        Assert.Equal("KSArmory", viewModel.SearchText);
        Assert.True(viewModel.HideInstalled);
        Assert.Equal(61, rows.Count);
        Assert.Equal(rows, viewModel.DiscoverItems);
    }

    [Fact]
    public async Task BackToTop_ShowsItsLabelAndAName_AndTheKeyboardReachesItAndGoesOnFromTheTop()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => ViewModelHarness.WithCopies(json, "KSArmory", 60));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        var seen = await RenderAsync(harness, 1280, async page =>
        {
            var (scroller, button) = BackToTop(page);
            var window = (Window)TopLevel.GetTopLevel(page)!;
            scroller.Offset = new Vector(0, 2 * scroller.Viewport.Height);
            window.UpdateLayout();

            // the button sits between the list and the side panel in the tab order
            SidePanel(page).GetVisualDescendants().OfType<TextBox>().First().Focus(NavigationMethod.Tab);
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Shift);
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.Shift);
            var reached = button.IsFocused;
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            var refresh = page.GetVisualDescendants().OfType<Button>().Single(candidate => candidate.Command == viewModel.RefreshContentIndexCommand);
            var label = button.GetVisualDescendants().OfType<TextBlock>().Single().Text;
            await HeadlessApp.FramesAsync();
            return (Tip: ToolTip.GetTip(button), Label: label, Name: AutomationProperties.GetName(button), reached, Offset: scroller.Offset.Y, TopFocused: refresh.IsFocused);
        });

        // the label is on the button, so a tooltip would only repeat it
        Assert.Null(seen.Tip);
        Assert.Equal(harness.Localization.DiscoverBackToTop, seen.Label);
        Assert.Equal(harness.Localization.DiscoverBackToTop, seen.Name);
        Assert.True(seen.reached);
        Assert.Equal(0, seen.Offset);
        Assert.True(seen.TopFocused);
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

    private static Task<T> RenderAsync<T>(ViewModelHarness harness, double windowWidth, Func<DiscoverPage, Task<T>> read, double windowHeight = 832) =>
        HeadlessApp.RunAsync(harness, async () =>
        {
            var page = new DiscoverPage { DataContext = harness.ViewModel };
            Grid.SetColumn(page, 1);
            var body = new Grid { ColumnDefinitions = new ColumnDefinitions($"{PageBodyPanel.NavigationRailWidth},*"), Children = { page } };
            var window = new Window { Width = windowWidth, Height = windowHeight, Content = body, DataContext = harness.ViewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                return await read(page);
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

    /// <summary>The scroll viewer of the list, which is the first one of the page, and the button that takes it back to the top.</summary>
    private static (ScrollViewer Scroller, ScrollToTopButton Button) BackToTop(Visual page) =>
        (page.GetVisualDescendants().OfType<ScrollViewer>().First(), page.GetVisualDescendants().OfType<ScrollToTopButton>().Single());

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
