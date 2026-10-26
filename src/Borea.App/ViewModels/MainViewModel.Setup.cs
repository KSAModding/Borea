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
using Borea.Storage.ModLoaders;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

public enum SettingsTab
{
    General,
    Game,
    About,
    Help,
}

public enum GameSetupState
{
    Ready,
    NotSaved,
    FolderMissing,
}

/// <summary>
/// The Game section of the settings modal: where KSA is, and the installed
/// versions of each mod loader. Saving rebuilds the services, because their paths are
/// fixed when the graph is built (see <see cref="BoreaServices"/>).
/// </summary>
public partial class MainViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralTab))]
    [NotifyPropertyChangedFor(nameof(IsGameTab))]
    [NotifyPropertyChangedFor(nameof(IsAboutTab))]
    [NotifyPropertyChangedFor(nameof(IsHelpTab))]
    private SettingsTab _settingsTab;

    public bool IsGeneralTab => SettingsTab == SettingsTab.General;

    public bool IsGameTab => SettingsTab == SettingsTab.Game;

    public bool IsAboutTab => SettingsTab == SettingsTab.About;

    public bool IsHelpTab => SettingsTab == SettingsTab.Help;

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
    /// on Discover and Library (#152) and the Current Install header of Home.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsGameSetup), nameof(ShowLoaderPromptAction))]
    private GameSetupState _gameSetupState;

    public bool NeedsGameSetup => GameSetupState != GameSetupState.Ready;

    public string? GameSetupBannerText => GameSetupState switch
    {
        GameSetupState.NotSaved => Localization.SetupBannerNotSaved,
        GameSetupState.FolderMissing => Localization.SetupBannerFolderMissing,
        _ => null,
    };

    /// <summary>The short form of <see cref="GameSetupBannerText"/> for the Current Install header of Home.</summary>
    public string? HomeSetupText => GameSetupState switch
    {
        GameSetupState.NotSaved => Localization.HomeSetupNotSaved,
        GameSetupState.FolderMissing => Localization.HomeSetupFolderMissing,
        _ => null,
    };

    private bool _promptedForGameSetup;

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
        OnPropertyChanged(nameof(HomeSetupText));

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

    /// <summary>The installed loader versions, one row each, with the instances that start them.</summary>
    public ObservableCollection<LoaderVersionItem> LoaderVersionItems { get; } = [];

    public bool HasNoLoaderVersions => LoaderVersionItems.Count == 0;

    /// <summary>The loader is a choice only once the index lists more than one.</summary>
    public bool ShowLoaderChoice => Loaders.Count > 1;

    /// <summary>The releases of the selected loader that the channel offers and that are not installed, newest first.</summary>
    [ObservableProperty]
    private IReadOnlyList<LoaderReleaseChoice> _loaderInstallReleases = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallLoader))]
    private LoaderReleaseChoice? _selectedLoaderInstallRelease;

    public bool CanInstallLoader => SelectedLoaderInstallRelease is { CanInstall: true };

    /// <summary>The versions the last refresh listed, for the diagnostics. Null before the first refresh.</summary>
    private IReadOnlyList<InstalledLoaderVersion>? _listedLoaderVersions;

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

        // the field only points Borea at a folder of the player, so it shows the one that is used and never a folder of Borea
        LoaderDirectoryInput = loaderId is not null
            && _services.Settings.LoaderInstallations[loaderId] is { } recorded
            && LoaderLayoutMigration.IsPlayerFolder(_services.Paths.GetLoadersRoot(), recorded.DirectoryPath, recorded.IsAdopted)
            ? recorded.DirectoryPath
            : string.Empty;

        await EnsureDiscoverLoadedAsync();
        Loaders.Clear();
        foreach (var loader in _listings.Where(item => item.Type == ContentType.ModLoader))
            Loaders.Add(loader);
        OnPropertyChanged(nameof(ShowLoaderChoice));
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
        var needsLoader = (await services.LoaderVersions.ListAsync()).Count == 0;
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
            && !LoaderVersionItems.Any(item => ModIds.Equals(item.LoaderId, selected.ModId))
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
        _ = RefreshLoaderReleasesAsync();
    }

    /// <summary>
    /// Lists the installed versions with the instances that start them, and the releases of the selected loader that
    /// Borea can install beside them.
    /// </summary>
    internal async Task RefreshLoaderStateAsync()
    {
        if (_services is not { } services)
            return;

        var versions = await ReadLoaderVersionsAsync(services);
        if (!ReferenceEquals(services, _services))
            return;

        _listedLoaderVersions = versions.Select(item => item.Version).ToList();
        LoaderVersionItems.Clear();
        foreach (var item in versions)
            LoaderVersionItems.Add(item);
        OnPropertyChanged(nameof(HasNoLoaderVersions));
        RefreshSuggestions();
        await RefreshLoaderReleasesAsync();
    }

    /// <summary>
    /// The releases of the selected loader that the channel offers and that are not installed. The release that the
    /// update check offers for the recorded version is chosen first, so the choice never moves a player to an older one.
    /// </summary>
    private async Task RefreshLoaderReleasesAsync()
    {
        if (_services is not { } services)
            return;

        var loader = SelectedLoader;
        LoaderUpdate? update = null;
        IReadOnlyList<ModVersionMetadata> history = [];
        if (loader is not null)
        {
            var installation = services.Settings.LoaderInstallations.FirstOrDefault(pair => ModIds.Equals(pair.Key, loader.ModId)).Value;
            try
            {
                update = await FindLoaderUpdateAsync(services, loader.ModId, installation);
                history = await services.Mods.GetReleaseHistoryAsync(loader.ModId);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
            {
                // without a known release there is nothing to install
            }
        }

        // a refresh that started later shows the newer state
        if (!ReferenceEquals(loader, SelectedLoader) || !ReferenceEquals(services, _services))
            return;

        var game = services.InstalledVersion.GetInstalledVersion()?.Version;
        var choices = history
            .Where(release => release.IsOffered
                && services.Settings.ReleaseChannel.Includes(release.ReleaseStatus)
                && !LoaderVersionItems.Any(item => ModIds.Equals(item.LoaderId, release.ModId) && item.Version.Version == release.Version))
            .DistinctBy(release => release.Version)
            .OrderByDescending(release => release.Version)
            .Select(release => new LoaderReleaseChoice(this, release.Version, release, isInstalled: false, Compatibility.Evaluate(release, game), [], _ => string.Empty))
            .ToList();
        LoaderInstallReleases = choices;
        SelectedLoaderInstallRelease = choices.FirstOrDefault(choice => choice.Version == update?.Offer?.Version);
    }

    /// <summary>
    /// The installed versions with their rating for the game in the settings and the instances that start them. The
    /// instances are resolved with the inputs of the launch, so a version that a launch starts is never shown as unused.
    /// When the instances cannot be resolved, no version can be removed.
    /// </summary>
    private async Task<IReadOnlyList<LoaderVersionItem>> ReadLoaderVersionsAsync(BoreaServices services)
    {
        var installed = await services.LoaderVersions.ListAsync();
        var releases = await HeldLoaderReleasesAsync(services);
        IReadOnlyList<LoaderVersionUse>? uses = null;
        try
        {
            uses = await LoaderVersionUsesAsync(services, installed);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            services.Log.Write("Could not tell which instances start each mod loader version.", exception);
        }

        var game = services.InstalledVersion.GetInstalledVersion()?.Version;
        var items = new List<LoaderVersionItem>();
        foreach (var version in installed)
        {
            var compatibility = GameCompatibility.Unknown;
            if (releases is not null && version.Version is { } known)
            {
                try
                {
                    if (await releases.GetReleaseAsync(version.LoaderId, known) is { } release)
                        compatibility = Compatibility.Evaluate(release, game);
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException)
                {
                    // the version stays unrated, as when Borea is offline
                }
            }

            var name = Loaders.FirstOrDefault(loader => ModIds.Equals(loader.ModId, version.LoaderId))?.Name ?? version.LoaderId;
            var users = uses?.First(use => ReferenceEquals(use.Version, version)).Users.Select(user => user.Instance.Name).ToList();
            items.Add(new LoaderVersionItem(this, version, name, compatibility, users));
        }

        return items;
    }

    private static async Task<IReadOnlyList<LoaderVersionUse>> LoaderVersionUsesAsync(BoreaServices services, IReadOnlyList<InstalledLoaderVersion> installed) =>
        await LoaderVersionUse.UsedByAsync(
            await services.Instances.GetAllAsync(),
            installed,
            services.OfflineContentIndex,
            services.ContentIndex,
            await HeldLoaderReleasesAsync(services),
            services.Settings.ReleaseChannel,
            instance => services.InstanceGames.Resolve(instance).Version?.Version);

    private void RefreshLoaderText()
    {
        foreach (var item in LoaderVersionItems)
            item.RefreshText();
        foreach (var choice in LoaderInstallReleases)
            choice.RefreshText();
    }

    /// <summary>
    /// Removes one installed version, or forgets a folder of the player. Which instances start it is read again here,
    /// because an instance may have changed since the list was read.
    /// </summary>
    internal async Task RemoveLoaderVersionAsync(LoaderVersionItem item)
    {
        if (_services is not { } services || IsSetupBusy)
            return;

        item.IsConfirmingRemove = false;
        if (IsGameRunning(services))
        {
            SetupMessage = null;
            SetupError = Localization.LoaderUpdateGameRunning;
            return;
        }

        await RunSetupAsync(async current =>
        {
            var installed = await current.LoaderVersions.ListAsync();
            if (installed.FirstOrDefault(v => ModIds.Equals(v.LoaderId, item.LoaderId) && SameDirectory(v.DirectoryPath, item.Version.DirectoryPath)) is not { } version)
                return Localization.FormatSettingsLoaderRemoved(item.Name, item.VersionText);

            var users = (await LoaderVersionUsesAsync(current, installed)).First(use => ReferenceEquals(use.Version, version)).Users;
            if (users.Count > 0)
                throw new InvalidOperationException(Localization.FormatSettingsLoaderInUse(string.Join(", ", users.Select(user => user.Instance.Name))));

            var result = version.Version is { } known
                ? await current.LoaderUninstaller.UninstallVersionAsync(version.LoaderId, known)
                : await current.LoaderUninstaller.UninstallAsync(version.LoaderId);
            if (result.Failure == LoaderUninstallFailure.GameRunning)
                throw new InvalidOperationException(Localization.LoaderUpdateGameRunning);

            current.Log.Write($"Settings > Game removed {version.LoaderId} {version.Version?.ToString() ?? "of an unknown version"} at '{version.DirectoryPath}'.");
            return version.IsAdopted ? Localization.SetupSaved : Localization.FormatSettingsLoaderRemoved(item.Name, item.VersionText);
        });
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

        // the release its files match, so that Borea can rate it and offer an update
        var inspected = await InspectLoaderAsync(services, SelectedLoader.ModId, directory);
        // The Loaders root is Borea's own data, so a loader below it is Borea-owned even when the player names it here.
        var isAdopted = !LoaderLayoutMigration.IsBelowLoadersRoot(services.Paths.GetLoadersRoot(), directory);
        var installation = new LoaderInstallation(directory, inspected?.Version, inspected?.RawVersion, isAdopted);
        await services.SettingsRepository.SaveAsync((await ReadSavedSettingsAsync(services)).WithLoaderInstallation(SelectedLoader.ModId, installation));
        return Directory.Exists(directory) ? Localization.SetupSaved : Localization.SetupDirectoryMissing;
    });

    /// <summary>
    /// Downloads the chosen release and installs it beside the installed versions, in the folder that Borea chooses.
    /// </summary>
    [RelayCommand]
    private Task InstallLoaderAsync() => RunSetupAsync(async services =>
    {
        if (SelectedLoader is not { } loader)
            return Localization.SetupNoLoaderSelected;

        // the choices of an earlier loader pick never install for this one
        if (SelectedLoaderInstallRelease?.Release is not { } release || !ModIds.Equals(release.ModId, loader.ModId))
            throw new InvalidOperationException(Localization.DiscoverNoRelease);
        return await InstallLoaderReleaseAsync(services, loader.ModId, release);
    });

    private async Task<string> InstallLoaderReleaseAsync(BoreaServices services, string loaderId, ModVersionMetadata release)
    {
        // A new version goes into a new folder. The same version is replaced in place and the flat folder of an older Borea
        // is moved first, and both fail halfway while their files are in use.
        if (IsGameRunning(services))
        {
            var flatFolder = Path.Combine(services.Paths.GetLoadersRoot(), loaderId);
            if (InstalledLoaderVersion.Of(await services.LoaderVersions.ListAsync(), loaderId)
                .Any(installed => installed.Version == release.Version || SameDirectory(installed.DirectoryPath, flatFolder)))
            {
                throw new InvalidOperationException(Localization.LoaderUpdateGameRunning);
            }
        }

        var listing = await services.Mods.GetListingAsync(loaderId)
            ?? throw new InvalidOperationException(Localization.DiscoverNoRelease);

        // the record is what an older Borea starts with the game in the settings, so it is rated with that game
        var replacesUnfitRecord = await LoaderUpdate.ReplacesUnfitRecordAsync(
            services.Mods,
            services.Settings.LoaderInstallations.GetValueOrDefault(loaderId),
            release,
            services.InstalledVersion.GetInstalledVersion()?.Version);
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
                (reports, token) => services.LoaderInstaller.InstallAsync(listing, release, directory: null, replacesUnfitRecord, reports, token),
                progress);
            completed = true;
            ForgetLoaderUpdate(loaderId);
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

            await RefreshLoaderStateAsync();
            if (LoaderVersionItems.Count > 0)
                _detectedLoaders = [];
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
        OnPropertyChanged(nameof(UseSharedModStore));
        await LoadAsync();
        await RefreshCompatibilityAsync();
    }
}

