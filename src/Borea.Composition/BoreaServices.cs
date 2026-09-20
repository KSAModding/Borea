using System.Net.Http.Headers;
using Borea.Core.Announcements;
using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.GitHub;
using Borea.Core.History;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Listings;
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
using Borea.Network.Announcements;
using Borea.Network.Downloads;
using Borea.Network.GitHub;
using Borea.Network.Images;
using Borea.Network.Index;
using Borea.Network.Listings;
using Borea.Network.MasterServer;
using Borea.Network.Planning;
using Borea.Network.Sources;
using Borea.Network.SpaceDock;
using Borea.Storage.Announcements;
using Borea.Storage.Game;
using Borea.Storage.History;
using Borea.Storage.Images;
using Borea.Storage.Instances;
using Borea.Storage.Index;
using Borea.Storage.Launch;
using Borea.Storage.Listings;
using Borea.Storage.Logging;
using Borea.Storage.ModLoaders;
using Borea.Storage.ModPacks;
using Borea.Storage.Mods;
using Borea.Storage.Paths;
using Borea.Storage.Preferences;
using Borea.Storage.Settings;
using Borea.Storage.State;

namespace Borea.Composition;

/// <summary>
/// The composition root.
/// Builds every service an executable uses, once, from the saved settings.
/// Borea.Storage and Borea.Network do not reference each other,
/// so this is the one place that names their classes.
/// An executable sees only the Borea.Core interfaces.
/// The paths are fixed when the graph is built, because GamePathProvider takes
/// them in its constructor. To apply changed settings, save them through
/// <see cref="SettingsRepository"/>, dispose this instance, and build again.
/// </summary>
public sealed class BoreaServices : IDisposable
{
    private static readonly Uri ContentIndexUri = new("https://ksamodding.github.io/content-index-releases/v1/index.json");

    /// <summary>
    /// The client lives as long as the process, so its handler must drop pooled
    /// connections after this time. If it keeps them, the client sends to the old
    /// address after a DNS change.
    /// </summary>
    private static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The one HttpClient every network service shares. It names Borea in its
    /// User-Agent and its handler recycles pooled connections, so only the process
    /// GitHub session creates a second one. LatestVersionPing caches per instance,
    /// so the single instance built here is the one to use.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// The settings the graph was built from. Empty settings when no file was
    /// saved yet.
    /// </summary>
    public required BoreaSettings Settings { get; init; }

    public required IGamePathProvider Paths { get; init; }

    /// <summary>Borea's daily log. Installs, plans, index fetches, launches and instance changes write to it.</summary>
    public required IBoreaLog Log { get; init; }

    public required IBoreaSettingsRepository SettingsRepository { get; init; }

    public required IGameDirectoryChanger GameDirectoryChanger { get; init; }

    /// <summary>Moves the Instances and Backups folders. Build the services again after a change.</summary>
    public required ILibraryFolderChanger LibraryFolderChanger { get; init; }

    public required IAppPreferencesRepository AppPreferences { get; init; }

    public required ITaskHistoryRepository TaskHistory { get; init; }

    public required IInstanceRepository Instances { get; init; }

    public required IGameSettingsPresetRepository GameSettingsPresets { get; init; }

    public required IGameDataReader GameData { get; init; }

    public required IGameSaveStore GameSaves { get; init; }

    public required IGameSaveBackupStore GameSaveBackups { get; init; }

    public required IGameLogReader GameLog { get; init; }

    public required IPlaytimeService Playtime { get; init; }

    public required IInstanceSizeReader InstanceSizes { get; init; }

    public required IModListFormat ModListFormat { get; init; }

    public required IModStateRepository ModState { get; init; }

    public required IModFavoritesRepository ModFavorites { get; init; }

    public required IModPackFavoritesRepository ModPackFavorites { get; init; }

    public required IModUninstaller Uninstaller { get; init; }

    public required IModInstaller Installer { get; init; }

    public required IModReplacer Replacer { get; init; }

    public required IForeignModAdopter ForeignModAdopter { get; init; }

    public required IForeignModReleaseMatcher ForeignModReleaseMatcher { get; init; }

    public required ISharedProfileImporter SharedProfileImporter { get; init; }

    /// <summary>
    /// Every mod source behind one repository, each listing tagged with its source.
    /// The newest release follows the saved release channel.
    /// </summary>
    public required IModRepository Mods { get; init; }

