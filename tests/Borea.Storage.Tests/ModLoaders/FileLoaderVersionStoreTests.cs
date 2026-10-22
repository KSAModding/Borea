using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Settings;
using Borea.Storage.ModLoaders;
using Borea.Storage.Settings;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.ModLoaders;

public sealed class FileLoaderVersionStoreTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly FileBoreaSettingsRepository _settings;
    private readonly FileLoaderVersionStore _store;

    public FileLoaderVersionStoreTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _settings = new FileBoreaSettingsRepository(_paths);
        _store = new FileLoaderVersionStore(_settings, _paths);
    }

    private string LoaderFolder => Path.Combine(_paths.GetLoadersRoot(), "StarMap");

    [Fact]
    public async Task ListAsync_LegacyRecordAtTheFolderOfTheLoader_ListsOneVersion()
    {
        await RecordAsync(Folder(LoaderFolder), "0.4.6");

        var listed = await _store.ListAsync();

        var only = Assert.Single(listed);
        Assert.Equal("StarMap", only.LoaderId);
        Assert.Equal(ModVersion.Parse("0.4.6"), only.Version);
        Assert.True(only.IsRecorded);
        Assert.False(only.IsAdopted);
    }

    [Fact]
    public async Task ListAsync_VersionFolders_ListsOnlyTheVersionsNewestFirst()
    {
        Folder(Path.Combine(LoaderFolder, "0.4.7"));
        Folder(Path.Combine(LoaderFolder, "0.4.8"));
        Folder(Path.Combine(LoaderFolder, ".0.4.9.borea-staging-" + Guid.NewGuid().ToString("N")));
        Folder(Path.Combine(LoaderFolder, "notes"));
        Directory.CreateDirectory(Path.Combine(LoaderFolder, "0.5.0"));

        var listed = await _store.ListAsync();

        Assert.Equal(["0.4.8", "0.4.7"], listed.Select(v => v.Version.ToString()));
        Assert.All(listed, v => Assert.False(v.IsRecorded));
        Assert.Equal(Path.Combine(LoaderFolder, "0.4.8"), InstalledLoaderVersion.Find(listed, "starmap", ModVersion.Parse("0.4.8"))!.DirectoryPath);
    }

    [Fact]
    public async Task ListAsync_RecordedFolderUpdatedInPlace_ListsTheVersionOfTheRecordOnce()
    {
        await RecordAsync(Folder(Path.Combine(LoaderFolder, "0.4.8")), "0.4.9");

        var listed = await _store.ListAsync();

        var only = Assert.Single(listed);
        Assert.Equal(ModVersion.Parse("0.4.9"), only.Version);
        Assert.True(only.IsRecorded);
    }

    [Fact]
    public async Task ListAsync_TwoFoldersClaimOneVersion_KeepsTheRecordedFolder()
    {
        var recorded = Folder(Path.Combine(LoaderFolder, "0.4.8"));
        await RecordAsync(recorded, "0.4.9");
        Folder(Path.Combine(LoaderFolder, "0.4.9"));

        var listed = await _store.ListAsync();

        var only = Assert.Single(listed);
        Assert.Equal(ModVersion.Parse("0.4.9"), only.Version);
        Assert.Equal(recorded, only.DirectoryPath);
    }

    [Fact]
    public async Task ListAsync_AdoptedRecordOutsideTheLoadersRoot_IsListedAsAdopted()
    {
        await RecordAsync(Folder(Path.Combine(_tempRoot, "Elsewhere", "StarMap")), "0.4.6", isAdopted: true);

        var listed = await _store.ListAsync();

        Assert.True(Assert.Single(listed).IsAdopted);
    }

    [Fact]
    public async Task ListAsync_VersionRecordedAfterTheStoreWasBuilt_IsFoundOnTheNextCall()
    {
        Assert.Empty(await _store.ListAsync());

        // A folder outside the Loaders root, so only the settings can name it.
        await RecordAsync(Folder(Path.Combine(_tempRoot, "Elsewhere", "StarMap")), "0.4.7");

        Assert.Equal(ModVersion.Parse("0.4.7"), Assert.Single(await _store.ListAsync()).Version);
    }

    private static string Folder(string path)
    {
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "StarMap.exe"), "loader");
        return path;
    }

    private Task RecordAsync(string directory, string version, bool isAdopted = false)
        => _settings.SaveAsync(new BoreaSettings(
            gameDirectoryPath: null,
            loaderInstallations: new Dictionary<string, LoaderInstallation>
            {
                ["StarMap"] = new LoaderInstallation(directory, ModVersion.Parse(version), version + ".0", isAdopted),
            }));

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
