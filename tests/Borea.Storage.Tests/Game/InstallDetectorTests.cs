using System.Text.Json;
using Borea.Core.Game;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Storage.Game;
using Borea.Storage.ModLoaders;
using Borea.Storage.Settings;
using Borea.Storage.Tests.Mods;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Game;

public sealed class InstallDetectorTests : IDisposable
{
    private const string RealBuildFixture = "GameVersionFixture.dll";
    private const string ForeignBuildFixture = "UnparseableVersionFixture.dll";
    private const string LoaderVersionFixture = "LoaderVersionFixture.dll";

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly string _loadersRoot;
    private readonly FileBoreaSettingsRepository _settings;
    private readonly FakeCandidates _candidates = new();
    private readonly InstallDetector _detector;

    public InstallDetectorTests()
    {
        var paths = new TestGamePathProvider(_tempRoot);
        _loadersRoot = paths.GetLoadersRoot();
        _settings = new FileBoreaSettingsRepository(paths);
        _detector = new InstallDetector(_candidates, new FileLoaderAdopter(_settings, new LoaderConfigurator()), _loadersRoot);
    }

    [Fact]
    public async Task DetectAsync_NoCandidate_FindsNothing()
    {
        var detection = await _detector.DetectAsync([StarMap()]);

        Assert.Empty(detection.Games);
        Assert.Empty(detection.Loaders);
    }

    [Fact]
    public async Task DetectAsync_OneGame_ReturnsItsVersion()
    {
        var game = PlaceGame("KSA", RealBuildFixture);
        _candidates.Games.Add(game);

        var detection = await _detector.DetectAsync([]);

        var found = Assert.Single(detection.Games);
        Assert.Equal(game, found.Directory);
        Assert.Equal("2026.8.3.5117", found.Version.RawVersion);
    }

    [Fact]
    public async Task DetectAsync_TwoGames_ReturnsBothWithTheirVersions()
    {
        var first = PlaceGame("KSA", RealBuildFixture);
        var second = PlaceGame("KSA Old", ForeignBuildFixture);
        _candidates.Games.AddRange([first, second, first + Path.DirectorySeparatorChar]);

        var detection = await _detector.DetectAsync([]);

        Assert.Collection(
            detection.Games,
            game => Assert.Equal((first, "2026.8.3.5117"), (game.Directory, game.Version.RawVersion)),
            game => Assert.Equal((second, "1.0.0.0"), (game.Directory, game.Version.RawVersion)));
    }

    [Fact]
    public async Task DetectAsync_RegistryPathThatNoLongerExists_IsNotShown()
    {
        _candidates.Games.Add(Path.Combine(_tempRoot, "Uninstalled"));
        _candidates.Loaders.Add(Path.Combine(_tempRoot, "Removed StarMap"));

        var detection = await _detector.DetectAsync([StarMap()]);

        Assert.Empty(detection.Games);
        Assert.Empty(detection.Loaders);
    }

    [Fact]
    public async Task DetectAsync_FolderWithoutTheGameOrItsVersion_IsNotShown()
    {
        var noExecutable = Directory.CreateDirectory(Path.Combine(_tempRoot, "No exe")).FullName;
        File.Copy(Path.Combine(AppContext.BaseDirectory, RealBuildFixture), Path.Combine(noExecutable, "KSA.dll"));
        var noVersion = Directory.CreateDirectory(Path.Combine(_tempRoot, "No version")).FullName;
        File.WriteAllText(Path.Combine(noVersion, "KSA.exe"), "game");
        File.WriteAllText(Path.Combine(noVersion, "KSA.dll"), "not a portable executable");
        _candidates.Games.AddRange([noExecutable, noVersion, "relative"]);

        var detection = await _detector.DetectAsync([]);

        Assert.Empty(detection.Games);
    }

    [Fact]
    public async Task DetectAsync_StarMap_IsFoundAndOffersItsConfiguredGame()
    {
        var game = PlaceGame("KSA", RealBuildFixture);
        var loader = PlaceStarMap("StarMap", game);
        _candidates.Loaders.Add(Directory.CreateDirectory(Path.Combine(_tempRoot, "Empty mod")).FullName);
        _candidates.Loaders.Add(loader);

        var detection = await _detector.DetectAsync([StarMap()]);

        var found = Assert.Single(detection.Loaders);
        Assert.Equal(("StarMap", loader, "0.4.6.0"), (found.LoaderId, found.Directory, found.RawVersion));
        Assert.Equal(game, Assert.Single(detection.Games).Directory);
        Assert.Null(await _settings.GetAsync());
    }

    [Fact]
    public async Task DetectAsync_StarMapInBoreasLoaderFolder_IsFoundWithoutACandidate()
    {
        var game = PlaceGame("KSA", RealBuildFixture);
        var loader = PlaceStarMap(Path.Combine(_loadersRoot, "StarMap"), game);

        var detection = await _detector.DetectAsync([StarMap()]);

        Assert.Equal(loader, Assert.Single(detection.Loaders).Directory);
        Assert.Equal(game, Assert.Single(detection.Games).Directory);
    }

    [Fact]
    public async Task DetectAsync_StarMapConfiguredWithTheGameAssembly_OffersTheGameFolder()
    {
        var game = PlaceGame("KSA", RealBuildFixture);
        _candidates.Loaders.Add(PlaceStarMap("StarMap", Path.Combine(game, "KSA.dll")));

        var detection = await _detector.DetectAsync([StarMap()]);

        Assert.Equal(game, Assert.Single(detection.Games).Directory);
    }

    private string PlaceGame(string name, string fixture)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_tempRoot, name)).FullName;
        File.WriteAllText(Path.Combine(directory, "KSA.exe"), "game");
        File.Copy(Path.Combine(AppContext.BaseDirectory, fixture), Path.Combine(directory, "KSA.dll"));
        return directory;
    }

    private string PlaceStarMap(string name, string configuredGameLocation)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_tempRoot, name)).FullName;
        File.Copy(Path.Combine(AppContext.BaseDirectory, LoaderVersionFixture), Path.Combine(directory, "StarMap.exe"));
        File.WriteAllText(
            Path.Combine(directory, "StarMapConfig.json"),
            JsonSerializer.Serialize(new { GameLocation = configuredGameLocation }));
        return directory;
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

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private sealed class FakeCandidates : IInstallCandidateSource
    {
        public List<string> Games { get; } = [];

        public List<string> Loaders { get; } = [];

        public IReadOnlyList<string> GetGameDirectories() => Games;

        public IReadOnlyList<string> GetLoaderDirectories() => Loaders;
    }
}