    public required IModRepository ReadOnlyMods { get; init; }

    /// <summary>The mods of the content index that Borea holds, read without a request to any host.</summary>
    public required IModRepository OfflineMods { get; init; }

    public required IModPackRepository ModPacks { get; init; }

    public required IModPackRepository ReadOnlyModPacks { get; init; }

    public required IModPackInstaller ModPackInstaller { get; init; }

    public required IModDownloader Downloader { get; init; }

    public required IInstallPlanner InstallPlanner { get; init; }

    public required IInstallPlanExecutor PlanExecutor { get; init; }

    public required ILoaderInstaller LoaderInstaller { get; init; }

    public required ILoaderAdopter LoaderAdopter { get; init; }

    public required ILoaderUninstaller LoaderUninstaller { get; init; }

    public required ILauncher Launcher { get; init; }

    public required ISharedProfileLauncher SharedProfileLauncher { get; init; }

    public required ILatestVersionPing LatestVersion { get; init; }

    /// <summary>The newest published Borea release.</summary>
    public required IBoreaReleaseCheck ReleaseCheck { get; init; }

    /// <summary>The posts of the KSAModding team, fetched from the Borea repository and cached.</summary>
    public required IAnnouncementFeed Announcements { get; init; }

    public required IInstalledGameVersionProvider InstalledVersion { get; init; }

    public required IGamePatchNotesReader GamePatchNotes { get; init; }

    public required IGamePatchNotesFetcher GamePatchNotesFetcher { get; init; }

    public required IInstallDetector InstallDetector { get; init; }

    public required IContentIndexFetcher IndexFetcher { get; init; }

    public required IContentIndexReader IndexReader { get; init; }

    public required IContentIndexSnapshotProvider IndexSnapshots { get; init; }

    public required IContentIndexRefresh IndexRefresh { get; init; }

    public required IContentIndexRepository ContentIndex { get; init; }

    /// <summary>Listing images, verified against their records, from the cache or the author hosts.</summary>
    public required IContentImageSource Images { get; init; }

    /// <summary>The user's GitHub sign-in, which every graph built by a public overload shares.</summary>
    public required IGitHubSession GitHub { get; init; }

    /// <summary>Reads a release host and its latest archive for a new listing.</summary>
    public required IListingSourceReader ListingSources { get; init; }

    public required IForumThreadReader ForumThreads { get; init; }

    public required IListingImageMeasurer ListingImages { get; init; }

    public required IListedDocumentSource ListedDocuments { get; init; }

    public required IListingFormat ListingFormat { get; init; }

    public required IListingValidator ListingValidator { get; init; }

    /// <summary>The games this process started, which every graph built by a public overload shares.</summary>
    private static readonly RunningLaunches ProcessLaunches = new();

    /// <summary>
    /// The GitHub session of this process, on a client of its own, so a graph rebuilt
    /// after a settings change keeps the user signed in. The client does not follow
    /// redirects, because a followed redirect drops the token, so a moved repository
    /// comes back as its 3xx answer.
    /// </summary>
    private static readonly Lazy<GitHubSession> ProcessGitHub = new(() => new GitHubSession(
        BuildHttpClient(new SocketsHttpHandler { PooledConnectionLifetime = ConnectionLifetime, AllowAutoRedirect = false }),
        BoreaGitHubApp.ClientId,
        BoreaGitHubApp.Slug));

