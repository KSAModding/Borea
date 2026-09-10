using Borea.Core.Dependencies;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Settings;
using Borea.Network.Index;
using Borea.Network.Sources;
using Borea.Storage.Game;
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
    }

    [Fact]
    public async Task IndexFetcher_IsTheNetworkFetcher()
    {
        using var services = await BoreaServices.BuildAsync(_tempRoot);

        Assert.IsType<ContentIndexFetcher>(services.IndexFetcher);
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

    private static Dictionary<string, LoaderInstallation> LoaderAt(string path) => new()
    {
        ["StarMap"] = new LoaderInstallation(path, ModVersion.Parse("0.4.6"), "0.4.6.0", isAdopted: true),
    };

    private Task SaveAsync(BoreaSettings settings)
        => new FileBoreaSettingsRepository(new GamePathProvider(gameDirectory: null, boreaRoot: _tempRoot)).SaveAsync(settings);

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
