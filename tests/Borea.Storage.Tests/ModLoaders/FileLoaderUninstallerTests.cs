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
    private readonly FileBoreaSettingsRepository _settings;
    private readonly FileLoaderUninstaller _uninstaller;

    public FileLoaderUninstallerTests()
    {
        _settings = new FileBoreaSettingsRepository(new TestGamePathProvider(_tempRoot));
        _uninstaller = new FileLoaderUninstaller(_settings);
    }

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
        Assert.Throws<ArgumentNullException>(() => new FileLoaderUninstaller(null!));
    }

    private async Task<string> RecordAsync(bool isAdopted)
    {
        var directory = Path.Combine(_tempRoot, "StarMap");
        Directory.CreateDirectory(directory);
        var installation = new LoaderInstallation(
            directory,
            ModVersion.Parse("0.4.6"),
            "0.4.6.0",
            isAdopted);
        await _settings.SaveAsync(new BoreaSettings(
            gameDirectoryPath: null,
            loaderInstallations: new Dictionary<string, LoaderInstallation> { ["StarMap"] = installation }));
        return directory;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