    private BoreaServices(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Builds the services from the settings under Borea's default root,
    /// %LocalAppData%\Borea.
    /// </summary>
    public static Task<BoreaServices> BuildAsync(CancellationToken cancellationToken = default)
        => BuildAsync(boreaRoot: null, cancellationToken);

    /// <summary>
    /// Builds the services from the settings under <paramref name="boreaRoot"/>.
    /// Reads the settings file and writes nothing.
    /// </summary>
    /// <param name="boreaRoot">
    /// Where Borea keeps its own files. Null means the default root of
    /// <see cref="GamePathProvider"/>, %LocalAppData%\Borea.
    /// </param>
    public static Task<BoreaServices> BuildAsync(string? boreaRoot, CancellationToken cancellationToken = default)
        => BuildAsync(boreaRoot, BoreaLogSource.App, cancellationToken);

    /// <summary>Builds the services like the overload above, with log lines marked by <paramref name="logSource"/>.</summary>
    public static Task<BoreaServices> BuildAsync(string? boreaRoot, BoreaLogSource logSource, CancellationToken cancellationToken = default)
        => BuildCoreAsync(boreaRoot, logSource, httpHandler: null, fallbackRepository: null, installCandidates: null, ProcessLaunches, cancellationToken, gitHub: ProcessGitHub.Value);

    internal static Task<BoreaServices> BuildAsync(
        string? boreaRoot,
        HttpMessageHandler httpHandler,
        IModRepository fallbackRepository,
        CancellationToken cancellationToken = default)
        => BuildAsync(boreaRoot, httpHandler, fallbackRepository, new NoInstallCandidates(), cancellationToken);

    /// <param name="processStarter">Starts the launchers' processes. Null starts real ones.</param>
    /// <param name="images">Serves listing images. Null fetches them from the author hosts.</param>
    /// <param name="sharedProfileRoot">The game's own profile. Null means the one in My Games.</param>
    /// <param name="isGameProcessRunning">Whether a KSA or StarMap process runs. Null looks for one.</param>
    /// <param name="isOtherBoreaRunning">Whether another Borea App or command runs. Null looks for one.</param>
    /// <param name="gitHub">The GitHub session. Null builds one on this graph's client for <see cref="BoreaGitHubApp"/>.</param>
    internal static Task<BoreaServices> BuildAsync(
        string? boreaRoot,
        HttpMessageHandler httpHandler,
        IModRepository fallbackRepository,
        IInstallCandidateSource installCandidates,
        CancellationToken cancellationToken = default,
        IProcessStarter? processStarter = null,
        IContentImageSource? images = null,
        string? sharedProfileRoot = null,
        Func<bool>? isGameProcessRunning = null,
        Func<bool>? isOtherBoreaRunning = null,
        IGitHubSession? gitHub = null)
    {
        ArgumentNullException.ThrowIfNull(httpHandler);
        ArgumentNullException.ThrowIfNull(fallbackRepository);
        ArgumentNullException.ThrowIfNull(installCandidates);
        return BuildCoreAsync(boreaRoot, BoreaLogSource.App, httpHandler, fallbackRepository, installCandidates, new RunningLaunches(), cancellationToken, processStarter, images, sharedProfileRoot, isGameProcessRunning, isOtherBoreaRunning, gitHub);
    }

    private static async Task<BoreaServices> BuildCoreAsync(
        string? boreaRoot,
        BoreaLogSource logSource,
        HttpMessageHandler? httpHandler,
        IModRepository? fallbackRepository,
        IInstallCandidateSource? installCandidates,
        RunningLaunches launches,
        CancellationToken cancellationToken,
        IProcessStarter? processStarter = null,
        IContentImageSource? images = null,
        string? sharedProfileRoot = null,
        Func<bool>? isGameProcessRunning = null,
        Func<bool>? isOtherBoreaRunning = null,
        IGitHubSession? gitHub = null)
    {
        // the settings file lives under Borea's own root and needs no
        // game path to be found, so a provider without one reads it.
        var bootstrapPaths = new GamePathProvider(gameDirectory: null, boreaRoot: boreaRoot);
        var saved = await new FileBoreaSettingsRepository(bootstrapPaths).GetAsync(cancellationToken).ConfigureAwait(false);
        var settings = saved ?? new BoreaSettings(gameDirectoryPath: null);

        // every other service resolves its paths through the provider
        // built from those settings.
        var loaderDirectories = settings.LoaderInstallations.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.DirectoryPath,
            ModIds.Comparer);
        var paths = new GamePathProvider(settings.GameDirectoryPath, loaderDirectories, boreaRoot, sharedProfileRoot, settings.LibraryFolderPath);
        var log = new FileBoreaLog(paths, logSource);

        // Network. Every service that talks to a remote host is built here on the
        // one client, except the image source, which needs a handler of its own,
        // and the GitHub session, which outlives the graph.
        // Only the SpaceDock repository takes the resolver, because a
        // release carries an absolute download URL and the downloader needs no
        // host of its own.
        var http = BuildHttpClient(httpHandler);
        var resolver = new SpaceDockResolver();
        var indexReader = new ContentIndexReader(paths, ContentIndexModRepository.SourceName);
        var indexFetcher = new LoggingContentIndexFetcher(new ContentIndexFetcher(http, ContentIndexUri, indexReader), log);
        var indexSnapshots = new ContentIndexSnapshotProvider(indexFetcher, indexReader, paths);
        var contentIndex = new ContentIndexModRepository(indexSnapshots);
        var readOnlyContentIndex = new ContentIndexModRepository(new ReaderSnapshotProvider(indexReader));
        var modPacks = new ContentIndexModPackRepository(indexSnapshots);
        var spaceDock = fallbackRepository ?? new SpaceDockModRepository(http, resolver);
        var sources = new Dictionary<string, IModRepository>
        {
            [ContentIndexModRepository.SourceName] = contentIndex,
            [SpaceDockModRepository.SourceName] = spaceDock,
        };
        var mods = new CompositeModRepository(sources);
        var readOnlyMods = new CompositeModRepository(new Dictionary<string, IModRepository>
        {
            [ContentIndexModRepository.SourceName] = readOnlyContentIndex,
            [SpaceDockModRepository.SourceName] = spaceDock,
        });
        var offlineMods = new CompositeModRepository(new Dictionary<string, IModRepository>
        {
            [ContentIndexModRepository.SourceName] = new ContentIndexModRepository(indexSnapshots.CachedOnly),
            [SpaceDockModRepository.SourceName] = new OfflineSpaceDockModRepository(resolver),
        });
        var downloader = new HttpModDownloader(http);
        var settingsRepository = new FileBoreaSettingsRepository(paths);
        var loaderConfiguration = new LoaderConfigurator();
        var fileInstances = new FileInstanceRepository(paths);
        var instances = new LoggingInstanceRepository(fileInstances, paths, log);
        var loaderAdopter = new FileLoaderAdopter(settingsRepository, loaderConfiguration);
        installCandidates ??= OperatingSystem.IsWindows() ? new WindowsInstallCandidateSource() : new NoInstallCandidates();

        var modState = new FileModStateRepository(paths);
        var fileGameSettingsPresets = new GameSettingsPresetRepository(paths);
        var gameSettingsPresets = new LoggingGameSettingsPresetRepository(fileGameSettingsPresets, log);
        var modInstaller = new LoggingModInstaller(new FileModInstaller(paths, downloader, instances, modState), log);
        var modReplacer = new LoggingModReplacer(new FileModReplacer(paths, downloader, instances, modState), log);
        var foreignModAdopter = new FileForeignModAdopter(paths, instances, contentIndex);
        var foreignModReleaseMatcher = new FileForeignModReleaseMatcher(paths, downloader, foreignModAdopter, indexSnapshots);
        var installPlanner = new LoggingInstallPlanner(new RepositoryInstallPlanner(new ModDependencyResolver(), settings.ReleaseChannel), log);
        var launcher = new LoggingLauncher(new LastPlayedLauncher(new LoaderLauncher(paths, processStarter ?? new ProcessStarter(), launches), instances), log);
        var defaultLibraryFolder = Path.GetDirectoryName(bootstrapPaths.GetInstancesRoot())!;
        var announcementReader = new AnnouncementReader();
        var listedDocuments = new ListedDocumentFetcher(http);

        return new BoreaServices(http)
        {
            Settings = settings,
            Paths = paths,
            Log = log,
            SettingsRepository = settingsRepository,
            GameDirectoryChanger = new GameDirectoryChanger(settingsRepository, mods, loaderConfiguration),
            LibraryFolderChanger = new LoggingLibraryFolderChanger(new LibraryFolderChanger(settingsRepository, paths, defaultLibraryFolder, launcher, fileInstances, isGameProcessRunning, isOtherBoreaRunning), log),
            AppPreferences = new FileAppPreferencesRepository(paths),
            TaskHistory = new FileTaskHistoryRepository(paths),
            Instances = instances,
            GameData = new FileGameDataReader(paths),
            GameSaves = new FileGameSaveStore(paths),
            GameSaveBackups = new FileGameSaveBackupStore(paths),
            GameLog = new FileGameLogReader(paths),
            Playtime = new FilePlaytimeService(paths),
            InstanceSizes = new FileInstanceSizeReader(paths),
            ModListFormat = new TomlModListFormat(),
            ModState = modState,
            GameSettingsPresets = gameSettingsPresets,
            ModFavorites = new FileModFavoritesRepository(paths),
            ModPackFavorites = new FileModPackFavoritesRepository(paths),
            Uninstaller = new LoggingModUninstaller(new FileModUninstaller(paths, instances), log),
            Installer = modInstaller,
            Replacer = modReplacer,
            ForeignModAdopter = foreignModAdopter,
            ForeignModReleaseMatcher = foreignModReleaseMatcher,
            SharedProfileImporter = new FileSharedProfileImporter(paths, instances, modState, foreignModAdopter, foreignModReleaseMatcher),
            Mods = new ReleaseChannelModRepository(mods, settings.ReleaseChannel),
            ReadOnlyMods = new ReleaseChannelModRepository(readOnlyMods, settings.ReleaseChannel),
            OfflineMods = new ReleaseChannelModRepository(offlineMods, settings.ReleaseChannel),
            ModPacks = modPacks,
            ReadOnlyModPacks = new ContentIndexModPackRepository(new ReaderSnapshotProvider(indexReader)),
            ModPackInstaller = new ModPackInstaller(instances, installPlanner, modInstaller, modReplacer),
            Downloader = downloader,
            InstallPlanner = installPlanner,
            PlanExecutor = new InstallPlanExecutor(instances, modInstaller, modReplacer),
            LoaderInstaller = new FileLoaderInstaller(paths, downloader, settingsRepository, loaderConfiguration),
            LoaderAdopter = loaderAdopter,
            LoaderUninstaller = new FileLoaderUninstaller(settingsRepository),
            Launcher = launcher,
            SharedProfileLauncher = new LoggingSharedProfileLauncher(new SharedProfileLauncher(paths, processStarter ?? new ProcessStarter()), log),
            LatestVersion = new LatestVersionPing(http),
            ReleaseCheck = new BoreaReleaseCheck(http),
            Announcements = new AnnouncementFeed(new AnnouncementFetcher(http, AnnouncementFetcher.DefaultUri, announcementReader), announcementReader, paths, log),
            InstalledVersion = new InstalledGameVersionProvider(paths),
            GamePatchNotes = new FileGamePatchNotesReader(paths),
            GamePatchNotesFetcher = new GamePatchNotesFetcher(http, new FileGamePatchNotesCache(paths)),
            InstallDetector = new InstallDetector(installCandidates, loaderAdopter, paths.GetLoadersRoot()),
            IndexFetcher = indexFetcher,
            IndexReader = indexReader,
            IndexSnapshots = indexSnapshots,
            IndexRefresh = indexSnapshots,
            ContentIndex = contentIndex,
            Images = images ?? new ContentImageSource(new FileContentImageCache(paths)),
            GitHub = new LoggingGitHubSession(gitHub ?? new GitHubSession(http, BoreaGitHubApp.ClientId, BoreaGitHubApp.Slug), log),
            ListingSources = new ListingSourceReader(new ListingHostClient(http), downloader),
            ForumThreads = new ForumThreadReader(http),
            ListingImages = new ListingImageMeasurer(),
            ListedDocuments = listedDocuments,
            ListingFormat = new TomlListingFormat(),
            ListingValidator = new ListingValidator(new ListingSchemaStore(listedDocuments, paths)),
        };
    }

    private static HttpClient BuildHttpClient(HttpMessageHandler? handler)
    {
        handler ??= new SocketsHttpHandler { PooledConnectionLifetime = ConnectionLifetime };
        var http = new HttpClient(handler);

        var version = typeof(BoreaServices).Assembly.GetName().Version?.ToString(3);
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Borea", version));

        return http;
    }

    public void Dispose()
    {
        if (Launcher is IDisposable disposable)
            disposable.Dispose();

        if (Images is IDisposable images)
            images.Dispose();

        if (ListingImages is IDisposable listingImages)
            listingImages.Dispose();

        _http.Dispose();
    }

    private sealed class ReaderSnapshotProvider(IContentIndexReader reader) : IContentIndexSnapshotProvider
    {
        public Task<ContentIndexSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            reader.ReadAsync(cancellationToken);
    }

    private sealed class NoInstallCandidates : IInstallCandidateSource
    {
        public IReadOnlyList<string> GetGameDirectories() => [];

        public IReadOnlyList<string> GetLoaderDirectories() => [];
    }
}
