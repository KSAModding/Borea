using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Logging;
using Borea.Core.ModLoaders;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.Planning;
using Borea.Core.Preferences;
using Borea.Core.Settings;
using Borea.Core.State;
using Borea.Core.Updates;

namespace Borea.Cli;

/// <summary>
/// The services the commands run against, seen as Borea.Core interfaces.
/// The program fills it from <see cref="BoreaServices"/>.
/// </summary>
internal sealed class CliServices : IDisposable
{
    /// <summary>
    /// The settings the graph was built from. Empty settings when no file was
    /// saved yet.
    /// </summary>
    public required BoreaSettings Settings { get; init; }

    public required IBoreaSettingsRepository SettingsRepository { get; init; }

    public required IGameDirectoryChanger GameDirectoryChanger { get; init; }

    public required ILibraryFolderChanger LibraryFolderChanger { get; init; }

    /// <summary>Whether a KSA or StarMap process runs, whoever started it.</summary>
    public required Func<bool> IsGameProcessRunning { get; init; }

    public required IInstanceRepository Instances { get; init; }

    public required IModStateRepository ModState { get; init; }

    public required IGameLogReader GameLog { get; init; }

    public required IGameSaveBackupStore GameSaveBackups { get; init; }

    public required IPlaytimeService Playtime { get; init; }

    public required IModListFormat ModListFormat { get; init; }

    public required ILatestVersionPing LatestVersion { get; init; }

    public required IInstalledGameVersionProvider InstalledVersion { get; init; }

    public required IGameShapeCheck GameShape { get; init; }

    public required IContentIndexFetcher IndexFetcher { get; init; }

    public required IContentIndexReader IndexReader { get; init; }

    public required IContentIndexSnapshotProvider IndexSnapshots { get; init; }

    public required IContentIndexRefresh IndexRefresh { get; init; }

    public required IGamePathProvider Paths { get; init; }

    public required IBoreaLog Log { get; init; }

    public required IModRepository Mods { get; init; }

    public required IModRepository ReadOnlyMods { get; init; }

    public required IInstallPlanner InstallPlanner { get; init; }

    public required IInstallSpaceCheck SpaceCheck { get; init; }

    public required IModInstaller Installer { get; init; }

    public required IModReplacer Replacer { get; init; }

    public required IModUninstaller Uninstaller { get; init; }

    public required IForeignModAdopter ForeignModAdopter { get; init; }

    public required IMissingModDetector MissingMods { get; init; }
    public required IForeignModHandover ForeignModHandover { get; init; }

    public required ISharedModStore SharedModStore { get; init; }

    public required ISharedProfileImporter SharedProfileImporter { get; init; }

    public required ILoaderInstaller LoaderInstaller { get; init; }

    public required ILoaderAdopter LoaderAdopter { get; init; }

    public required ILoaderUninstaller LoaderUninstaller { get; init; }

    public required ILauncher Launcher { get; init; }

    public required IModPackRepository ModPacks { get; init; }

    public required IModPackRepository ReadOnlyModPacks { get; init; }

    public required IModPackInstaller ModPackInstaller { get; init; }

    public required IModPackUpdater ModPackUpdater { get; init; }

    public required ISharedProfileLauncher SharedProfileLauncher { get; init; }

    public required IAppPreferencesRepository AppPreferences { get; init; }

    public required IBoreaReleaseCheck ReleaseCheck { get; init; }

    public required ISelfUpdater SelfUpdater { get; init; }

    /// <summary>
    /// The graph the services came from, disposed with this instance. Null when
    /// nothing needs disposing.
    /// </summary>
    public IDisposable? Graph { get; init; }

    public IDisposable? AdditionalDisposable { get; init; }

