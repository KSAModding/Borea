using System.Net.Http.Headers;
using Borea.Core.Dependencies;
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
using Borea.Network.Downloads;
using Borea.Network.GitHub;
using Borea.Network.Images;
using Borea.Network.Index;
using Borea.Network.MasterServer;
using Borea.Network.Planning;
using Borea.Network.Sources;
using Borea.Network.SpaceDock;
using Borea.Storage.Game;
using Borea.Storage.Images;
using Borea.Storage.Instances;
using Borea.Storage.Index;
using Borea.Storage.Launch;
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
    /// User-Agent and its handler recycles pooled connections, so nothing creates
    /// a second one. LatestVersionPing caches per instance, so the single instance
    /// built here is the one to use.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// The settings the graph was built from. Empty settings when no file was
    /// saved yet.
    /// </summary>
    public required BoreaSettings Settings { get; init; }

    public required IGamePathProvider Paths { get; init; }

    /// <summary>Borea's daily log. Installs, plans, index fetches and launches write to it.</summary>
    public required IBoreaLog Log { get; init; }

    public required IBoreaSettingsRepository SettingsRepository { get; init; }

    public required IGameDirectoryChanger GameDirectoryChanger { get; init; }

    public required IAppPreferencesRepository AppPreferences { get; init; }

    public required IInstanceRepository Instances { get; init; }

    public required IGameDataReader GameData { get; init; }

    public required IGameLogReader GameLog { get; init; }

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

    public required IInstalledGameVersionProvider InstalledVersion { get; init; }

    public required IInstallDetector InstallDetector { get; init; }

    public required IContentIndexFetcher IndexFetcher { get; init; }

    public required IContentIndexReader IndexReader { get; init; }

    public required IContentIndexSnapshotProvider IndexSnapshots { get; init; }

    public required IContentIndexRefresh IndexRefresh { get; init; }

    public required IContentIndexRepository ContentIndex { get; init; }

    /// <summary>Listing images, verified against their records, from the cache or the author hosts.</summary>
    public required IContentImageSource Images { get; init; }

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
        => BuildCoreAsync(boreaRoot, logSource, httpHandler: null, fallbackRepository: null, installCandidates: null, cancellationToken);

    internal static Task<BoreaServices> BuildAsync(
        string? boreaRoot,
        HttpMessageHandler httpHandler,
        IModRepository fallbackRepository,
        CancellationToken cancellationToken = default)
        => BuildAsync(boreaRoot, httpHandler, fallbackRepository, new NoInstallCandidates(), cancellationToken);

    /// <param name="processStarter">Starts the launchers' processes. Null starts real ones.</param>
    /// <param name="images">Serves listing images. Null fetches them from the author hosts.</param>
    /// <param name="sharedProfileRoot">The game's own profile. Null means the one in My Games.</param>
    internal static Task<BoreaServices> BuildAsync(
        string? boreaRoot,
        HttpMessageHandler httpHandler,
        IModRepository fallbackRepository,
        IInstallCandidateSource installCandidates,
        CancellationToken cancellationToken = default,
        IProcessStarter? processStarter = null,
        IContentImageSource? images = null,
        string? sharedProfileRoot = null)
    {
        ArgumentNullException.ThrowIfNull(httpHandler);
        ArgumentNullException.ThrowIfNull(fallbackRepository);
        ArgumentNullException.ThrowIfNull(installCandidates);
        return BuildCoreAsync(boreaRoot, BoreaLogSource.App, httpHandler, fallbackRepository, installCandidates, cancellationToken, processStarter, images, sharedProfileRoot);
    }

    private static async Task<BoreaServices> BuildCoreAsync(
        string? boreaRoot,
        BoreaLogSource logSource,
        HttpMessageHandler? httpHandler,
        IModRepository? fallbackRepository,
        IInstallCandidateSource? installCandidates,
        CancellationToken cancellationToken,
        IProcessStarter? processStarter = null,
        IContentImageSource? images = null,
        string? sharedProfileRoot = null)
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
        var paths = new GamePathProvider(settings.GameDirectoryPath, loaderDirectories, boreaRoot, sharedProfileRoot);
        var log = new FileBoreaLog(paths, logSource);

        // Network. Every service that talks to a remote host is built here on the
        // one client, except the image source, which needs a handler of its own.
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
        var downloader = new HttpModDownloader(http);
        var settingsRepository = new FileBoreaSettingsRepository(paths);
        var loaderConfiguration = new LoaderConfigurator();
        var instances = new FileInstanceRepository(paths);
        var loaderAdopter = new FileLoaderAdopter(settingsRepository, loaderConfiguration);
        installCandidates ??= OperatingSystem.IsWindows() ? new WindowsInstallCandidateSource() : new NoInstallCandidates();

        var modState = new FileModStateRepository(paths);
        var modInstaller = new LoggingModInstaller(new FileModInstaller(paths, downloader, instances, modState), log);
        var modReplacer = new LoggingModReplacer(new FileModReplacer(paths, downloader, instances, modState), log);
        var foreignModAdopter = new FileForeignModAdopter(paths, instances, contentIndex);
        var foreignModReleaseMatcher = new FileForeignModReleaseMatcher(paths, downloader, foreignModAdopter, indexSnapshots);
        var installPlanner = new LoggingInstallPlanner(new RepositoryInstallPlanner(new ModDependencyResolver(), settings.ReleaseChannel), log);

        return new BoreaServices(http)
        {
            Settings = settings,
            Paths = paths,
            Log = log,
            SettingsRepository = settingsRepository,
            GameDirectoryChanger = new GameDirectoryChanger(settingsRepository, mods, loaderConfiguration),
            AppPreferences = new FileAppPreferencesRepository(paths),
            Instances = instances,
            GameData = new FileGameDataReader(paths),
            GameLog = new FileGameLogReader(paths),
            ModState = modState,
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
            ModPacks = modPacks,
            ReadOnlyModPacks = new ContentIndexModPackRepository(new ReaderSnapshotProvider(indexReader)),
            ModPackInstaller = new ModPackInstaller(instances, installPlanner, modInstaller, modReplacer),
            Downloader = downloader,
            InstallPlanner = installPlanner,
            PlanExecutor = new InstallPlanExecutor(instances, modInstaller, modReplacer),
            LoaderInstaller = new FileLoaderInstaller(paths, downloader, settingsRepository, loaderConfiguration),
            LoaderAdopter = loaderAdopter,
            LoaderUninstaller = new FileLoaderUninstaller(settingsRepository),
            Launcher = new LoggingLauncher(new LoaderLauncher(paths, processStarter ?? new ProcessStarter()), log),
            SharedProfileLauncher = new SharedProfileLauncher(paths, processStarter ?? new ProcessStarter()),
            LatestVersion = new LatestVersionPing(http),
            ReleaseCheck = new BoreaReleaseCheck(http),
            InstalledVersion = new InstalledGameVersionProvider(paths),
            InstallDetector = new InstallDetector(installCandidates, loaderAdopter, paths.GetLoadersRoot()),
            IndexFetcher = indexFetcher,
            IndexReader = indexReader,
            IndexSnapshots = indexSnapshots,
            IndexRefresh = indexSnapshots,
            ContentIndex = contentIndex,
            Images = images ?? new ContentImageSource(new FileContentImageCache(paths)),
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
