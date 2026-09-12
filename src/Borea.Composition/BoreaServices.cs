using System.Net.Http.Headers;
using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.Planning;
using Borea.Core.Settings;
using Borea.Core.State;
using Borea.Network.Downloads;
using Borea.Network.Index;
using Borea.Network.MasterServer;
using Borea.Network.Planning;
using Borea.Network.Sources;
using Borea.Network.SpaceDock;
using Borea.Storage.Game;
using Borea.Storage.Instances;
using Borea.Storage.Index;
using Borea.Storage.Launch;
using Borea.Storage.ModLoaders;
using Borea.Storage.ModPacks;
using Borea.Storage.Mods;
using Borea.Storage.Paths;
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

    public required IBoreaSettingsRepository SettingsRepository { get; init; }

    public required IGameDirectoryChanger GameDirectoryChanger { get; init; }

    public required IInstanceRepository Instances { get; init; }

    public required IModStateRepository ModState { get; init; }

    public required IModFavoritesRepository ModFavorites { get; init; }

    public required IModPackFavoritesRepository ModPackFavorites { get; init; }

    public required IModUninstaller Uninstaller { get; init; }

    public required IModInstaller Installer { get; init; }

    public required IModReplacer Replacer { get; init; }

    public required IForeignModAdopter ForeignModAdopter { get; init; }

    /// <summary>
    /// Every mod source behind one repository, each listing tagged with its source.
    /// </summary>
    public required IModRepository Mods { get; init; }

    public required IModRepository ReadOnlyMods { get; init; }

    public required IModPackRepository ModPacks { get; init; }

    public required IModDownloader Downloader { get; init; }

    public required IInstallPlanner InstallPlanner { get; init; }

    public required ILoaderInstaller LoaderInstaller { get; init; }

    public required ILoaderAdopter LoaderAdopter { get; init; }

    public required ILoaderUninstaller LoaderUninstaller { get; init; }

    public required ILauncher Launcher { get; init; }

    public required ILatestVersionPing LatestVersion { get; init; }

    public required IInstalledGameVersionProvider InstalledVersion { get; init; }

    public required IContentIndexFetcher IndexFetcher { get; init; }

    public required IContentIndexReader IndexReader { get; init; }

    public required IContentIndexSnapshotProvider IndexSnapshots { get; init; }

    public required IContentIndexRepository ContentIndex { get; init; }

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
        => BuildCoreAsync(boreaRoot, httpHandler: null, fallbackRepository: null, cancellationToken);

    internal static Task<BoreaServices> BuildAsync(
        string? boreaRoot,
        HttpMessageHandler httpHandler,
        IModRepository fallbackRepository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpHandler);
        ArgumentNullException.ThrowIfNull(fallbackRepository);
        return BuildCoreAsync(boreaRoot, httpHandler, fallbackRepository, cancellationToken);
    }

    private static async Task<BoreaServices> BuildCoreAsync(
        string? boreaRoot,
        HttpMessageHandler? httpHandler,
        IModRepository? fallbackRepository,
        CancellationToken cancellationToken)
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
        var paths = new GamePathProvider(settings.GameDirectoryPath, loaderDirectories, boreaRoot);

        // Network. Every service that talks to a remote host is built here on the
        // one client. Only the SpaceDock repository takes the resolver, because a
        // release carries an absolute download URL and the downloader needs no
        // host of its own.
        var http = BuildHttpClient(httpHandler);
        var resolver = new SpaceDockResolver();
        var indexReader = new ContentIndexReader(paths, ContentIndexModRepository.SourceName);
        var indexFetcher = new ContentIndexFetcher(http, ContentIndexUri, indexReader);
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

        var modState = new FileModStateRepository(paths);

        return new BoreaServices(http)
        {
            Settings = settings,
            Paths = paths,
            SettingsRepository = settingsRepository,
            GameDirectoryChanger = new GameDirectoryChanger(settingsRepository, mods, loaderConfiguration),
            Instances = instances,
            ModState = modState,
            ModFavorites = new FileModFavoritesRepository(paths),
            ModPackFavorites = new FileModPackFavoritesRepository(paths),
            Uninstaller = new FileModUninstaller(paths, instances),
            Installer = new FileModInstaller(paths, downloader, instances, modState),
            Replacer = new FileModReplacer(paths, downloader, instances, modState),
            ForeignModAdopter = new FileForeignModAdopter(paths, instances, contentIndex),
            Mods = mods,
            ReadOnlyMods = readOnlyMods,
            ModPacks = modPacks,
            Downloader = downloader,
            InstallPlanner = new RepositoryInstallPlanner(new ModDependencyResolver()),
            LoaderInstaller = new FileLoaderInstaller(paths, downloader, settingsRepository, loaderConfiguration),
            LoaderAdopter = new FileLoaderAdopter(settingsRepository, loaderConfiguration),
            LoaderUninstaller = new FileLoaderUninstaller(settingsRepository),
            Launcher = new LoaderLauncher(paths, new ProcessStarter()),
            LatestVersion = new LatestVersionPing(http),
            InstalledVersion = new InstalledGameVersionProvider(paths),
            IndexFetcher = indexFetcher,
            IndexReader = indexReader,
            IndexSnapshots = indexSnapshots,
            ContentIndex = contentIndex,
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

        _http.Dispose();
    }

    private sealed class ReaderSnapshotProvider(IContentIndexReader reader) : IContentIndexSnapshotProvider
    {
        public Task<ContentIndexSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            reader.ReadAsync(cancellationToken);
    }
}
