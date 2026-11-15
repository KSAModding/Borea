using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Borea.App.GameWatch;
using Borea.App.Localization;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.Preferences;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Borea.App.ViewModels;

/// <summary>
/// What the App does with its window after a launch from the App started the game. It keeps the window open,
/// minimizes it until the game ends, or closes and lets the reopen watcher open it again. Work that runs keeps the
/// window open, because a closed or hidden App would stop it or keep a waiting answer from the player.
/// </summary>
public partial class MainViewModel
{
    private GameStartAction? _whenGameStarts;

    private bool? _reopenAfterGame;

    private IReadOnlyList<GameStartOption>? _gameStartOptions;

    private Task _gameWindowWatch = Task.CompletedTask;

    internal IGameWindow? GameWindow { get; set; }

    /// <summary>
    /// Starts the watcher that opens Borea again after the game, with the process names of the installed loaders.
    /// Null where no program can run the watcher, such as a build that runs through the dotnet host.
    /// </summary>
    internal Action<IReadOnlyList<string>>? StartReopenWatcher { get; set; }

    /// <summary>Whether the game folder holds the Windows build on a Linux or macOS host, which runs through Wine.</summary>
    internal Func<bool>? GameRunsThroughWineCheck { get; set; }

    private bool GameRunsThroughWine()
        => GameRunsThroughWineCheck?.Invoke()
            ?? (!OperatingSystem.IsWindows() && _services?.GamePlatform.Current == OsPlatform.Windows);

    public IReadOnlyList<GameStartOption> GameStartOptions
        => _gameStartOptions ??= Enum.GetValues<GameStartAction>().Select(action => new GameStartOption(Localization, action)).ToArray();

    public GameStartAction WhenGameStarts => _whenGameStarts ?? _appPreferences.WhenGameStarts;

    public GameStartOption SelectedGameStart
    {
        get => GameStartOptions.First(option => option.Action == WhenGameStarts);
        set
        {
            if (value is null || value.Action == WhenGameStarts)
                return;

            _whenGameStarts = value.Action;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WhenGameStarts));
            OnPropertyChanged(nameof(IsCloseOnGameStart));
            QueuePreferenceSave(preferences => preferences.WithWhenGameStarts(value.Action));
        }
    }

    public bool IsCloseOnGameStart => WhenGameStarts == GameStartAction.Close;

    public bool ReopenAfterGame
    {
        get => _reopenAfterGame ?? _appPreferences.ReopenAfterGame;
        set
        {
            if (value == ReopenAfterGame)
                return;

            _reopenAfterGame = value;
            OnPropertyChanged();
            QueuePreferenceSave(preferences => preferences.WithReopenAfterGame(value));
        }
    }

    internal Task WhenGameWindowWatchedAsync() => _gameWindowWatch;

    /// <summary>
    /// Applies the choice after a launch that started the game. A failed launch never comes here, so its line and
    /// modal stay as they are. When Borea cannot start the watcher that opens it again, it minimizes instead of closing.
    /// </summary>
    private async Task AfterGameStartedAsync(Guid? instanceId)
    {
        var action = WhenGameStarts;
        if (action == GameStartAction.KeepOpen || _services is not { } services || GameWindow is not { } window)
            return;

        // the process ends at once after the close, so the saves finish before the check that nothing else runs
        if (action == GameStartAction.Close)
            await Task.WhenAll(WhenPreferencesSavedAsync(), Tasks.WhenSavedAsync());

        if (IsWorkRunning(window))
        {
            services.Log.Write("Borea stays open after the game started, because work runs.");
            ShowStaysOpenNote();
            return;
        }

        // a game in Wine has no process name that the watcher knows, so only the App can watch it, and only for an instance
        var wine = GameRunsThroughWine();
        if (action == GameStartAction.Close && (!ReopenAfterGame || (!wine && TryStartReopenWatcher(services))))
        {
            services.Log.Write(ReopenAfterGame
                ? "Borea closed while the game runs, and opens again when the game closes."
                : "Borea closed while the game runs.");
            EndApp?.Invoke();
            return;
        }

        if (wine && instanceId is null)
        {
            services.Log.Write("Borea stays open while the game runs, because it cannot watch a game in Wine that no instance started.");
            return;
        }

        MinimizeWhileGameRuns(services, window, instanceId);
    }

    /// <summary>Image and preview fetches do not count, because they start again when a view shows them.</summary>
    private bool IsWorkRunning(IGameWindow window)
        => Tasks.IsBusy
            || HasRunningInstalls
            || IsChangingLibraryFolder
            || IsSetupBusy
            || IsSelfUpdating
            || Volatile.Read(ref _libraryUses) > 0
            || _listingEditor is { IsPublishing: true } or { IsSharing: true }
            || window.IsModalOpen;

    /// <summary>The launch line shows the note, and a toast shows it on a page without that line, as for a refused launch.</summary>
    private void ShowStaysOpenNote()
    {
        LaunchMessage = string.IsNullOrEmpty(LaunchMessage)
            ? Localization.LaunchStaysOpenForWork
            : LaunchMessage + " " + Localization.LaunchStaysOpenForWork;

        if (!CurrentWindowHome && !CurrentWindowLibrary && !CurrentWindowInstance)
            Toasts.ShowMessage(ToastKind.Success, () => Localization.LaunchStaysOpenForWork);
    }

    private bool TryStartReopenWatcher(BoreaServices services)
    {
        if (StartReopenWatcher is not { } start)
        {
            services.Log.Write("Borea minimizes instead of closing, because this build cannot start the watcher that opens it again after the game.");
            return false;
        }

        try
        {
            start(services.LoaderProcessNames());
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            services.Log.Write("Borea minimizes instead of closing, because the watcher that opens it again after the game did not start.", exception);
            return false;
        }
    }

    private void MinimizeWhileGameRuns(BoreaServices services, IGameWindow window, Guid? instanceId)
    {
        IdleIcons.Clear();
        window.Minimize();
        services.Log.Write("Borea minimized while the game runs.");
        if (_gameWindowWatch.IsCompleted)
            _gameWindowWatch = RestoreAfterGameAsync(services, window, instanceId);
    }

    /// <summary>A window that the player restored before the game ended is not brought to the front again.</summary>
    private async Task RestoreAfterGameAsync(BoreaServices services, IGameWindow window, Guid? instanceId)
    {
        try
        {
            await new GameRunWatch(
                    () => _services is { } current && (current.IsGameProcessRunning() || (instanceId is { } id && current.Launcher.IsRunning(id))),
                    Clock)
                .WaitForEndAsync();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            services.Log.Write("Borea could not watch the game, so it restores its window now.", exception);
        }

        if (window.IsMinimized)
            window.Restore();
    }
}

/// <summary>One choice of what Borea does when the game starts, as the General settings name it.</summary>
public sealed class GameStartOption : ObservableObject
{
    private readonly LocalizationService _localization;

    public GameStartAction Action { get; }

    public string Text => Action switch
    {
        GameStartAction.Minimize => _localization.GameStartMinimize,
        GameStartAction.Close => _localization.GameStartClose,
        _ => _localization.GameStartKeepOpen,
    };

    public GameStartOption(LocalizationService localization, GameStartAction action)
    {
        _localization = localization;
        Action = action;
    }

    internal void RefreshText() => OnPropertyChanged(nameof(Text));
}
