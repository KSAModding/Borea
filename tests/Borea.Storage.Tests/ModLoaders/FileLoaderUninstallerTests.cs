using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Settings;
using Borea.Storage.ModLoaders;
using Borea.Storage.Settings;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.ModLoaders;

public sealed class FileLoaderUninstallerTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly ObservedSettingsRepository _settings;
    private readonly FileLoaderVersionStore _store;
    private readonly FileLoaderUninstaller _uninstaller;
    private bool _gameRunning;

    public FileLoaderUninstallerTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _settings = new ObservedSettingsRepository(new FileBoreaSettingsRepository(_paths));
        _store = new FileLoaderVersionStore(_settings, _paths);
        _uninstaller = new FileLoaderUninstaller(_settings, _paths, _store, () => _gameRunning);
    }

    private string LoaderFolder => Path.Combine(_paths.GetLoadersRoot(), "StarMap");

    [Fact]
    public async Task UninstallAsync_AdoptedLoader_PreservesDirectoryAndRemovesRecord()
    {
        var directory = await RecordAsync(isAdopted: true);
        var file = Path.Combine(directory, "user-file.txt");
        await File.WriteAllTextAsync(file, "keep");

        var result = await _uninstaller.UninstallAsync("StarMap");

        Assert.True(result.RecordRemoved);
        Assert.False(result.DirectoryRemoved);
        Assert.True(File.Exists(file));
        Assert.Empty((await _settings.GetAsync())!.LoaderInstallations);
    }

    [Fact]
    public async Task UninstallAsync_KeepsTheReleaseChannel()
    {
        await RecordAsync(isAdopted: true);
        var recorded = await _settings.GetAsync();
        await _settings.SaveAsync(recorded!.WithReleaseChannel(ReleaseChannel.Testing));

        await _uninstaller.UninstallAsync("StarMap");

        var settings = await _settings.GetAsync();
        Assert.Empty(settings!.LoaderInstallations);
        Assert.Equal(ReleaseChannel.Testing, settings.ReleaseChannel);
    }

    [Fact]
    public async Task UninstallAsync_BoreaInstalledLoader_RemovesDirectoryAndRecord()
    {
        var directory = await RecordAsync(isAdopted: false);
        await File.WriteAllTextAsync(Path.Combine(directory, "StarMap.exe"), "loader");

        var result = await _uninstaller.UninstallAsync("starmap");

        Assert.True(result.RecordRemoved);
        Assert.True(result.DirectoryRemoved);
        Assert.False(Directory.Exists(directory));
        Assert.Empty((await _settings.GetAsync())!.LoaderInstallations);
    }

    [Fact]
    public async Task UninstallAsync_UnknownLoader_IsNoOp()
    {
        var result = await _uninstaller.UninstallAsync("StarMap");

        Assert.False(result.RecordRemoved);
        Assert.False(result.DirectoryRemoved);
        Assert.Null(result.Directory);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a valid id")]
    public async Task UninstallAsync_InvalidLoaderId_ThrowsArgumentException(string loaderId)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _uninstaller.UninstallAsync(loaderId));
    }

    [Fact]
    public void Constructor_NullSettings_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new FileLoaderUninstaller(null!, _paths, _store));
    }

    [Fact]
    public async Task UninstallVersionAsync_RecordedVersion_DeletesOnlyItAndPointsTheRecordAtTheOther()
    {
        var kept = Folder(Path.Combine(LoaderFolder, "0.4.7"));
        await RecordAsync(Folder(Path.Combine(LoaderFolder, "0.4.8")), "0.4.8");

        var result = await _uninstaller.UninstallVersionAsync("StarMap", ModVersion.Parse("0.4.8"));

        Assert.Equal(LoaderUninstallFailure.None, result.Failure);
        Assert.True(result.DirectoryRemoved);
        Assert.True(result.FolderDeleted);
        Assert.False(result.RecordRemoved);
        Assert.Equal([kept], Directory.GetFileSystemEntries(LoaderFolder));
        var record = (await _settings.GetAsync())!.LoaderInstallations["StarMap"];
        Assert.Equal(kept, record.DirectoryPath);
        Assert.Equal(ModVersion.Parse("0.4.7"), record.Version);
    }

    [Fact]
    public async Task UninstallVersionAsync_RecordedVersionBesideAPrerelease_PointsTheRecordAtTheRelease()
    {
        var release = Folder(Path.Combine(LoaderFolder, "0.4.7"));
        Folder(Path.Combine(LoaderFolder, "0.5.0-beta.1"));
        await RecordAsync(Folder(Path.Combine(LoaderFolder, "0.4.8")), "0.4.8");

        await _uninstaller.UninstallVersionAsync("StarMap", ModVersion.Parse("0.4.8"));

        Assert.Equal(release, (await _settings.GetAsync())!.LoaderInstallations["StarMap"].DirectoryPath);
    }

    [Fact]
    public async Task UninstallVersionAsync_LegacyFolder_MovesItBesideTheLoaderFolderBeforeTheRecordIsRemoved()
    {
        await RecordAsync(Folder(LoaderFolder), "0.4.6");
        string[]? atSave = null;
        _settings.OnSave = () => atSave = Directory.GetDirectories(_paths.GetLoadersRoot()).Select(Path.GetFileName).ToArray()!;

        var result = await _uninstaller.UninstallVersionAsync("StarMap", ModVersion.Parse("0.4.6"));

        Assert.True(result.RecordRemoved);
        Assert.True(result.FolderDeleted);
        Assert.StartsWith(".StarMap.borea-removing-", Assert.Single(atSave!));
        Assert.Empty(Directory.GetFileSystemEntries(_paths.GetLoadersRoot()));
        Assert.Empty((await _settings.GetAsync())!.LoaderInstallations);
    }

    [Fact]
    public async Task UninstallVersionAsync_AdoptedFolderOutsideTheLoadersRoot_KeepsTheFolder()
    {
        var adopted = await RecordAsync(Folder(Path.Combine(_tempRoot, "My StarMap")), "0.4.6", isAdopted: true);

        var result = await _uninstaller.UninstallVersionAsync("StarMap", ModVersion.Parse("0.4.6"));

        Assert.True(result.RecordRemoved);
        Assert.False(result.DirectoryRemoved);
        Assert.True(File.Exists(Path.Combine(adopted, "StarMap.exe")));
        Assert.Empty((await _settings.GetAsync())!.LoaderInstallations);
    }

    [Fact]
    public async Task UninstallVersionAsync_LeftoversOfAnEarlierRemoval_AreSweptInBothPlaces()
    {
        var legacyLeftover = Folder(Path.Combine(_paths.GetLoadersRoot(), ".StarMap.borea-removing-" + Guid.NewGuid().ToString("N")));
        var versionLeftover = Folder(Path.Combine(LoaderFolder, ".0.4.6.borea-removing-" + Guid.NewGuid().ToString("N")));
        Folder(Path.Combine(LoaderFolder, "0.4.7"));
        var recorded = await RecordAsync(Folder(Path.Combine(LoaderFolder, "0.4.8")), "0.4.8");

        await _uninstaller.UninstallVersionAsync("StarMap", ModVersion.Parse("0.4.7"));

        Assert.False(Directory.Exists(legacyLeftover));
        Assert.False(Directory.Exists(versionLeftover));
        Assert.Equal([recorded], Directory.GetFileSystemEntries(LoaderFolder));
        Assert.Equal(recorded, (await _settings.GetAsync())!.LoaderInstallations["StarMap"].DirectoryPath);
    }

    [Fact]
    public async Task UninstallVersionAsync_WhileTheGameRuns_ChangesNothing()
    {
        var recorded = await RecordAsync(Folder(Path.Combine(LoaderFolder, "0.4.8")), "0.4.8");
        _gameRunning = true;

        var version = await _uninstaller.UninstallVersionAsync("StarMap", ModVersion.Parse("0.4.8"));
        var loader = await _uninstaller.UninstallAsync("StarMap");

        Assert.Equal(LoaderUninstallFailure.GameRunning, version.Failure);
        Assert.Equal(LoaderUninstallFailure.GameRunning, loader.Failure);
        Assert.True(File.Exists(Path.Combine(recorded, "StarMap.exe")));
        Assert.Equal(recorded, (await _settings.GetAsync())!.LoaderInstallations["StarMap"].DirectoryPath);
    }

    [Fact]
    public async Task UninstallAsync_RecordAtTheLoaderFolderThatHoldsVersions_KeepsTheLoaderFolderAndMovesTheRecord()
    {
        await RecordAsync(Folder(LoaderFolder), "0.4.6");
        var older = Folder(Path.Combine(LoaderFolder, "0.4.7"));
        var newer = Folder(Path.Combine(LoaderFolder, "0.4.8"));

        var result = await _uninstaller.UninstallAsync("StarMap");

        Assert.False(result.RecordRemoved);
        Assert.False(result.DirectoryRemoved);
        Assert.Equal(newer, (await _settings.GetAsync())!.LoaderInstallations["StarMap"].DirectoryPath);
        Assert.True(File.Exists(Path.Combine(LoaderFolder, "StarMap.exe")));
        Assert.True(Directory.Exists(older));
        Assert.True(Directory.Exists(newer));
    }

    private async Task<string> RecordAsync(bool isAdopted) =>
        await RecordAsync(Directory.CreateDirectory(Path.Combine(_tempRoot, "StarMap")).FullName, "0.4.6", isAdopted);

    private async Task<string> RecordAsync(string directory, string version, bool isAdopted = false)
    {
        var installation = new LoaderInstallation(
            directory,
            ModVersion.Parse(version),
            version + ".0",
            isAdopted);
        await _settings.SaveAsync(new BoreaSettings(
            gameDirectoryPath: null,
            loaderInstallations: new Dictionary<string, LoaderInstallation> { ["StarMap"] = installation }));
        return directory;
    }

    private static string Folder(string path)
    {
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "StarMap.exe"), "loader");
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>Runs <see cref="OnSave"/> before each save, so a test sees the folders at the moment the record is written.</summary>
    private sealed class ObservedSettingsRepository(IBoreaSettingsRepository inner) : IBoreaSettingsRepository
    {
        public Action? OnSave { get; set; }

        public Task<BoreaSettings?> GetAsync(CancellationToken cancellationToken = default) => inner.GetAsync(cancellationToken);

        public Task SaveAsync(BoreaSettings settings, CancellationToken cancellationToken = default)
        {
            OnSave?.Invoke();
            return inner.SaveAsync(settings, cancellationToken);
        }
    }
}
