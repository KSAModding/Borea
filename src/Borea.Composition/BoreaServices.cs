using System.Net.Http.Headers;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.Settings;
using Borea.Core.State;
using Borea.Network.MasterServer;
using Borea.Network.Sources;
using Borea.Network.SpaceDock;
using Borea.Storage.Instances;
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

    public required IInstanceRepository Instances { get; init; }

    public required IModStateRepository ModState { get; init; }

    public required IModFavoritesRepository ModFavorites { get; init; }

    public required IModPackFavoritesRepository ModPackFavorites { get; init; }

    public required IModUninstaller Uninstaller { get; init; }

    /// <summary>
    /// Every mod source behind one repository, each listing tagged with its source.
    /// </summary>
    public required IModRepository Mods { get; init; }

    public required IModDownloader Downloader { get; init; }

    public required ILatestVersionPing LatestVersion { get; init; }

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
    public static async Task<BoreaServices> BuildAsync(string? boreaRoot, CancellationToken cancellationToken = default)
    {
        // the settings file lives under Borea's own root and needs no
        // game path to be found, so a provider without one reads it.
        var bootstrapPaths = new GamePathProvider(gameDirectory: null, boreaRoot: boreaRoot);
        var saved = await new FileBoreaSettingsRepository(bootstrapPaths).GetAsync(cancellationToken).ConfigureAwait(false);
        var settings = saved ?? new BoreaSettings(gameDirectoryPath: null);

        // every other service resolves its paths through the provider
        // built from those settings.
        var paths = new GamePathProvider(settings.GameDirectoryPath, settings.LoaderDirectoryPaths, boreaRoot);

        // Network. Every service that talks to a remote host is built here on the
        // one client. The resolver is shared because the downloader registers the
        // true mod id that the repository then resolves, and its map lives as long
        // as this instance.
        var http = BuildHttpClient();
        var resolver = new SpaceDockResolver();
        var sources = new Dictionary<string, IModRepository>
        {
            [SpaceDockModRepository.SourceName] = new SpaceDockModRepository(http, resolver),
        };

        return new BoreaServices(http)
        {
            Settings = settings,
            Paths = paths,
            SettingsRepository = new FileBoreaSettingsRepository(paths),
            Instances = new FileInstanceRepository(paths),
            ModState = new FileModStateRepository(paths),
            ModFavorites = new FileModFavoritesRepository(paths),
            ModPackFavorites = new FileModPackFavoritesRepository(paths),
            Uninstaller = new FileModUninstaller(paths),
            Mods = new CompositeModRepository(sources),
            Downloader = new SpaceDockModDownloader(http, resolver),
            LatestVersion = new LatestVersionPing(http),
        };
    }

    private static HttpClient BuildHttpClient()
    {
        var handler = new SocketsHttpHandler { PooledConnectionLifetime = ConnectionLifetime };
        var http = new HttpClient(handler);

        var version = typeof(BoreaServices).Assembly.GetName().Version?.ToString(3);
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Borea", version));

        return http;
    }

    public void Dispose() => _http.Dispose();
}
