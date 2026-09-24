using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class MouseNavigationTests
{
    [Fact]
    public async Task BackButton_OnAModFromDiscover_GoesBackToDiscoverWithItsFilters()
    {
        using var harness = await OpenModFromDiscoverAsync();
        var viewModel = harness.ViewModel;

        await OnMainWindowAsync(harness, window => PressAsync(window, MouseButton.XButton1, viewModel.GoBackCommand));

        Assert.True(viewModel.CurrentWindowDiscover);
        Assert.False(viewModel.CurrentWindowContent);
        Assert.Equal("Flight", viewModel.SearchText);
        Assert.Contains(viewModel.DiscoverItems, item => item.ModId == "AdvancedFlightComputer");
    }

    [Fact]
    public async Task BackThenForward_ReturnsToTheSameModPage()
    {
        using var harness = await OpenModFromDiscoverAsync();
        var viewModel = harness.ViewModel;
        var mod = viewModel.SelectedContent;

        await OnMainWindowAsync(harness, async window =>
        {
            await PressAsync(window, MouseButton.XButton1, viewModel.GoBackCommand);
            await PressAsync(window, MouseButton.XButton2, viewModel.GoForwardCommand);
        });

        Assert.True(viewModel.CurrentWindowContent);
        Assert.Same(mod, viewModel.SelectedContent);
    }

    [Fact]
    public async Task OpenModal_IgnoresBothButtons()
    {
        using var harness = await OpenModFromDiscoverAsync();
        var viewModel = harness.ViewModel;

        var (stayedOnMod, stayedOnDiscover) = await OnMainWindowAsync(harness, async window =>
        {
            viewModel.SetMainWindowSettings();
            await PressAsync(window, MouseButton.XButton1, viewModel.GoBackCommand);
            var onMod = viewModel.CurrentWindowContent;

            viewModel.CloseSettingsCommand.Execute(null);
            await viewModel.GoBackCommand.ExecuteAsync(null);
            viewModel.SetMainWindowSettings();
            await PressAsync(window, MouseButton.XButton2, viewModel.GoForwardCommand);
            return (onMod, viewModel.CurrentWindowDiscover);
        });

        Assert.True(stayedOnMod);
        Assert.True(stayedOnDiscover);
    }

    [Fact]
    public async Task BackButton_OnAPageWithoutTheBackArrow_StaysThere()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        await OnMainWindowAsync(harness, window => PressAsync(window, MouseButton.XButton1, viewModel.GoBackCommand));

        Assert.True(viewModel.CurrentWindowHome);
    }

    private static async Task<ViewModelHarness> OpenModFromDiscoverAsync()
    {
        var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.SetMainWindowDiscover();
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.SearchText = "Flight";
        await viewModel.OpenContentCommand.ExecuteAsync(viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer"));
        Assert.True(viewModel.CurrentWindowContent);
        return harness;
    }

    private static Task OnMainWindowAsync(ViewModelHarness harness, Func<Window, Task> body) =>
        OnMainWindowAsync(harness, async window =>
        {
            await body(window);
            return true;
        });

    private static Task<T> OnMainWindowAsync<T>(ViewModelHarness harness, Func<Window, Task<T>> body) =>
        HeadlessApp.RunAsync(harness, async () =>
        {
            var window = new MainWindow { DataContext = harness.ViewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                return await body(window);
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>Presses a mouse button in the middle of the window and waits for what the command it may start does.</summary>
    private static Task PressAsync(Window window, MouseButton button, IAsyncRelayCommand command)
    {
        var center = new Point(window.Bounds.Width / 2, window.Bounds.Height / 2);
        window.MouseDown(center, button);
        window.MouseUp(center, button);
        return command.ExecutionTask ?? Task.CompletedTask;
    }
}
