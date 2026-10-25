using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Settings;
using Borea.Storage.ModLoaders;
using Borea.Storage.Settings;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.ModLoaders;

public sealed class LoaderLayoutMigrationTests : IDisposable
{
    private const string LoaderId = "StarMap";

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly FileBoreaSettingsRepository _settings;
    private readonly LoaderLayoutMigration _migration;

    public LoaderLayoutMigrationTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _settings = new FileBoreaSettingsRepository(_paths);
        _migration = new LoaderLayoutMigration(_settings, _paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private string FlatFolder => Path.Combine(_paths.GetLoadersRoot(), LoaderId);

    private string MovingFolder => Path.Combine(_paths.GetLoadersRoot(), $".{LoaderId}.borea-moving-{Guid.NewGuid():N}");

    private static void PlaceStarMap(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "StarMap.exe"), "exe");
    }

    private Task RecordAtFlatFolderAsync(bool isAdopted = false) =>
        _settings.SaveAsync(new BoreaSettings(null, new Dictionary<string, LoaderInstallation>
        {
            [LoaderId] = new(FlatFolder, ModVersion.Parse("0.4.7"), rawVersion: null, isAdopted),
        }));

    private async Task<LoaderInstallation> RecordAsync() => (await _settings.GetAsync())!.LoaderInstallations[LoaderId];

    private void AssertNoMovingFolder() => Assert.Empty(Directory.EnumerateDirectories(_paths.GetLoadersRoot(), ".*"));

    [Fact]
    public async Task RecoverAsync_MoveStoppedWithARecordedVersion_FinishesIt()
    {
        await RecordAtFlatFolderAsync(isAdopted: true);
        PlaceStarMap(MovingFolder);
        Directory.CreateDirectory(FlatFolder);

        var repairs = await _migration.RecoverAsync();

        Assert.Equal(1, repairs);
        Assert.True(File.Exists(Path.Combine(FlatFolder, "0.4.7", "StarMap.exe")));
        AssertNoMovingFolder();
        var record = await RecordAsync();
        Assert.Equal(Path.Combine(FlatFolder, "0.4.7"), record.DirectoryPath);
        Assert.False(record.IsAdopted);
    }

    [Fact]
    public async Task RecoverAsync_MoveStoppedWithoutAVersion_PutsTheFlatFolderBack()
    {
        PlaceStarMap(MovingFolder);

        await _migration.RecoverAsync();

        Assert.True(File.Exists(Path.Combine(FlatFolder, "StarMap.exe")));
        AssertNoMovingFolder();
    }

    [Fact]
    public async Task RecoverAsync_RenamedButNotSaved_PointsTheRecordAtTheVersionFolder()
    {
        await RecordAtFlatFolderAsync();
        PlaceStarMap(Path.Combine(FlatFolder, "0.4.7"));

        var repairs = await _migration.RecoverAsync();

        Assert.Equal(1, repairs);
        var record = await RecordAsync();
        Assert.Equal(Path.Combine(FlatFolder, "0.4.7"), record.DirectoryPath);
        Assert.Equal(ModVersion.Parse("0.4.7"), record.Version);
    }

    [Fact]
    public async Task RecoverAsync_FlatInstall_IsLeftAsItIs()
    {
        await RecordAtFlatFolderAsync();
        PlaceStarMap(FlatFolder);

        var repairs = await _migration.RecoverAsync();

        Assert.Equal(0, repairs);
        Assert.Equal(FlatFolder, (await RecordAsync()).DirectoryPath);
        Assert.True(File.Exists(Path.Combine(FlatFolder, "StarMap.exe")));
    }
}
