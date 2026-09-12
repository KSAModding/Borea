using System.Text.Json.Nodes;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Settings;
using Borea.Storage.ModLoaders;
using Borea.Storage.Paths;
using Borea.Storage.Settings;

namespace Borea.Storage.Tests.Settings;

public sealed class GameDirectoryChangerTests : IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> Links = new Dictionary<string, string>
    {
        ["forums"] = "https://forums.ahwoo.com/threads/loader.1/",
    };

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());

    [Fact]
    public async Task ChangeAsync_OneConfiguredLoader_UpdatesTheFileAndSettings()
    {
        var oldGame = Path.Combine(_tempRoot, "OldGame");
        var newGame = Path.Combine(_tempRoot, "NewGame");
        var loaderDirectory = Path.Combine(_tempRoot, "Loaders", "StarMap");
        var configPath = Path.Combine(loaderDirectory, "loader.json");
        Directory.CreateDirectory(loaderDirectory);
        await File.WriteAllTextAsync(configPath, """
            {
              "Keep": true,
              "GameLocation": "old"
            }
            """);

        var settings = SettingsRepository();
        await settings.SaveAsync(new BoreaSettings(oldGame, Installations(("StarMap", loaderDirectory))));
        var changer = new GameDirectoryChanger(settings, new FakeModRepository(Loader("StarMap")), new LoaderConfigurator());

        await changer.ChangeAsync(newGame);

        var json = (JsonObject)JsonNode.Parse(await File.ReadAllTextAsync(configPath))!;
        Assert.True(json["Keep"]!.GetValue<bool>());
        Assert.Equal(newGame, json["GameLocation"]!.GetValue<string>());
        Assert.Equal(newGame, (await settings.GetAsync())!.GameDirectoryPath);
    }

    [Fact]
    public async Task ChangeAsync_LoaderWithoutGamePath_LeavesItsFileUnchanged()
    {
        var oldGame = Path.Combine(_tempRoot, "OldGame");
        var newGame = Path.Combine(_tempRoot, "NewGame");
        var loaderDirectory = Path.Combine(_tempRoot, "Loaders", "Other");
        var configPath = Path.Combine(loaderDirectory, "loader.json");
        const string original = "{ \"Keep\": true }";
        Directory.CreateDirectory(loaderDirectory);
        await File.WriteAllTextAsync(configPath, original);

        var settings = SettingsRepository();
        await settings.SaveAsync(new BoreaSettings(oldGame, Installations(("Other", loaderDirectory))));
        var listing = Loader("Other", new LoaderConfigure("loader.json", ConfigureFormat.Json));
        var changer = new GameDirectoryChanger(settings, new FakeModRepository(listing), new LoaderConfigurator());

        await changer.ChangeAsync(newGame);

        Assert.Equal(original, await File.ReadAllTextAsync(configPath));
        Assert.Equal(newGame, (await settings.GetAsync())!.GameDirectoryPath);
    }

    [Fact]
    public async Task ChangeAsync_SecondLoaderFails_RestoresEarlierFileAndSettings()
    {
        var oldGame = Path.Combine(_tempRoot, "OldGame");
        var newGame = Path.Combine(_tempRoot, "NewGame");
        var firstDirectory = Path.Combine(_tempRoot, "Loaders", "First");
        var secondDirectory = Path.Combine(_tempRoot, "Loaders", "Second");
        var firstConfig = Path.Combine(firstDirectory, "loader.json");
        var secondConfig = Path.Combine(secondDirectory, "loader.json");
        const string firstOriginal = "{ \"GameLocation\": \"old\", \"Keep\": 1 }";
        const string secondOriginal = "[]";
        Directory.CreateDirectory(firstDirectory);
        Directory.CreateDirectory(secondDirectory);
        await File.WriteAllTextAsync(firstConfig, firstOriginal);
        await File.WriteAllTextAsync(secondConfig, secondOriginal);

        var settings = SettingsRepository();
        await settings.SaveAsync(new BoreaSettings(
            oldGame,
            Installations(("First", firstDirectory), ("Second", secondDirectory))));
        var changer = new GameDirectoryChanger(
            settings,
            new FakeModRepository(Loader("First"), Loader("Second")),
            new LoaderConfigurator());

        await Assert.ThrowsAsync<InvalidOperationException>(() => changer.ChangeAsync(newGame));

        Assert.Equal(firstOriginal, await File.ReadAllTextAsync(firstConfig));
        Assert.Equal(secondOriginal, await File.ReadAllTextAsync(secondConfig));
        Assert.Equal(oldGame, (await settings.GetAsync())!.GameDirectoryPath);
    }

    [Fact]
    public async Task ChangeAsync_SettingsSaveFails_RestoresTheLoaderAndSettings()
    {
        var oldGame = Path.Combine(_tempRoot, "OldGame");
        var newGame = Path.Combine(_tempRoot, "NewGame");
        var loaderDirectory = Path.Combine(_tempRoot, "Loaders", "StarMap");
        var configPath = Path.Combine(loaderDirectory, "loader.json");
        const string original = "{ \"GameLocation\": \"old\" }";
        Directory.CreateDirectory(loaderDirectory);
        await File.WriteAllTextAsync(configPath, original);

        var previous = new BoreaSettings(oldGame, Installations(("StarMap", loaderDirectory)));
        var settings = new FailingOnceSettingsRepository(previous);
        var changer = new GameDirectoryChanger(settings, new FakeModRepository(Loader("StarMap")), new LoaderConfigurator());

        await Assert.ThrowsAsync<IOException>(() => changer.ChangeAsync(newGame));

        Assert.Equal(original, await File.ReadAllTextAsync(configPath));
        Assert.Equal(oldGame, settings.Current.GameDirectoryPath);
    }

    [Fact]
    public async Task ChangeAsync_SettingsRollbackFails_ReportsTheInconsistentState()
    {
        var oldGame = Path.Combine(_tempRoot, "OldGame");
        var newGame = Path.Combine(_tempRoot, "NewGame");
        var loaderDirectory = Path.Combine(_tempRoot, "Loaders", "StarMap");
        var configPath = Path.Combine(loaderDirectory, "loader.json");
        const string original = "{ \"GameLocation\": \"old\" }";
        Directory.CreateDirectory(loaderDirectory);
        await File.WriteAllTextAsync(configPath, original);

        var previous = new BoreaSettings(oldGame, Installations(("StarMap", loaderDirectory)));
        var settings = new FailingSettingsRepository(previous);
        var changer = new GameDirectoryChanger(settings, new FakeModRepository(Loader("StarMap")), new LoaderConfigurator());

        var exception = await Assert.ThrowsAsync<AggregateException>(() => changer.ChangeAsync(newGame));

        Assert.Contains("rollback could not restore a consistent state", exception.Message);
        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Equal(original, await File.ReadAllTextAsync(configPath));
        Assert.Equal(newGame, settings.Current.GameDirectoryPath);
    }

    private FileBoreaSettingsRepository SettingsRepository() =>
        new(new GamePathProvider(gameDirectory: null, boreaRoot: _tempRoot));

    private static IReadOnlyDictionary<string, LoaderInstallation> Installations(
        params (string Id, string Directory)[] loaders) =>
        loaders.ToDictionary(
            loader => loader.Id,
            loader => new LoaderInstallation(loader.Directory, version: null, rawVersion: null, isAdopted: false),
            ModIds.Comparer);

    private static ModMetadata Loader(string id, LoaderConfigure? configure = null) => new(
        specVersion: 1,
        modId: id,
        source: "index",
        name: id,
        authors: new[] { "Author" },
        abstractText: "A loader.",
        license: "MIT",
        links: Links,
        gameMin: "2026.8.3.5117",
        type: ContentType.ModLoader,
        install: new InstallDescriptor(target: InstallAnchor.Standalone),
        provides: new LoaderProvides(
            "loader.exe",
            InstallAnchor.Mods,
            configure: configure ?? new LoaderConfigure("loader.json", ConfigureFormat.Json, "GameLocation")));

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private sealed class FakeModRepository(params ModMetadata[] listings) : IModRepository
    {
        public Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModMetadata>>(listings);

        public Task<ModVersionMetadata?> GetLatestReleaseAsync(string modId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ModVersionMetadata?>(null);

        public Task<ModVersionMetadata?> GetReleaseAsync(
            string modId,
            ModVersion version,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ModVersionMetadata?>(null);

        public Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(
            string modId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModVersion>>(Array.Empty<ModVersion>());

        public Task<IReadOnlyList<ModMetadata>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModMetadata>>(listings);
    }

    private sealed class FailingOnceSettingsRepository(BoreaSettings current) : IBoreaSettingsRepository
    {
        private bool _failed;

        public BoreaSettings Current { get; private set; } = current;

        public Task<BoreaSettings?> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<BoreaSettings?>(Current);

        public Task SaveAsync(BoreaSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            if (!_failed)
            {
                _failed = true;
                throw new IOException("Settings save failed.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FailingSettingsRepository(BoreaSettings current) : IBoreaSettingsRepository
    {
        private int _saveCount;

        public BoreaSettings Current { get; private set; } = current;

        public Task<BoreaSettings?> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<BoreaSettings?>(Current);

        public Task SaveAsync(BoreaSettings settings, CancellationToken cancellationToken = default)
        {
            _saveCount++;
            if (_saveCount == 1)
                Current = settings;
            throw new IOException("Settings save failed.");
        }
    }
}
