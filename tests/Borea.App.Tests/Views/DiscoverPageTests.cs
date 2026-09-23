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

    /// <summary>Renders Discover next to a navigation rail, as the main window does.</summary>
    private static Task<T> RenderAsync<T>(ViewModelHarness harness, double windowWidth, Func<DiscoverPage, T> read) =>
        HeadlessApp.RunAsync(harness, () =>
        {
            var page = new DiscoverPage { DataContext = harness.ViewModel };
            Grid.SetColumn(page, 1);
            var body = new Grid { ColumnDefinitions = new ColumnDefinitions($"{PageBodyPanel.NavigationRailWidth},*"), Children = { page } };
            var window = new Window { Width = windowWidth, Height = 832, Content = body, DataContext = harness.ViewModel };
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
}
