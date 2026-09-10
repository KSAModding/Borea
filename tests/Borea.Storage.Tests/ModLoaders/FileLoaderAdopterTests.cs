using System.Text.Json;
using Borea.Core.Dependencies;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Settings;
using Borea.Storage.ModLoaders;
using Borea.Storage.Settings;
using Borea.Storage.Tests.Mods;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.ModLoaders;

public sealed class FileLoaderAdopterTests : IDisposable
{
    private const string LoaderVersionFixture = "LoaderVersionFixture.dll";

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly FileBoreaSettingsRepository _settings;
    private readonly FileLoaderAdopter _adopter;

    public FileLoaderAdopterTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _settings = new FileBoreaSettingsRepository(_paths);
        _adopter = new FileLoaderAdopter(_settings, new LoaderConfigurator());
    }

    private string GameDirectory => Path.Combine(_tempRoot, "Game");

    private string LoaderDirectory => Path.Combine(_tempRoot, "Manual StarMap");

    [Fact]
    public async Task AdoptAsync_RealStarMapLayout_MatchesAndRecordsTheRelease()
    {
        await _settings.SaveAsync(new BoreaSettings(GameDirectory));
        PlaceStarMap(GameDirectory);
        var configBefore = await File.ReadAllBytesAsync(Path.Combine(LoaderDirectory, "StarMapConfig.json"));

        var result = await _adopter.AdoptAsync(StarMap(), new[] { StarMapRelease() }, LoaderDirectory);

        Assert.Equal("0.4.6.0", result.RawVersion);
        Assert.Equal(ModVersion.Parse("0.4.6"), result.Version);
        Assert.Equal(GameDirectory, result.ConfiguredGameDirectory);
        Assert.True(result.GameDirectoryMatches is true);
        Assert.Empty(result.Warnings);
        Assert.Equal(configBefore, await File.ReadAllBytesAsync(Path.Combine(LoaderDirectory, "StarMapConfig.json")));

        var installation = (await _settings.GetAsync())!.LoaderInstallations["StarMap"];
        Assert.Equal(LoaderDirectory, installation.DirectoryPath);
        Assert.Equal(ModVersion.Parse("0.4.6"), installation.Version);
        Assert.Equal("0.4.6.0", installation.RawVersion);
        Assert.True(installation.IsAdopted);
    }

    [Fact]
    public async Task AdoptAsync_LaunchExecutableMissing_RefusesWithoutARecord()
    {
        Directory.CreateDirectory(LoaderDirectory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _adopter.AdoptAsync(StarMap(), new[] { StarMapRelease() }, LoaderDirectory));

        Assert.Contains("StarMap.exe", exception.Message);
        Assert.Null(await _settings.GetAsync());
    }

    [Fact]
    public async Task AdoptAsync_FileVersionHasNoIndexedRelease_RecordsUnknownWithAWarning()
    {
        PlaceStarMap(GameDirectory);

        var result = await _adopter.AdoptAsync(StarMap(), Array.Empty<ModVersionMetadata>(), LoaderDirectory);

        Assert.Equal("0.4.6.0", result.RawVersion);
        Assert.Null(result.Version);
        Assert.Contains(result.Warnings, warning => warning.Contains("does not match an indexed release", StringComparison.Ordinal));

        var installation = (await _settings.GetAsync())!.LoaderInstallations["StarMap"];
        Assert.Null(installation.Version);
        Assert.True(installation.IsAdopted);
    }

    [Fact]
    public async Task AdoptAsync_ConfiguredForAnotherGame_WarnsAndDoesNotRewriteTheFile()
    {
        await _settings.SaveAsync(new BoreaSettings(GameDirectory));
        var otherGame = Path.Combine(_tempRoot, "Other Game");
        PlaceStarMap(otherGame);
        var configPath = Path.Combine(LoaderDirectory, "StarMapConfig.json");
        var before = await File.ReadAllBytesAsync(configPath);

        var result = await _adopter.AdoptAsync(StarMap(), new[] { StarMapRelease() }, LoaderDirectory);

        Assert.Equal(otherGame, result.ConfiguredGameDirectory);
        Assert.True(result.GameDirectoryMatches is false);
        Assert.Contains(result.Warnings, warning => warning.Contains("was not changed", StringComparison.Ordinal));
        Assert.Equal(before, await File.ReadAllBytesAsync(configPath));
    }

    [Fact]
    public async Task AdoptAsync_EmptyConfiguredGamePath_WarnsThatItDoesNotMatch()
    {
        await _settings.SaveAsync(new BoreaSettings(GameDirectory));
        PlaceStarMap(string.Empty);

        var result = await _adopter.AdoptAsync(StarMap(), new[] { StarMapRelease() }, LoaderDirectory);

        Assert.Equal(string.Empty, result.ConfiguredGameDirectory);
        Assert.True(result.GameDirectoryMatches is false);
        Assert.Contains(result.Warnings, warning => warning.Contains("was not changed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AdoptAsync_ConfiguredGamePathCannotBeRead_WarnsAndRecordsTheLoader()
    {
        await _settings.SaveAsync(new BoreaSettings(GameDirectory));
        PlaceStarMap(GameDirectory);
        File.Delete(Path.Combine(LoaderDirectory, "StarMapConfig.json"));

        var result = await _adopter.AdoptAsync(StarMap(), new[] { StarMapRelease() }, LoaderDirectory);

        Assert.Null(result.ConfiguredGameDirectory);
        Assert.Null(result.GameDirectoryMatches);
        Assert.Contains(result.Warnings, warning => warning.Contains("could not confirm", StringComparison.Ordinal));
        Assert.True((await _settings.GetAsync())!.LoaderInstallations["StarMap"].IsAdopted);
    }

    [Fact]
    public async Task AdoptAsync_RecordedAtAnotherDirectory_Refuses()
    {
        PlaceStarMap(GameDirectory);
        var recorded = new LoaderInstallation(Path.Combine(_tempRoot, "Elsewhere"), null, null, isAdopted: true);
        await _settings.SaveAsync(new BoreaSettings(
            GameDirectory,
            loaderInstallations: new Dictionary<string, LoaderInstallation> { ["StarMap"] = recorded }));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _adopter.AdoptAsync(StarMap(), new[] { StarMapRelease() }, LoaderDirectory));
    }

    [Fact]
    public async Task AdoptAsync_RelativeDirectory_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _adopter.AdoptAsync(StarMap(), new[] { StarMapRelease() }, "StarMap"));
    }

    [Fact]
    public void Constructor_NullDependency_ThrowsArgumentNullException()
    {
        var configuration = new LoaderConfigurator();

        Assert.Throws<ArgumentNullException>(() => new FileLoaderAdopter(null!, configuration));
        Assert.Throws<ArgumentNullException>(() => new FileLoaderAdopter(_settings, null!));
    }

    private void PlaceStarMap(string configuredGameDirectory)
    {
        Directory.CreateDirectory(LoaderDirectory);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, LoaderVersionFixture),
            Path.Combine(LoaderDirectory, "StarMap.exe"));
        File.WriteAllText(Path.Combine(LoaderDirectory, "StarMap.dll"), "loader");
        File.WriteAllText(
            Path.Combine(LoaderDirectory, "StarMapConfig.json"),
            JsonSerializer.Serialize(new
            {
                GameLocation = configuredGameDirectory,
                RepositoryLocation = "C:\\Repositories",
            }));
    }

    private static ModMetadata StarMap() => new(
        specVersion: 1,
        modId: "StarMap",
        source: "index",
        name: "StarMap",
        authors: new[] { "KlaasWhite" },
        abstractText: "The loader.",
        license: "MIT",
        links: MetadataFixtures.SampleLinks(),
        gameMin: "2026.8.3.5117",
        type: ContentType.ModLoader,
        install: new InstallDescriptor(target: InstallAnchor.Standalone),
        provides: new LoaderProvides(
            launch: "StarMap.exe",
            contentDir: InstallAnchor.Mods,
            configure: new LoaderConfigure("StarMapConfig.json", ConfigureFormat.Json, "GameLocation")));

    private static ModVersionMetadata StarMapRelease() => new(
        specVersion: 1,
        modId: "StarMap",
        version: ModVersion.Parse("0.4.6"),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: new DateTimeOffset(2026, 8, 2, 16, 47, 50, TimeSpan.Zero),
        gameMin: "2026.8.3.5117",
        gameMinRevision: 5117,
        download: new DownloadInfo("https://example.invalid/StarMap.zip", null, null, "application/zip"),
        installSizeBytes: null,
        dependencies: Array.Empty<ModDependency>(),
        type: ContentType.ModLoader,
        install: new InstallInfo(null, derived: true, target: InstallAnchor.Standalone));

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

}
