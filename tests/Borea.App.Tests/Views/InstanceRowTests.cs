using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views.Pages;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Storage.Launch;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class InstanceRowTests
{
    private static async Task<T> OnLibraryAsync<T>(MainViewModel viewModel, Func<Window, Control, Task<T>> read)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await HeadlessApp.Session.Dispatch(async () =>
        {
            var page = new LibraryPage();
            var window = new Window { Width = 1200, Height = 600, DataContext = viewModel, Content = page };
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
        }, timeout.Token);
    }

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

        var opened = await OnLibraryAsync(viewModel, async (window, page) =>
        {
            var card = Card(page);
            Click(window, card, new Point(150, card.Bounds.Height / 2));
            var running = viewModel.ActiveInstance!.OpenCommand.ExecutionTask;
            if (running is not null)
                await running;
            return (running, viewModel.SelectedInstance?.InstanceId);
        });

        Assert.NotNull(opened.Item1);
        Assert.Equal(viewModel.ActiveInstance!.InstanceId, opened.Item2);
    }

    [Fact]
    public async Task ClickingPlayRunsTheLaunchAndLeavesTheInstanceClosed()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;

        var (prompt, opened, selected) = await OnLibraryAsync(viewModel, async (window, page) =>
        {
            var item = viewModel.ActiveInstance!;
            var play = Card(page).GetVisualDescendants().OfType<Button>().Single(button => button.Command == item.PlayCommand);
            Click(window, play, new Point(play.Bounds.Width / 2, play.Bounds.Height / 2));
            var running = item.PlayCommand.ExecutionTask;
            if (running is not null)
                await running;
            return (viewModel.IsLoaderPromptOpen, item.OpenCommand.ExecutionTask, viewModel.SelectedInstance);
        });

        // no loader takes this instance, so a launch that ran offers to install one
        Assert.True(prompt);
        Assert.Null(opened);
        Assert.Null(selected);
    }

    [Fact]
    public async Task ClickingTheRowMenuOpensItAndLeavesTheInstanceClosed()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;

        var (flyoutOpen, opened, selected) = await OnLibraryAsync(viewModel, (window, page) =>
        {
            var menu = Card(page).GetVisualDescendants().OfType<Button>().Single(button => button.Flyout is not null);
            Click(window, menu, new Point(menu.Bounds.Width / 2, menu.Bounds.Height / 2));
            return Task.FromResult((menu.Flyout!.IsOpen, viewModel.ActiveInstance!.OpenCommand.ExecutionTask, viewModel.SelectedInstance));
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

        var (opened, selected) = await OnLibraryAsync(viewModel, async (window, page) =>
        {
            var toggle = Card(page).GetVisualDescendants().OfType<ToggleSwitch>().Single();
            Click(window, toggle, new Point(toggle.Bounds.Width / 2, toggle.Bounds.Height / 2));
            var toggling = row.ToggleActiveCommand.ExecutionTask;
            if (toggling is not null)
                await toggling;
            return (row.OpenCommand.ExecutionTask, viewModel.SelectedInstance);
        });

        // the switch turned the instance off, which is the proof that the click reached it
        Assert.Null(viewModel.ActiveInstance);
        Assert.Null(opened);
        Assert.Null(selected);
    }

    [Fact]
    public async Task ClickingTheDeleteConfirmationLeavesTheInstanceClosed()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.ActiveInstance!.BeginDeleteCommand.Execute(null);

        var (opened, selected) = await OnLibraryAsync(viewModel, (window, page) =>
        {
            var cancel = Card(page).GetVisualDescendants().OfType<Button>()
                .Single(button => button.Content as string == harness.Localization.LibraryCancel);
            Click(window, cancel, new Point(cancel.Bounds.Width / 2, cancel.Bounds.Height / 2));
            return Task.FromResult((viewModel.ActiveInstance!.OpenCommand.ExecutionTask, viewModel.SelectedInstance));
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