/// <summary>One installed loader version in Settings > Game, with the instances that start it.</summary>
public sealed partial class LoaderVersionItem : ObservableObject
{
    /// <summary>How many instance names the row shows before it cuts the list.</summary>
    internal const int UsersShown = 3;

    private readonly MainViewModel _owner;

    /// <param name="users">The names of the instances that start this version, or null when Borea could not tell.</param>
    public LoaderVersionItem(MainViewModel owner, InstalledLoaderVersion version, string name, GameCompatibility compatibility, IReadOnlyList<string>? users)
    {
        _owner = owner;
        Version = version;
        Name = name;
        Compatibility = compatibility;
        Users = users;
    }

    public InstalledLoaderVersion Version { get; }

    public string LoaderId => Version.LoaderId;

    public string Name { get; }

    public GameCompatibility Compatibility { get; }

    public IReadOnlyList<string>? Users { get; }

    /// <summary>A folder of the player, which Borea only forgets and never deletes.</summary>
    public bool IsAdopted => Version.IsAdopted;

    public string VersionText => Version.Version?.ToString() ?? Version.RawVersion ?? string.Empty;

    /// <summary>"StarMap 0.4.8", or "StarMap 0.4.6, your folder" for a folder of the player.</summary>
    public string Title
    {
        get
        {
            var title = Version.Version is null ? Name : $"{Name} {VersionText}";
            return IsAdopted ? _owner.Localization.FormatSettingsLoaderYourFolder(title) : title;
        }
    }