    /// <summary>
    /// The graph's services. <paramref name="latestVersion"/> replaces the
    /// graph's master-server ping and <paramref name="installedVersion"/> its
    /// reader of the installed build, which is what a test needs.
    /// <paramref name="spaceCheck"/> replaces the check against the real
    /// volumes, so a test does not depend on the free space of the machine it
    /// runs on.
    /// </summary>
    public static CliServices From(
        BoreaServices services,
        IInstanceRepository? instances = null,
        ILatestVersionPing? latestVersion = null,
        IInstalledGameVersionProvider? installedVersion = null,
        IGameShapeCheck? gameShape = null,
        IContentIndexFetcher? indexFetcher = null,
        IContentIndexReader? indexReader = null,
        IContentIndexSnapshotProvider? indexSnapshots = null,
        IModRepository? mods = null,
        IModRepository? readOnlyMods = null,
        IInstallPlanner? installPlanner = null,
        IInstallSpaceCheck? spaceCheck = null,
        IModInstaller? installer = null,
        IModReplacer? replacer = null,
        IModUninstaller? uninstaller = null,
        IForeignModAdopter? foreignModAdopter = null,
        IMissingModDetector? missingMods = null,
        IForeignModHandover? foreignModHandover = null,
        ILoaderInstaller? loaderInstaller = null,
        ILoaderAdopter? loaderAdopter = null,
        ILoaderUninstaller? loaderUninstaller = null,
        ILauncher? launcher = null,
        IModPackRepository? modPacks = null,
        IModPackRepository? readOnlyModPacks = null,
        IModPackInstaller? modPackInstaller = null,
        IModPackUpdater? modPackUpdater = null,
        ISharedProfileLauncher? sharedProfileLauncher = null,
        IContentIndexRefresh? indexRefresh = null,
        ISharedProfileImporter? sharedProfileImporter = null,
        ILibraryFolderChanger? libraryFolderChanger = null,
        IBoreaReleaseCheck? releaseCheck = null,
        ISelfUpdater? selfUpdater = null,
        Func<bool>? isGameProcessRunning = null,
        ISharedModStore? sharedModStore = null)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        return new CliServices
        {
            Settings = services.Settings,
            SettingsRepository = services.SettingsRepository,
            GameDirectoryChanger = services.GameDirectoryChanger,
            LibraryFolderChanger = libraryFolderChanger ?? services.LibraryFolderChanger,
            IsGameProcessRunning = isGameProcessRunning ?? services.IsGameProcessRunning,
            Instances = instances ?? services.Instances,
            ModState = services.ModState,
            GameLog = services.GameLog,
            GameSaveBackups = services.GameSaveBackups,
            Playtime = services.Playtime,
            ModListFormat = services.ModListFormat,
            LatestVersion = latestVersion ?? services.LatestVersion,
            InstalledVersion = installedVersion ?? services.InstalledVersion,
            GameShape = gameShape ?? services.GameShape,
            IndexFetcher = indexFetcher ?? services.IndexFetcher,
            IndexReader = indexReader ?? services.IndexReader,
            IndexSnapshots = indexSnapshots ?? services.IndexSnapshots,
            IndexRefresh = indexRefresh ?? services.IndexRefresh,
            Paths = services.Paths,
            Log = services.Log,
            Mods = mods ?? services.Mods,
            ReadOnlyMods = readOnlyMods ?? services.ReadOnlyMods,
            InstallPlanner = installPlanner ?? services.InstallPlanner,
            SpaceCheck = spaceCheck ?? services.SpaceCheck,
            Installer = installer ?? services.Installer,
            Replacer = replacer ?? services.Replacer,
            Uninstaller = uninstaller ?? services.Uninstaller,
            ForeignModAdopter = foreignModAdopter ?? services.ForeignModAdopter,
            MissingMods = missingMods ?? services.MissingMods,
            ForeignModHandover = foreignModHandover ?? services.ForeignModHandover,
            SharedModStore = sharedModStore ?? services.SharedModStore,
            SharedProfileImporter = sharedProfileImporter ?? services.SharedProfileImporter,
            LoaderInstaller = loaderInstaller ?? services.LoaderInstaller,
            LoaderAdopter = loaderAdopter ?? services.LoaderAdopter,
            LoaderUninstaller = loaderUninstaller ?? services.LoaderUninstaller,
            Launcher = launcher ?? services.Launcher,
            ModPacks = modPacks ?? services.ModPacks,
            ReadOnlyModPacks = readOnlyModPacks ?? services.ReadOnlyModPacks,
            ModPackInstaller = modPackInstaller ?? services.ModPackInstaller,
            ModPackUpdater = modPackUpdater ?? services.ModPackUpdater,
            SharedProfileLauncher = sharedProfileLauncher ?? services.SharedProfileLauncher,
            AppPreferences = services.AppPreferences,
            ReleaseCheck = releaseCheck ?? services.ReleaseCheck,
            SelfUpdater = selfUpdater ?? services.SelfUpdater,
            Graph = services,
            AdditionalDisposable = launcher is IDisposable disposable && !ReferenceEquals(launcher, services.Launcher)
                ? disposable
                : null,
        };
    }

    public void Dispose()
    {
        AdditionalDisposable?.Dispose();
        Graph?.Dispose();
    }
}
