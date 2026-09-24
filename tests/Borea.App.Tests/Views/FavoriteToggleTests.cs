using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.Core.Instances;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.Tests.Views;

/// <summary>The favorite entry in the menus of the mod page, the pack page and a Discover row, and the star on a Discover row.</summary>
[Collection(HeadlessCollection.Name)]
public sealed class FavoriteToggleTests
{
    private sealed record Entry(object? HeaderBefore, object? HeaderAfter);

    private static void ClickCenter(Window window, Visual target)
    {
        var center = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        window.UpdateLayout();
    }

    /// <summary>
    /// Opens the menu of <paramref name="opener"/> and returns its entry with the command.
    /// The menu lives in a layer of its own above the page, so the entry is looked up from the window.
    /// </summary>
    private static MenuItem MenuEntry(Window window, Button opener, ICommand command)
    {
        ClickCenter(window, opener);
        return window.GetVisualDescendants().OfType<MenuItem>().Single(entry => ReferenceEquals(entry.Command, command));
    }

    /// <summary>Toggles the favorite through a menu and reads its entry before and after.</summary>
    private static Task<Entry> ToggleInMenuAsync(ViewModelHarness harness, Func<Control> createPage, Func<Control, Button> opener, IAsyncRelayCommand command) =>
        HeadlessApp.RunAsync(harness, async () =>
        {
            var page = createPage();
            var window = new Window { Width = 1280, Height = 900, DataContext = harness.ViewModel, Content = page };
            window.Show();
            try
            {
                page.UpdateLayout();
                var entry = MenuEntry(window, opener(page), command);
                var before = entry.Header;
                ClickCenter(window, entry);
                if (command.ExecutionTask is { } running)
                    await running;

                return new Entry(before, MenuEntry(window, opener(page), command).Header);
            }
            finally
            {
                window.Close();
            }
        });

    private static Func<Control, Button> MoreMenu(ViewModelHarness harness) => page =>
        page.GetVisualDescendants().OfType<Button>()
            .Single(button => button.IsEffectivelyVisible && button.Flyout is not null && AutomationProperties.GetName(button) == harness.Localization.LibraryMoreActions);

    [Fact]
    public async Task MoreMenuOfTheModPage_MarksTheMod()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var row = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");
        await row.OpenCommand.ExecuteAsync(null);

        var entry = await ToggleInMenuAsync(harness, () => new Borea.App.Views.Pages.ContentPage(), MoreMenu(harness), row.ToggleFavoriteCommand);

        Assert.Equal(new Entry(harness.Localization.ContentAddFavorite, harness.Localization.ContentRemoveFavorite), entry);
        Assert.Equal(["AdvancedFlightComputer"], await harness.Services.ModFavorites.GetFavoriteModIdsAsync());
    }

    [Fact]
    public async Task MoreMenuOfAModPageFromAnInactiveInstance_StillMarksTheMod()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: false);
        var second = (await harness.Services.Instances.CreateAsync("Second", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(second.InstanceId);
        await viewModel.LoadAsync();
        await viewModel.Instances.Single(instance => instance.Name == "Main").OpenCommand.ExecuteAsync(null);
        await viewModel.ContentGroups.SelectMany(group => group.Items).Single().OpenCommand.ExecuteAsync(null);
        Assert.False(viewModel.CanActOnSelectedContent);

        var entry = await ToggleInMenuAsync(harness, () => new Borea.App.Views.Pages.ContentPage(), MoreMenu(harness), viewModel.SelectedContent!.ToggleFavoriteCommand);

        Assert.Equal(new Entry(harness.Localization.ContentAddFavorite, harness.Localization.ContentRemoveFavorite), entry);
        Assert.Equal(["AdvancedFlightComputer"], await harness.Services.ModFavorites.GetFavoriteModIdsAsync());
    }

    [Fact]
    public async Task MoreMenuOfThePackPage_MarksThePack()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: PackViewModelTests.WithPacks(
            PackViewModelTests.Pack("armory-pack", "Armory Pack", PackViewModelTests.Version("1.0.0", PackViewModelTests.Pin("KSArmory", "0.8.44")))));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = viewModel.DiscoverPacks.Single();
        await pack.OpenCommand.ExecuteAsync(null);

        var entry = await ToggleInMenuAsync(harness, () => new Borea.App.Views.Pages.PackPage(), MoreMenu(harness), pack.ToggleFavoriteCommand);

        Assert.Equal(new Entry(harness.Localization.ContentAddFavorite, harness.Localization.ContentRemoveFavorite), entry);
        Assert.Equal(["armory-pack"], await harness.Services.ModPackFavorites.GetFavoriteModPackIdsAsync());
    }

    [Fact]
    public async Task DiscoverRow_ShowsTheStarOnlyForAFavorite()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").ToggleFavoriteCommand.ExecuteAsync(null);

        var starred = await HeadlessApp.RunAsync(harness, () =>
        {
            var page = new Borea.App.Views.Pages.DiscoverPage();
            var window = new Window { Width = 1280, Height = 900, DataContext = viewModel, Content = page };
            window.Show();
            try
            {
                page.UpdateLayout();
                var stars = page.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()
                    .Where(path => AutomationProperties.GetName(path) == harness.Localization.DiscoverFavorite)
                    .ToList();
                Assert.True(stars.Count > 1);
                return Task.FromResult(stars.Where(path => path.IsEffectivelyVisible).Select(path => ((DiscoverItem)path.DataContext!).ModId).ToList());
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal(["AdvancedFlightComputer"], starred);
    }

    [Fact]
    public async Task CheckmarkMenuOfAnInstalledDiscoverRow_MarksTheMod()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        var row = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");
        Assert.True(row.IsInstalled);

        var entry = await ToggleInMenuAsync(harness, () => new Borea.App.Views.Pages.DiscoverPage(), page =>
            page.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && button.Classes.Contains("installed") && button.DataContext == row), row.ToggleFavoriteCommand);

        Assert.Equal(new Entry(harness.Localization.ContentAddFavorite, harness.Localization.ContentRemoveFavorite), entry);
        Assert.Equal(["AdvancedFlightComputer"], await harness.Services.ModFavorites.GetFavoriteModIdsAsync());
    }
}
