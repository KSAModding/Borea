using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.History;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Planning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

public enum SettingsTab
{
    General,
    Game,
    About,
}

public enum GameSetupState
{
    Ready,
    NotSaved,
    FolderMissing,
}

/// <summary>
/// The Game section of the settings modal: where KSA is, and which mod
/// loader starts it. Saving rebuilds the services, because their paths are
/// fixed when the graph is built (see <see cref="BoreaServices"/>).
/// </summary>
public partial class MainViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralTab))]
    [NotifyPropertyChangedFor(nameof(IsGameTab))]
    [NotifyPropertyChangedFor(nameof(IsAboutTab))]
    private SettingsTab _settingsTab;

    public bool IsGeneralTab => SettingsTab == SettingsTab.General;

    public bool IsGameTab => SettingsTab == SettingsTab.Game;

    public bool IsAboutTab => SettingsTab == SettingsTab.About;

    [ObservableProperty]
    private string _gameDirectoryInput = string.Empty;

    /// <summary>
    /// The games Borea found while no usable game directory is saved.
    /// </summary>
    public ObservableCollection<DetectedGame> DetectedGames { get; } = [];

    [ObservableProperty]
    private DetectedGame? _selectedDetectedGame;

    public bool HasSeveralDetectedGames => DetectedGames.Count > 1;

    /// <summary>
    /// True while the field holds a found folder that is not saved yet.
    /// </summary>
    [ObservableProperty]
    private bool _isGameDirectorySuggested;

    [ObservableProperty]
    private bool _isLoaderDirectorySuggested;

    private IReadOnlyList<DetectedLoader> _detectedLoaders = [];

    /// <summary>
    /// Why the game is not usable yet, or null when it is. Drives the banner
    /// on Home, Discover and Library (#152).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsGameSetup))]
    private GameSetupState _gameSetupState;

    public bool NeedsGameSetup => GameSetupState != GameSetupState.Ready;

    public string? GameSetupBannerText => GameSetupState switch
    {
        GameSetupState.NotSaved => Localization.SetupBannerNotSaved,
        GameSetupState.FolderMissing => Localization.SetupBannerFolderMissing,
        _ => null,
    };

    private bool _promptedForGameSetup;

    /// <summary>
    /// Opens the settings on the Game tab, from the banner.
    /// </summary>
    [RelayCommand]
    private Task OpenGameSetupAsync()
    {
        IsSettingsOpen = true;
        return ShowGameSettingsAsync();
    }

    /// <summary>
    /// Reads the saved game directory after a load or a rebuild. The first
    /// load without a usable one looks for the game in the background; after
    /// that the banner is the reminder.
    /// </summary>
    private void RefreshGameSetup()
    {
        var directory = _services?.Settings.GameDirectoryPath;
        GameSetupState = _services is null || directory is not null && Directory.Exists(directory)
            ? GameSetupState.Ready
            : directory is null ? GameSetupState.NotSaved : GameSetupState.FolderMissing;
        OnPropertyChanged(nameof(GameSetupBannerText));

        if (_services is { } services && !_promptedForGameSetup)
        {
            _promptedForGameSetup = true;
            if (NeedsGameSetup)
                _gameDetection = OfferFoundGamesAsync(services);
        }
    }

    [ObservableProperty]
    private string _loaderDirectoryInput = string.Empty;

    public ObservableCollection<DiscoverItem> Loaders { get; } = [];

    [ObservableProperty]
    private DiscoverItem? _selectedLoader;

    [ObservableProperty]
    private string? _setupMessage;

    [ObservableProperty]
    private string? _setupError;

    [ObservableProperty]
    private bool _isSetupBusy;

    [ObservableProperty]
    private double _setupProgress;

    [ObservableProperty]
    private string? _setupProgressStatus;

    [ObservableProperty]
    private string? _setupProgressDetail;

    [ObservableProperty]
    private InstallRun? _loaderInstallRun;

    /// <summary>
    /// What the settings record for the selected loader, as the chip under the
    /// picker shows it. Null when the selected loader is not installed.
    /// </summary>
    [ObservableProperty]
    private string? _installedLoaderText;

    /// <summary>
    /// The label of the install button. It installs a loader Borea does not
    /// know, updates one to the newest release, or reinstalls one whose
    /// version is unknown.
    /// </summary>
    [ObservableProperty]
    private string _loaderInstallActionText = string.Empty;

    /// <summary>
    /// False when the recorded loader is already the newest release, because
    /// the button would then only download the same files again.
    /// </summary>
    [ObservableProperty]
    private bool _canInstallLoader = true;

    private LoaderInstallation? _selectedLoaderInstallation;

    private ModVersion? _selectedLoaderLatest;

    [RelayCommand]
    private void ShowGeneralSettings() => SettingsTab = SettingsTab.General;

    [RelayCommand]
    private async Task ShowGameSettingsAsync()
    {
        SettingsTab = SettingsTab.Game;
        _gameTabShown = true;
        SetupMessage = null;
        SetupError = null;
        if (_services is null)
            return;

        ClearDetectedGames();
        _detectedLoaders = [];
        GameDirectoryInput = _services.Settings.GameDirectoryPath ?? string.Empty;
        var loaderId = _services.Settings.LoaderInstallations.Keys.FirstOrDefault();
        LoaderDirectoryInput = loaderId is null ? string.Empty : _services.Settings.LoaderInstallations[loaderId].DirectoryPath;

        await EnsureDiscoverLoadedAsync();
        Loaders.Clear();
        foreach (var loader in _listings.Where(item => item.Type == ContentType.ModLoader))
            Loaders.Add(loader);
        SelectedLoader = Loaders.FirstOrDefault(loader => loaderId is not null && ModIds.Equals(loader.ModId, loaderId)) ?? Loaders.FirstOrDefault();
        await DetectInstallsAsync(_services);
        await RefreshLoaderStateAsync();
    }

    /// <summary>
    /// Fills the fields with what Borea found when nothing usable is saved.
    /// Only a confirmation saves a found folder.
    /// </summary>
    private async Task DetectInstallsAsync(BoreaServices services)
    {
        var savedGame = services.Settings.GameDirectoryPath;
        var needsGame = savedGame is null || !Directory.Exists(savedGame);
        var needsLoader = services.Settings.LoaderInstallations.Count == 0;
        if (needsGame || needsLoader)
        {
            var gameInput = GameDirectoryInput;
            var loaderInput = LoaderDirectoryInput;
            var detection = await FindInstallsAsync(services);
            if (!ReferenceEquals(services, _services))
                return;

            if (detection is not null && needsGame)
            {
                foreach (var game in detection.Games)
                    DetectedGames.Add(game);
                if (DetectedGames.Count == 1 && GameDirectoryInput == gameInput)
                    GameDirectoryInput = DetectedGames[0].Directory;
            }

            if (detection is not null && needsLoader)
            {
                _detectedLoaders = detection.Loaders;
                if (LoaderDirectoryInput == loaderInput && FoundLoaderDirectory(SelectedLoader) is { } found)
                    LoaderDirectoryInput = found;
            }
        }

        OnPropertyChanged(nameof(HasSeveralDetectedGames));
        RefreshSuggestions();
    }

    private void ClearDetectedGames()
    {
        DetectedGames.Clear();
        SelectedDetectedGame = null;
        OnPropertyChanged(nameof(HasSeveralDetectedGames));
    }

    private string? FoundLoaderDirectory(DiscoverItem? loader) =>
        loader is null ? null : _detectedLoaders.FirstOrDefault(found => ModIds.Equals(found.LoaderId, loader.ModId))?.Directory;

    partial void OnSelectedDetectedGameChanged(DetectedGame? value)
    {
        if (value is not null)
            GameDirectoryInput = value.Directory;
    }

    partial void OnGameDirectoryInputChanged(string value) => RefreshSuggestions();

    partial void OnLoaderDirectoryInputChanged(string value) => RefreshSuggestions();

    private void RefreshSuggestions()
    {
        var input = GameDirectoryInput.Trim();
        IsGameDirectorySuggested = input.Length > 0
            && !SameDirectory(_services?.Settings.GameDirectoryPath, input)
            && DetectedGames.Any(game => SameDirectory(game.Directory, input));

        var loaderInput = LoaderDirectoryInput.Trim();
        IsLoaderDirectorySuggested = loaderInput.Length > 0
            && SelectedLoader is { } selected
            && _services?.Settings.LoaderInstallations.Keys.Any(id => ModIds.Equals(id, selected.ModId)) != true
            && _detectedLoaders.Any(found => ModIds.Equals(found.LoaderId, selected.ModId) && SameDirectory(found.Directory, loaderInput));
    }

    private static bool SameDirectory(string? left, string right)
    {
        if (left is null)
            return false;

        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    partial void OnSelectedLoaderChanged(DiscoverItem? oldValue, DiscoverItem? newValue)
    {
        var input = LoaderDirectoryInput.Trim();
        if (input.Length == 0 || SameDirectory(FoundLoaderDirectory(oldValue), input))
            LoaderDirectoryInput = FoundLoaderDirectory(newValue) ?? string.Empty;

        RefreshSuggestions();
        _ = RefreshLoaderStateAsync();
    }

    /// <summary>
    /// Reads what the settings record for the selected loader and which release
    /// is the newest, then updates the chip and the install button.
    /// </summary>
    internal async Task RefreshLoaderStateAsync()
    {
        var loader = SelectedLoader;
        LoaderInstallation? installation = null;
        ModVersion? latest = null;
        if (_services is not null && loader is not null)
        {
            installation = _services.Settings.LoaderInstallations
                .FirstOrDefault(pair => ModIds.Equals(pair.Key, loader.ModId)).Value;
            try
            {
                latest = (await _services.Mods.GetLatestReleaseAsync(loader.ModId))?.Version;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
            {
                // without a known release there is nothing newer to offer
            }
        }

        // a selection that changed while the release was read refreshes itself
        if (!ReferenceEquals(loader, SelectedLoader))
            return;

        _selectedLoaderInstallation = installation;
        _selectedLoaderLatest = latest;
        RefreshLoaderText();
    }

    private void RefreshLoaderText()
    {
        var installation = _selectedLoaderInstallation;
        var latest = _selectedLoaderLatest;

        if (installation is null)
        {
            InstalledLoaderText = null;
            LoaderInstallActionText = Localization.SetupInstallLoader;
            CanInstallLoader = true;
        }
        else if (installation.Version is not { } installed)
        {
            InstalledLoaderText = Localization.SetupLoaderInstalledUnknownVersion;
            LoaderInstallActionText = Localization.SetupReinstallLoader;
            CanInstallLoader = latest is not null;
        }
        else
        {
            InstalledLoaderText = Localization.FormatSetupLoaderInstalledVersion(installed.ToString());
            LoaderInstallActionText = latest is { } newest && newest >= installed ? Localization.FormatSetupUpdateLoader(newest.ToString()) : Localization.SetupInstallLoader;
            CanInstallLoader = latest is { } candidate && candidate > installed;
        }
    }

    [RelayCommand]
    private Task SaveGameDirectoryAsync() => RunSetupAsync(services => ChangeGameDirectoryAsync(services, GameDirectoryInput.Trim()));

    /// <summary>
    /// Saves the game directory and writes it into the configuration of each
    /// recorded loader. An empty path clears it.
    /// </summary>
    private async Task<string> ChangeGameDirectoryAsync(BoreaServices services, string path)
    {
        var directory = path.Length == 0 ? null : Path.GetFullPath(path);
        if (directory is null)
            await services.SettingsRepository.SaveAsync((await ReadSavedSettingsAsync(services)).WithGameDirectory(null));
        else
            await services.GameDirectoryChanger.ChangeAsync(directory);
        return directory is not null && !Directory.Exists(directory) ? Localization.SetupDirectoryMissing : Localization.SetupSaved;
    }

    /// <summary>
    /// Points Borea at a loader that is already on disk, the way
    /// <c>borea settings set loader</c> does.
    /// </summary>
    [RelayCommand]
    private Task AdoptLoaderAsync() => RunSetupAsync(async services =>
    {
        if (SelectedLoader is null)
            return Localization.SetupNoLoaderSelected;

        var directory = Path.GetFullPath(LoaderDirectoryInput.Trim());
        var installation = new LoaderInstallation(directory, version: null, rawVersion: null, isAdopted: true);
        await services.SettingsRepository.SaveAsync((await ReadSavedSettingsAsync(services)).WithLoaderInstallation(SelectedLoader.ModId, installation));
        return Directory.Exists(directory) ? Localization.SetupSaved : Localization.SetupDirectoryMissing;
    });

    /// <summary>
    /// Downloads the loader's latest release and installs it. An empty
    /// directory lets the installer pick its default.
    /// </summary>
    [RelayCommand]
    private Task InstallLoaderAsync() => RunSetupAsync(services => SelectedLoader is null
        ? Task.FromResult(Localization.SetupNoLoaderSelected)
        : InstallNewestLoaderAsync(services, SelectedLoader.ModId, LoaderDirectoryInput.Trim()));

    private async Task<string> InstallNewestLoaderAsync(BoreaServices services, string loaderId, string directory)
    {
        var listing = await services.Mods.GetListingAsync(loaderId)
            ?? throw new InvalidOperationException(Localization.DiscoverNoRelease);
        var release = await services.Mods.GetLatestReleaseAsync(loaderId)
            ?? throw new InvalidOperationException(Localization.DiscoverNoRelease);
        var version = release.Version.ToString();
        var run = LoaderInstallRun = StartInstallRun(StartTask(TaskKind.LoaderInstall, listing.Name, version: version));
        run.TaskItem.NewVersion = version;
        var text = new InstallProgressText(Localization);
        InstallProgress? paused = null;
        void Show(InstallProgress value)
        {
            if (!IsSetupBusy)
                return;

            text.Report(value);
            paused = text.IsPaused ? value : null;
            SetupProgress = text.Percent;
            SetupProgressStatus = text.Status;
            SetupProgressDetail = text.Detail;
            run.Report(value.Phase);
            run.TaskItem.Report(text);
        }

        run.RepeatPausedReport = () => { if (paused is { } value) run.ShowReport(() => Show(value)); };
        var progress = new Progress<InstallProgress>(value => run.ShowReport(() => Show(value)));
        var completed = false;
        var stopped = false;
        string? error = null;
        try
        {
            var result = await run.InstallStop.RunAsync(
                (reports, token) => services.LoaderInstaller.InstallAsync(listing, release, directory.Length == 0 ? null : Path.GetFullPath(directory), reports, token),
                progress);
            completed = true;
            LoaderDirectoryInput = result.Directory;
            return Localization.FormatSetupLoaderInstalled(listing.Name, result.Version.ToString(), result.Directory);
        }
        catch (InstallStoppedException)
        {
            stopped = true;
            return Localization.InstallStopped;
        }
        catch (Exception exception) when (IsInstallFailure(exception) || exception is ArgumentException)
        {
            error = exception.Message;
            throw;
        }
        finally
        {
            EndInstallRun(run, completed, stopped, error);
            LoaderInstallRun = null;
        }
    }

    private async Task RunSetupAsync(Func<BoreaServices, Task<string>> operation)
    {
        if (_services is null || IsSetupBusy)
            return;

        if (IsChangingLibraryFolder)
        {
            SetupError = Localization.LibraryFolderBusy;
            return;
        }

        IsSetupBusy = true;
        SetupError = null;
        SetupMessage = null;
        try
        {
            SetupMessage = await operation(_services);
            await RebuildServicesAsync();
            if (_services.Settings.GameDirectoryPath is { } game && Directory.Exists(game))
                ClearDetectedGames();
            if (_services.Settings.LoaderInstallations.Count > 0)
                _detectedLoaders = [];

            await RefreshLoaderStateAsync();
            RefreshSuggestions();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or HttpRequestException or DownloadFailedException or TaskCanceledException or AggregateException)
        {
            SetupError = exception.Message;
        }
        finally
        {
            IsSetupBusy = false;
            SetupProgress = 0;
            SetupProgressStatus = null;
            SetupProgressDetail = null;
        }
    }

    /// <summary>
    /// Builds a fresh graph from the saved settings and reloads every page
    /// that shows something path-dependent.
    /// </summary>
    private async Task RebuildServicesAsync()
    {
        if (_services is null)
            return;

        var previous = _services;
        _services = await _rebuildServices();
        _instances = _services.Instances;
        AttachGitHubSession(previous, _services);
        previous.Dispose();
        OnPropertyChanged(nameof(SelectedReleaseChannel));
        await LoadAsync();
        await RefreshCompatibilityAsync();
    }
}
