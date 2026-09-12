using System.Net;
using System.Text;
using System.Text.Json;
using Borea.Core.Dependencies;
using Borea.Core.Index;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Settings;
using Borea.Network.Index;
using Borea.Network.Sources;
using Borea.Storage.Game;
using Borea.Storage.Index;
using Borea.Storage.ModLoaders;
using Borea.Storage.Paths;
using Borea.Storage.Settings;

namespace Borea.Composition.Tests;

public sealed class BoreaServicesTests : IDisposable
{
    private const string GamePath = @"C:\Games\KSA";
    private const string StarMapPath = @"C:\Games\StarMap";

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());

    [Fact]
    public async Task BuildAsync_NoSettingsFile_KnowsNoGameAndNoLoader()
    {
        using var services = await BoreaServices.BuildAsync(_tempRoot);

        Assert.Null(services.Settings.GameDirectoryPath);
        Assert.Empty(services.Settings.LoaderInstallations);
        Assert.Null(services.Paths.GetGameDirectoryPath());
        Assert.Null(services.Paths.GetLoaderDirectoryPath("StarMap"));
    }

    [Fact]
    public async Task BuildAsync_NoSettingsFile_WritesNothing()
    {
        using var services = await BoreaServices.BuildAsync(_tempRoot);

        Assert.False(Directory.Exists(_tempRoot));
    }

    [Fact]
    public async Task BuildAsync_SettingsNamingNoGame_KnowsTheLoaderOnly()
    {
        await SaveAsync(new BoreaSettings(null, LoaderAt(StarMapPath)));

        using var services = await BoreaServices.BuildAsync(_tempRoot);

        Assert.Null(services.Paths.GetGameDirectoryPath());
        Assert.Equal(StarMapPath, services.Paths.GetLoaderDirectoryPath("StarMap"));
    }

    [Fact]
    public async Task BuildAsync_FullSettings_KnowsTheGameAndTheLoader()
    {
        await SaveAsync(new BoreaSettings(GamePath, LoaderAt(StarMapPath)));

        using var services = await BoreaServices.BuildAsync(_tempRoot);

        Assert.Equal(GamePath, services.Settings.GameDirectoryPath);
        Assert.Equal(GamePath, services.Paths.GetGameDirectoryPath());
        Assert.Equal(StarMapPath, services.Paths.GetLoaderDirectoryPath("StarMap"));
    }

    [Fact]
    public async Task BuildAsync_RootsBoreaPathsAtTheGivenRoot()
    {
        using var services = await BoreaServices.BuildAsync(_tempRoot);

        Assert.StartsWith(_tempRoot, services.Paths.GetBoreaSettingsPath());
        Assert.StartsWith(_tempRoot, services.Paths.GetInstancesRoot());
    }

    [Fact]
    public async Task BuildAsync_SettingsFileThatDoesNotLoad_Throws()
    {
        // Loader ids that collide by case are rejected by BoreaSettings, and a
        // build must surface that instead of starting with empty settings.
        Directory.CreateDirectory(_tempRoot);
        await File.WriteAllTextAsync(SettingsPath, """
            [LoaderInstallations.StarMap]
            DirectoryPath = 'C:\Games\StarMap'
            IsAdopted = true

            [LoaderInstallations.starmap]
            DirectoryPath = 'C:\Games\Other'
            IsAdopted = true
            """);

        await Assert.ThrowsAsync<ArgumentException>(() => BoreaServices.BuildAsync(_tempRoot));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BuildAsync_WhitespaceRoot_ThrowsArgumentException(string boreaRoot)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => BoreaServices.BuildAsync(boreaRoot));
    }

    [Fact]
    public async Task SettingsRepository_WritesWhereThePathsPoint_AndTheNextBuildReadsIt()
    {
        using (var services = await BoreaServices.BuildAsync(_tempRoot))
        {
            await services.SettingsRepository.SaveAsync(new BoreaSettings(GamePath));

            Assert.True(File.Exists(services.Paths.GetBoreaSettingsPath()));
        }

        using var rebuilt = await BoreaServices.BuildAsync(_tempRoot);

        Assert.Equal(GamePath, rebuilt.Paths.GetGameDirectoryPath());
    }

    [Fact]
    public async Task Mods_IsTheCompositeRepository()
    {
        using var services = await BoreaServices.BuildAsync(_tempRoot);

        Assert.IsType<CompositeModRepository>(services.Mods);
        Assert.IsType<FileLoaderInstaller>(services.LoaderInstaller);
        Assert.IsType<FileLoaderAdopter>(services.LoaderAdopter);
        Assert.IsType<FileLoaderUninstaller>(services.LoaderUninstaller);
        Assert.IsType<GameDirectoryChanger>(services.GameDirectoryChanger);
    }

    [Fact]
    public async Task IndexFetcher_IsTheNetworkFetcher()
    {
        using var services = await BoreaServices.BuildAsync(_tempRoot);

        Assert.IsType<ContentIndexFetcher>(services.IndexFetcher);
    }

    [Fact]
    public async Task IndexReader_IsTheStorageReader()
    {
        using var services = await BoreaServices.BuildAsync(_tempRoot);

        Assert.IsType<ContentIndexReader>(services.IndexReader);
        Assert.IsType<ContentIndexModRepository>(services.ContentIndex);
        Assert.IsAssignableFrom<IContentIndexRepository>(services.ContentIndex);
    }

    [Fact]
    public async Task ContentIndex_CurrentSnapshotFixture_IsUsableThroughTheRepository()
    {
        using var services = await BoreaServices.BuildAsync(_tempRoot);
        var indexPath = services.Paths.GetIndexPath();
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Index", "Fixtures", "current-snapshot.json"),
            indexPath);
        var repository = new ContentIndexModRepository(
            new CachedIndexFetcher(),
            services.IndexReader,
            services.Paths);

        var available = await repository.GetAvailableModsAsync();
        var latest = await repository.GetLatestReleaseAsync("AdvancedFlightComputer");
        var diagnostics = await repository.GetDiagnosticsAsync();

        Assert.Equal(4, available.Count);
        Assert.Contains(available, mod => mod.ModId == "StarMap" && mod.Type == ContentType.ModLoader);
        Assert.Equal(ModVersion.Parse("0.7.5"), latest!.Version);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Mods_ControlledSnapshot_ResolvesStarMapThroughTheProductionComposite()
    {
        var snapshot = await File.ReadAllTextAsync(SnapshotFixturePath);
        var handler = new ControlledHttpMessageHandler(snapshot);
        using var services = await BoreaServices.BuildAsync(_tempRoot, handler, new ConflictingStarMapRepository());

        var available = await services.Mods.GetAvailableModsAsync();
        var starMap = Assert.Single(available, mod => mod.ModId == "StarMap");
        var versions = await services.Mods.GetAvailableVersionsAsync("StarMap");
        var latest = await services.Mods.GetLatestReleaseAsync("StarMap");

        Assert.Equal(ContentIndexModRepository.SourceName, starMap.Source);
        Assert.Equal(ContentType.ModLoader, starMap.Type);
        Assert.Equal(InstallAnchor.Standalone, starMap.Install!.Target);
        Assert.Equal("StarMap.exe", starMap.Provides!.Launch);
        Assert.Equal(InstallAnchor.Mods, starMap.Provides.ContentDir);
        Assert.Equal("StarMapConfig.json", starMap.Provides.Configure!.File);
        Assert.Equal(ConfigureFormat.Json, starMap.Provides.Configure.Format);
        Assert.Equal("GameLocation", starMap.Provides.Configure.GamePath);
        Assert.Equal([ModVersion.Parse("0.4.6")], versions);
        Assert.NotNull(latest);
        Assert.Equal(ModVersion.Parse("0.4.6"), latest.Version);
        Assert.Equal(ContentIndexModRepository.SourceName, latest.Source);
        Assert.Equal(InstallAnchor.Standalone, latest.Install!.Target);
        Assert.Equal(
            ["https://ksamodding.github.io/content-index-releases/v1/index.json"],
            handler.RequestUris.Select(uri => uri.AbsoluteUri));
    }

    [Fact]
    public async Task GameDirectoryChanger_ControlledSnapshot_UpdatesStarMapAndSettings()
    {
        var oldGame = Path.Combine(_tempRoot, "OldGame");
        var newGame = Path.Combine(_tempRoot, "NewGame");
        var loaderDirectory = Path.Combine(_tempRoot, "StarMap");
        var configurationPath = Path.Combine(loaderDirectory, "StarMapConfig.json");
        Directory.CreateDirectory(loaderDirectory);
        await File.WriteAllTextAsync(configurationPath, """
            {
              "GameLocation": "old",
              "Keep": true
            }
            """);
        await SaveAsync(new BoreaSettings(oldGame, LoaderAt(loaderDirectory)));
        var snapshot = await File.ReadAllTextAsync(SnapshotFixturePath);

        using var services = await BoreaServices.BuildAsync(
            _tempRoot,
            new ControlledHttpMessageHandler(snapshot),
            new ConflictingStarMapRepository());
        await services.GameDirectoryChanger.ChangeAsync(newGame);

        using var configuration = JsonDocument.Parse(await File.ReadAllTextAsync(configurationPath));
        Assert.Equal(newGame, configuration.RootElement.GetProperty("GameLocation").GetString());
        Assert.True(configuration.RootElement.GetProperty("Keep").GetBoolean());
        Assert.Equal(newGame, (await services.SettingsRepository.GetAsync())!.GameDirectoryPath);
    }

    [Fact]
    public async Task GameDirectoryChanger_InvalidLoaderConfiguration_RestoresFileAndSettings()
    {
        var oldGame = Path.Combine(_tempRoot, "OldGame");
        var newGame = Path.Combine(_tempRoot, "NewGame");
        var loaderDirectory = Path.Combine(_tempRoot, "StarMap");
        var configurationPath = Path.Combine(loaderDirectory, "StarMapConfig.json");
        const string originalConfiguration = "[]";
        Directory.CreateDirectory(loaderDirectory);
        await File.WriteAllTextAsync(configurationPath, originalConfiguration);
        await SaveAsync(new BoreaSettings(oldGame, LoaderAt(loaderDirectory)));
        var snapshot = await File.ReadAllTextAsync(SnapshotFixturePath);

        using var services = await BoreaServices.BuildAsync(
            _tempRoot,
            new ControlledHttpMessageHandler(snapshot),
            new ConflictingStarMapRepository());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => services.GameDirectoryChanger.ChangeAsync(newGame));
        Assert.Equal(originalConfiguration, await File.ReadAllTextAsync(configurationPath));
        Assert.Equal(oldGame, (await services.SettingsRepository.GetAsync())!.GameDirectoryPath);
    }

    [Fact]
    public async Task InstalledVersion_ReadsTheGameDirectoryTheSettingsName()
    {
        using var services = await BoreaServices.BuildAsync(_tempRoot);

        Assert.IsType<InstalledGameVersionProvider>(services.InstalledVersion);
        // No game directory is set, so there is nothing to read.
        Assert.Null(services.InstalledVersion.GetInstalledVersion());
    }

    [Fact]
    public async Task Dispose_ClosesTheOneClientEveryNetworkServiceUses()
    {
        var services = await BoreaServices.BuildAsync(_tempRoot);

        services.Dispose();

        // A disposed client refuses a request before it reaches any host, so each
        // probe proves that the service holds the shared client and sends nothing.
        await Assert.ThrowsAsync<ObjectDisposedException>(() => services.LatestVersion.PingAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => services.Mods.GetAvailableModsAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => services.Downloader.DownloadAsync(Release(), Path.Combine(_tempRoot, "probe.zip")));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => services.IndexFetcher.FetchAsync(services.Paths.GetIndexPath()));
    }

    /// <summary>The least a release needs to reach the client, which is all the
    /// disposal probe above asks of it.</summary>
    private static ModVersionMetadata Release() => new(
        specVersion: 1,
        modId: "ModA",
        version: ModVersion.Parse("1.0.0"),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        gameMin: "2026.7.4.2131",
        gameMinRevision: 2131,
        download: new DownloadInfo("https://example.invalid/ModA.zip", null, null, "application/zip"),
        installSizeBytes: null,
        dependencies: Array.Empty<ModDependency>());

    private string SettingsPath => new GamePathProvider(gameDirectory: null, boreaRoot: _tempRoot).GetBoreaSettingsPath();

    private static string SnapshotFixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Index", "Fixtures", "current-snapshot.json");

    private static Dictionary<string, LoaderInstallation> LoaderAt(string path) => new()
    {
        ["StarMap"] = new LoaderInstallation(path, ModVersion.Parse("0.4.6"), "0.4.6.0", isAdopted: true),
    };

    private Task SaveAsync(BoreaSettings settings)
        => new FileBoreaSettingsRepository(new GamePathProvider(gameDirectory: null, boreaRoot: _tempRoot)).SaveAsync(settings);

    private sealed class CachedIndexFetcher : IContentIndexFetcher
    {
        public Task<ContentIndexFetchResult> FetchAsync(
            string destinationPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ContentIndexFetchResult.NotModified);
    }

    private sealed class ControlledHttpMessageHandler(string snapshot) : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request.RequestUri);
            RequestUris.Add(request.RequestUri);

            if (!request.RequestUri.AbsoluteUri.StartsWith(
                "https://ksamodding.github.io/content-index-releases/",
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected request to {request.RequestUri}.");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(snapshot, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ConflictingStarMapRepository : IModRepository
    {
        private static readonly ModVersion Version = ModVersion.Parse("9.9.9");

        private static readonly ModMetadata Listing = new(
            specVersion: 1,
            modId: "StarMap",
            source: "fallback",
            name: "Fallback StarMap",
            authors: ["Fallback"],
            abstractText: "Fallback listing.",
            license: "MIT",
            links: new Dictionary<string, string>
            {
                ["forums"] = "https://forums.ahwoo.com/threads/fallback.1/",
            },
            gameMin: "2026.1",
            type: ContentType.ModLoader);

        private static readonly ModVersionMetadata Release = new(
            specVersion: 1,
            modId: "StarMap",
            version: Version,
            releaseStatus: ReleaseStatus.Stable,
            releaseDate: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            gameMin: "2026.1.1.1",
            gameMinRevision: 1,
            download: new DownloadInfo("https://example.invalid/fallback.zip", null, null, "application/zip"),
            installSizeBytes: null,
            dependencies: [],
            type: ContentType.ModLoader,
            source: "fallback");

        public Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModMetadata>>([Listing]);

        public Task<ModVersionMetadata?> GetLatestReleaseAsync(
            string modId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ModVersionMetadata?>(ModIds.Equals(modId, Listing.ModId) ? Release : null);

        public Task<ModVersionMetadata?> GetReleaseAsync(
            string modId,
            ModVersion version,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ModVersionMetadata?>(
                ModIds.Equals(modId, Listing.ModId) && version.Equals(Version) ? Release : null);

        public Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(
            string modId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModVersion>>(
                ModIds.Equals(modId, Listing.ModId) ? [Version] : []);

        public Task<IReadOnlyList<ModMetadata>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModMetadata>>(
                Listing.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ? [Listing] : []);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