    /// <summary>A version Borea does not know cannot be rated, so its chip says that instead.</summary>
    public string CompatibilityText => Version.Version is null
        ? _owner.Localization.SetupLoaderInstalledUnknownVersion
        : _owner.CompatibilityText(Compatibility);

    public bool IsCompatible => Compatibility == GameCompatibility.Compatible;

    public bool IsIncompatible => Compatibility == GameCompatibility.Incompatible;

    /// <summary>"Used by A, B, C, ..." with at most <see cref="UsersShown"/> names, "Not used", or null when Borea could not tell.</summary>
    public string? UsedByText => Users switch
    {
        null => null,
        { Count: 0 } => _owner.Localization.SettingsLoaderUnused,
        { Count: <= UsersShown } => _owner.Localization.FormatSettingsLoaderUsedBy(string.Join(", ", Users)),
        _ => _owner.Localization.FormatSettingsLoaderUsedBy(string.Join(", ", Users.Take(UsersShown)) + ", ..."),
    };

    /// <summary>Every name, only when <see cref="UsedByText"/> cut the list.</summary>
    public string? UsedByToolTip => Users is { Count: > UsersShown }
        ? _owner.Localization.FormatSettingsLoaderUsedBy(string.Join(", ", Users))
        : null;

    /// <summary>Only a version that no instance starts can go, so no launch ever finds its loader gone.</summary>
    public bool CanRemove => Users is { Count: 0 };

    /// <summary>Why Remove is disabled, with every instance that starts the version.</summary>
    public string? RemoveToolTip => Users is { Count: > 0 }
        ? _owner.Localization.FormatSettingsLoaderInUse(string.Join(", ", Users))
        : null;

    public string RemoveText => IsAdopted ? _owner.Localization.SettingsLoaderForget : _owner.Localization.ContentRemove;

    /// <summary>Remove deletes files, so it asks once, the way the presets delete does. Forget deletes nothing and does not ask.</summary>
    [ObservableProperty]
    private bool _isConfirmingRemove;

    [RelayCommand]
    private Task RemoveAsync()
    {
        if (!IsAdopted && !IsConfirmingRemove)
        {
            IsConfirmingRemove = true;
            return Task.CompletedTask;
        }

        return _owner.RemoveLoaderVersionAsync(this);
    }

    [RelayCommand]
    private void CancelRemove() => IsConfirmingRemove = false;

    /// <summary>Every text in the new language.</summary>
    internal void RefreshText() => OnPropertyChanged(string.Empty);
}
