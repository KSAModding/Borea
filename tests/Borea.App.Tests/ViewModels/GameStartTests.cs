using System.ComponentModel;
using Borea.App.ViewModels;
using Borea.Core.History;
using Borea.Core.Index;
using Borea.Core.Launch;
using Borea.Core.Preferences;

namespace Borea.App.Tests.ViewModels;

public sealed class GameStartTests
{
    private static readonly LaunchPlan Plan = new(Path.Combine(Path.GetTempPath(), "launcher"), [], Path.GetTempPath(), new Dictionary<string, string>());

    private static readonly LaunchResult Started = LaunchResult.Success(Plan, 4244, "Started.");

    /// <summary>The game runs for 15 seconds of the clock, and a minimized window comes back when it ends.</summary>
    [Theory]
    [InlineData(true, GameStartAction.Close, true, false, 1, 1, "")]
    [InlineData(false, GameStartAction.Close, false, false, 0, 1, "")]
    [InlineData(false, GameStartAction.Close, true, true, 1, 0, "Minimize Restore")]
    [InlineData(false, GameStartAction.Minimize, true, false, 0, 0, "Minimize Restore")]
    [InlineData(false, GameStartAction.KeepOpen, true, false, 0, 0, "")]
    public async Task Launch_Started_AppliesTheChoice(bool withLoader, GameStartAction action, bool reopen, bool watcherFails, int watcherStarts, int ends, string windowCalls)
    {
        using var harness = await CreateAsync(withLoader, new LaunchFailureTests.FixedLauncher(Started));
        var (viewModel, window, clock) = Arrange(harness, action, reopen);
        var gameEnds = TimeSpan.FromSeconds(15);
        harness.IsGameProcessRunning = () => clock.Elapsed < gameEnds;
        var (starts, endCount) = (0, 0);
        viewModel.StartReopenWatcher = _ =>
        {
            starts++;
            if (watcherFails)
                throw new Win32Exception(2);
        };
        viewModel.EndApp = () => endCount++;
        var idleIcon = new ListingImage(viewModel, new IconImage("https://images.example/1.png", new string('A', 64), 256, 256, 100)) { Bytes = new byte[100] };
        await viewModel.WhenPreferencesSavedAsync();

        await PlayAsync(harness, withLoader);
        await viewModel.WhenGameWindowWatchedAsync();
        await viewModel.WhenPreferencesSavedAsync();

        var saved = (await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames)).Preferences;
        Assert.Equal((action, reopen), (saved.WhenGameStarts, saved.ReopenAfterGame));
        Assert.Equal((watcherStarts, ends), (starts, endCount));
        Assert.Equal(windowCalls, string.Join(" ", window.Calls));
        Assert.Equal(windowCalls.Length == 0 ? TimeSpan.Zero : gameEnds, clock.Elapsed);
        Assert.Equal(windowCalls.Length == 0, idleIcon.IsLoaded);
        gameEnds = TimeSpan.Zero;
    }

    /// <summary>A game in Wine has no process name for the watcher, so Close minimizes, and a launch without an instance stays open.</summary>
    [Theory]
    [InlineData(true, "Minimize Restore")]
    [InlineData(false, "")]
    public async Task Launch_GameInWine_NeverClosesAndNeverRestoresEarly(bool withLoader, string windowCalls)
    {
        using var harness = await CreateAsync(withLoader, new LaunchFailureTests.FixedLauncher(Started));
        var (viewModel, window, clock) = Arrange(harness, GameStartAction.Close, reopen: true);
        harness.IsGameProcessRunning = () => clock.Elapsed < TimeSpan.FromSeconds(15);
        var starts = 0;
        viewModel.GameRunsThroughWineCheck = () => true;
        await viewModel.WhenPreferencesSavedAsync();
        viewModel.StartReopenWatcher = _ => starts++;
        viewModel.EndApp = () => throw new InvalidOperationException("Borea closed while the game runs in Wine.");

        await PlayAsync(harness, withLoader);
        await viewModel.WhenGameWindowWatchedAsync();

        Assert.Equal(0, starts);
        Assert.Equal(windowCalls, string.Join(" ", window.Calls));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Launch_FailsOrStopsEarly_StaysOpen(bool stopsEarly)
    {
        var launcher = stopsEarly
            ? new LaunchFailureTests.FixedLauncher(Started, LaunchResult.ExitedEarly(Plan, 3, ["Fatal error."], blamedModId: null, "English text."))
            : new LaunchFailureTests.FixedLauncher(LaunchResult.Failed(LaunchOutcome.OtherBuildRunning, "English text."));
        using var harness = await CreateAsync(withLoader: true, launcher);
        var (viewModel, window, _) = Arrange(harness, GameStartAction.Close, reopen: true);
        var starts = 0;
        viewModel.StartReopenWatcher = _ => starts++;
        viewModel.EndApp = () => throw new InvalidOperationException("Borea closed after a failed launch.");

        await PlayAsync(harness, withLoader: true);

        Assert.Equal(0, starts);
        Assert.Empty(window.Calls);
        Assert.Equal(stopsEarly, viewModel.IsLaunchFailureOpen);
        Assert.True(viewModel.HasLaunchFailed);
        Assert.DoesNotContain(harness.Localization.LaunchStaysOpenForWork, viewModel.LaunchMessage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Launch_WhileWorkRuns_StaysOpenAndSaysSo(bool modalOpen)
    {
        using var harness = await CreateAsync(withLoader: false, launcher: null);
        var (viewModel, window, _) = Arrange(harness, GameStartAction.Close, reopen: true);
        window.IsModalOpen = modalOpen;
        _ = modalOpen ? null : viewModel.Tasks.Start(TaskKind.ModInstall, "KSArmory", null, null, null, null, TaskState.Running);
        var starts = 0;
        viewModel.StartReopenWatcher = _ => starts++;
        viewModel.EndApp = () => throw new InvalidOperationException("Borea closed while work runs.");

        await PlayAsync(harness, withLoader: false);

        Assert.Equal(0, starts);
        Assert.Empty(window.Calls);
        Assert.EndsWith(harness.Localization.LaunchStaysOpenForWork, viewModel.LaunchMessage);
    }

    private static Task<ViewModelHarness> CreateAsync(bool withLoader, ILauncher? launcher) => withLoader
        ? LaunchFailureTests.CreateWithLauncherAsync(launcher!)
        : ViewModelHarness.CreateAsync(sharedProfileLauncher: new LaunchFailureTests.FixedSharedProfileLauncher(SharedProfileLaunchResult.Success(Plan, 4244, "Started.")));

    private static (MainViewModel ViewModel, FakeGameWindow Window, AdvancingClock Clock) Arrange(ViewModelHarness harness, GameStartAction action, bool reopen)
    {
        var viewModel = harness.ViewModel;
        var window = new FakeGameWindow();
        var clock = new AdvancingClock();
        viewModel.GameWindow = window;
        viewModel.Clock = clock;
        viewModel.SelectedGameStart = viewModel.GameStartOptions.Single(option => option.Action == action);
        viewModel.ReopenAfterGame = reopen;
        return (viewModel, window, clock);
    }

    private static async Task PlayAsync(ViewModelHarness harness, bool withLoader)
    {
        if (withLoader)
        {
            await LaunchFailureTests.PlayActiveInstanceAsync(harness);
            return;
        }

        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.PlayWithoutModLoaderCommand.ExecuteAsync(null);
    }

    private sealed class FakeGameWindow : IGameWindow
    {
        public List<string> Calls { get; } = [];

        public bool IsModalOpen { get; set; }

        public bool IsMinimized { get; private set; }

        public void Minimize()
        {
            Calls.Add(nameof(Minimize));
            IsMinimized = true;
        }

        public void Restore()
        {
            Calls.Add(nameof(Restore));
            IsMinimized = false;
        }
    }
}
