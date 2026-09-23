using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.Core.Mods;

namespace Borea.App.Tests.Views;

/// <summary>
/// The top of the content page: the banner that names where the page was
/// opened from, and the place of the Installed chip.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ContentHeaderTests
{
    private const string ModId = "AdvancedFlightComputer";

    private static Task<T> OnContentPageAsync<T>(ViewModelHarness harness, double width, Func<Window, Control, T> read) =>
        HeadlessApp.RunAsync(harness, () =>
        {
            var page = new Borea.App.Views.Pages.ContentPage();
            var window = new Window { Width = width, Height = 900, DataContext = harness.ViewModel, Content = page };
            window.Show();
            try
            {
                page.UpdateLayout();
                return Task.FromResult(read(window, page));
            }
            finally
            {
                window.Close();
            }
        });

    private static Rect BoundsIn(Visual page, Visual control)
        => new(control.TranslatePoint(default, page)!.Value, control.Bounds.Size);

    private static TextBlock? Shown(Control page, string text)
        => page.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(block => block.IsEffectivelyVisible && block.Text == text);

    private static Border InstalledChip(Control page, string installed)
        => page.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Classes.Contains("chip") && border.IsEffectivelyVisible && border.Child is TextBlock { Text: var text } && text == installed);

    private static Button RemoveButton(Control page)
        => page.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && button.Classes.Contains("danger"));

    private static async Task<ViewModelHarness> InstalledAsync()
    {
        var harness = await ViewModelHarness.CreateAsync();
        await InstalledContent.AddAsync(harness, ModId, activate: true, ownership: ModInstallOwnership.Borea);
        await harness.ViewModel.LoadAsync();
        return harness;
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task OpenedFromAnInstance_TheInstalledChipLeadsTheMetaRowAndRemoveStaysInTheActions(double width)
    {
        using var harness = await InstalledAsync();
        var viewModel = harness.ViewModel;
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.ContentGroups.SelectMany(group => group.Items).Single().OpenCommand.ExecuteAsync(null);

        var (chip, remove, name) = await OnContentPageAsync(harness, width, (_, page) => (
            BoundsIn(page, InstalledChip(page, harness.Localization.DiscoverInstalled)),
            BoundsIn(page, RemoveButton(page)),
            BoundsIn(page, Shown(page, viewModel.SelectedContent!.Name)!)));

        Assert.True(chip.Top >= remove.Bottom, $"The chip at {chip} is not below Remove at {remove}.");
        Assert.Equal(name.Left, chip.Left, 0.5);
    }

    [Fact]
    public async Task OpenedFromDiscover_TheInstalledChipStaysBesideRemove()
    {
        using var harness = await InstalledAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == ModId).OpenCommand.ExecuteAsync(null);

        var (chip, remove) = await OnContentPageAsync(harness, 1280, (_, page) => (
            BoundsIn(page, InstalledChip(page, harness.Localization.DiscoverInstalled)),
            BoundsIn(page, RemoveButton(page))));

        Assert.Equal(remove.Center.Y, chip.Center.Y, 0.5);
        Assert.True(chip.Right <= remove.Left);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpenedFromHomeOrDiscover_ShowsNoBanner(bool fromHome)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        if (fromHome)
        {
            await viewModel.RecentItems.Single(item => item.ModId == ModId).OpenCommand.ExecuteAsync(null);
        }
        else
        {
            await viewModel.EnsureDiscoverLoadedAsync();
            await viewModel.DiscoverItems.Single(item => item.ModId == ModId).OpenCommand.ExecuteAsync(null);
        }

        var banner = await OnContentPageAsync(harness, 1280, (_, page) => Shown(page, harness.Localization.ContentInsideInstance));

        Assert.Null(banner);
    }
}
