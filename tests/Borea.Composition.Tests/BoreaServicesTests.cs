using Borea.Core.Mods;
using Borea.Core.Settings;
using Borea.Network.Sources;
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
        Assert.Empty(services.Settings.LoaderDirectoryPaths);
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
        await SaveAsync(new BoreaSettings(null, new Dictionary<string, string> { ["StarMap"] = StarMapPath }));

        using var services = await BoreaServices.BuildAsync(_tempRoot);

        Assert.Null(services.Paths.GetGameDirectoryPath());
        Assert.Equal(StarMapPath, services.Paths.GetLoaderDirectoryPath("StarMap"));
    }

    [Fact]
    public async Task BuildAsync_FullSettings_KnowsTheGameAndTheLoader()
    {
        await SaveAsync(new BoreaSettings(GamePath, new Dictionary<string, string> { ["StarMap"] = StarMapPath }));

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
            [LoaderDirectoryPaths]
            StarMap = 'C:\Games\StarMap'
            starmap = 'C:\Games\Other'
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
    public async Task Dispose_ClosesTheOneClientEveryNetworkServiceUses()
    {
        var services = await BoreaServices.BuildAsync(_tempRoot);

        services.Dispose();

        // A disposed client refuses a request before it reaches any host, so each
        // probe proves that the service holds the shared client and sends nothing.
        await Assert.ThrowsAsync<ObjectDisposedException>(() => services.LatestVersion.PingAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => services.Mods.GetAvailableModsAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => services.Downloader.DownloadAsync("1", new ModVersion(1, 0, 0), _tempRoot));
    }

    private string SettingsPath => new GamePathProvider(gameDirectory: null, boreaRoot: _tempRoot).GetBoreaSettingsPath();

    private Task SaveAsync(BoreaSettings settings)
        => new FileBoreaSettingsRepository(new GamePathProvider(gameDirectory: null, boreaRoot: _tempRoot)).SaveAsync(settings);

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
