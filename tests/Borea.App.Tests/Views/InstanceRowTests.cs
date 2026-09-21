using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views.Pages;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Storage.Launch;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class InstanceRowTests
{
    /// <summary>
    /// Renders the Library and hands the window to <paramref name="read"/>, which
    /// runs on the headless thread and awaits the command that its click starts.
    /// Both the click and its command belong to the Avalonia dispatcher of this
    /// dispatch, so they finish here, where that dispatcher still runs, and nothing
    /// of them is left for the caller.
    /// </summary>
    private static Task<T> OnLibraryAsync<T>(ViewModelHarness harness, Func<Window, Control, Task<T>> read) =>
        HeadlessApp.RunAsync(harness, async () =>
        {
            var page = new LibraryPage();
            var window = new Window { Width = 1200, Height = 600, DataContext = harness.ViewModel, Content = page };
            window.Show();
            try
            {
                page.UpdateLayout();
                return await read(window, page);
            }
            finally
            {
                window.Close();
            }
        });

    /// <inheritdoc cref="OnLibraryAsync{T}(ViewModelHarness, Func{Window, Control, Task{T}})"/>
    private static Task<T> OnLibraryAsync<T>(ViewModelHarness harness, Func<Window, Control, T> read) =>
        OnLibraryAsync(harness, (window, page) => Task.FromResult(read(window, page)));

    /// <inheritdoc cref="OnLibraryAsync{T}(ViewModelHarness, Func{Window, Control, Task{T}})"/>
    private static Task OnLibraryAsync(ViewModelHarness harness, Func<Window, Control, Task> read) =>
        OnLibraryAsync(harness, async (window, page) =>
        {
            await read(window, page);
            return true;
        });

    private static void Click(Window window, Visual target, Point inTarget)
    {
        var point = target.TranslatePoint(inTarget, window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    private static Button Card(Control page)
        => page.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("card-button"));

    private static async Task<ViewModelHarness> CreateAsync()
    {
        var harness = await ViewModelHarness.CreateAsync(processStarter: new NoProcessStarter());
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.Instances.Single().ActivateCommand.ExecuteAsync(null);
        harness.ViewModel.SetMainWindowLibraryCommand.Execute(null);
        return harness;
    }

    [Fact]
    public async Task ClickingTheCardBesideTheNameOpensTheInstance()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;

        await OnLibraryAsync(harness, async (window, page) =>
        {
            var card = Card(page);
            Click(window, card, new Point(150, card.Bounds.Height / 2));
            var opening = viewModel.ActiveInstance!.OpenCommand.ExecutionTask;
            Assert.NotNull(opening);
            await opening;
        });

        Assert.Equal(viewModel.ActiveInstance!.InstanceId, viewModel.SelectedInstance?.InstanceId);
    }

    [Fact]
    public async Task ClickingPlayRunsTheLaunchAndLeavesTheInstanceClosed()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;

        var item = viewModel.ActiveInstance!;
        await OnLibraryAsync(harness, async (window, page) =>
        {
            var play = Card(page).GetVisualDescendants().OfType<Button>().Single(button => button.Command == item.PlayCommand);
            Click(window, play, new Point(play.Bounds.Width / 2, play.Bounds.Height / 2));
            var launching = item.PlayCommand.ExecutionTask;
            Assert.NotNull(launching);
            await launching;
        });

        // no loader takes this instance, so a launch that ran offers to install one
        Assert.True(viewModel.IsLoaderPromptOpen);
        Assert.Null(item.OpenCommand.ExecutionTask);
        Assert.Null(viewModel.SelectedInstance);
    }

    [Fact]
    public async Task ClickingTheRowMenuOpensItAndLeavesTheInstanceClosed()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;

        var (flyoutOpen, opened, selected) = await OnLibraryAsync(harness, (window, page) =>
        {
            var menu = Card(page).GetVisualDescendants().OfType<Button>().Single(button => button.Flyout is not null);
            Click(window, menu, new Point(menu.Bounds.Width / 2, menu.Bounds.Height / 2));
            return (menu.Flyout!.IsOpen, viewModel.ActiveInstance!.OpenCommand.ExecutionTask, viewModel.SelectedInstance);
        });

        Assert.True(flyoutOpen);
        Assert.Null(opened);
        Assert.Null(selected);
    }

    [Fact]
    public async Task ClickingTheActivateSwitchLeavesTheInstanceClosed()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;

        var row = viewModel.Instances.Single();

        await OnLibraryAsync(harness, async (window, page) =>
        {
            var toggle = Card(page).GetVisualDescendants().OfType<ToggleSwitch>().Single();
            Click(window, toggle, new Point(toggle.Bounds.Width / 2, toggle.Bounds.Height / 2));
            var toggling = row.ToggleActiveCommand.ExecutionTask;
            Assert.NotNull(toggling);
            await toggling;
        });

        // the switch turned the instance off, which is the proof that the click reached it
        Assert.Null(viewModel.ActiveInstance);
        Assert.Null(row.OpenCommand.ExecutionTask);
        Assert.Null(viewModel.SelectedInstance);
    }

    [Fact]
    public async Task ClickingPlayWhileItIsOffLeavesTheInstanceClosed()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;

        var (opened, selected) = await OnLibraryAsync(harness, (window, page) =>
        {
            var item = viewModel.ActiveInstance!;
            var play = Card(page).GetVisualDescendants().OfType<Button>().Single(button => button.Command == item.PlayCommand);
            play.IsEnabled = false;
            page.UpdateLayout();

            Click(window, play, new Point(play.Bounds.Width / 2, play.Bounds.Height / 2));
            return (item.OpenCommand.ExecutionTask, viewModel.SelectedInstance);
        });

        Assert.Null(opened);
        Assert.Null(selected);
    }

    [Fact]
    public async Task ClickingTheDeleteConfirmationLeavesTheInstanceClosed()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.ActiveInstance!.BeginDeleteCommand.Execute(null);

        var (opened, selected) = await OnLibraryAsync(harness, (window, page) =>
        {
            var cancel = Card(page).GetVisualDescendants().OfType<Button>()
                .Single(button => button.Content as string == harness.Localization.LibraryCancel);
            Click(window, cancel, new Point(cancel.Bounds.Width / 2, cancel.Bounds.Height / 2));
            return (viewModel.ActiveInstance!.OpenCommand.ExecutionTask, viewModel.SelectedInstance);
        });

        Assert.Null(opened);
        Assert.Null(selected);
    }

    /// <summary>No test of the row starts the game, so a start is a failure and not a real process.</summary>
    private sealed class NoProcessStarter : IProcessStarter
    {
        public IStartedProcess Start(LaunchPlan plan) => throw new InvalidOperationException("The row tests start no process.");
    }
}
