using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Settings;
using Borea.Storage.Settings;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Settings;

public sealed class FileBoreaSettingsRepositoryTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly TestGamePathProvider _pathProvider;
    private readonly FileBoreaSettingsRepository _repository;

    public FileBoreaSettingsRepositoryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
        _pathProvider = new TestGamePathProvider(_tempRoot);
        _repository = new FileBoreaSettingsRepository(_pathProvider);
    }

    [Fact]
    public async Task GetAsync_NoSavedSettings_ReturnsNull()
    {
        var result = await _repository.GetAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveThenGet_RoundTripsTheGameAndTheLoaders()
    {
        var settings = new BoreaSettings(@"C:\Games\KSA", new Dictionary<string, LoaderInstallation>
        {
            ["StarMap"] = Installation(@"C:\Games\StarMap", "0.4.6", "0.4.6.0", isAdopted: true),
            ["Cheese-Loader"] = Installation(@"C:\Games\Cheese", "1.2.0", rawVersion: null, isAdopted: false),
        });

        await _repository.SaveAsync(settings);
        var reloaded = await _repository.GetAsync();

        Assert.NotNull(reloaded);
        Assert.Equal(@"C:\Games\KSA", reloaded!.GameDirectoryPath);
        Assert.Equal(2, reloaded.LoaderInstallations.Count);

        var starMap = reloaded.LoaderInstallations["StarMap"];
        Assert.Equal(@"C:\Games\StarMap", starMap.DirectoryPath);
        Assert.Equal(ModVersion.Parse("0.4.6"), starMap.Version);
        Assert.Equal("0.4.6.0", starMap.RawVersion);
        Assert.True(starMap.IsAdopted);

        var cheese = reloaded.LoaderInstallations["Cheese-Loader"];
        Assert.Equal(@"C:\Games\Cheese", cheese.DirectoryPath);
        Assert.False(cheese.IsAdopted);
    }

    [Fact]
    public async Task SaveThenGet_RoundTripsPartialSettings_GameOnly()
    {
        var settings = new BoreaSettings(@"C:\Games\KSA");

        await _repository.SaveAsync(settings);
        var reloaded = await _repository.GetAsync();

        Assert.Equal(@"C:\Games\KSA", reloaded!.GameDirectoryPath);
        Assert.Empty(reloaded.LoaderInstallations);
    }

    [Fact]
    public async Task SaveThenGet_RoundTripsPartialSettings_LoaderOnly()
    {
        var settings = new BoreaSettings(null, new Dictionary<string, LoaderInstallation>
        {
            ["StarMap"] = Installation(@"C:\Games\StarMap", "0.4.6", "0.4.6.0", isAdopted: true),
        });

        await _repository.SaveAsync(settings);
        var reloaded = await _repository.GetAsync();

        Assert.Null(reloaded!.GameDirectoryPath);
        Assert.Equal(@"C:\Games\StarMap", reloaded.LoaderInstallations["StarMap"].DirectoryPath);
    }

    [Fact]
    public async Task SaveThenGet_RoundTripsNothingSet()
    {
        var settings = new BoreaSettings(null);

        await _repository.SaveAsync(settings);
        var reloaded = await _repository.GetAsync();

        Assert.NotNull(reloaded);
        Assert.Null(reloaded!.GameDirectoryPath);
        Assert.Empty(reloaded.LoaderInstallations);
    }

    [Fact]
    public async Task SaveAsync_NoLoaders_WritesNoTable()
    {
        await _repository.SaveAsync(new BoreaSettings(@"C:\Games\KSA"));

        var text = await File.ReadAllTextAsync(_pathProvider.GetBoreaSettingsPath());

        Assert.DoesNotContain("LoaderInstallations", text);
    }

    [Fact]
    public async Task GetAsync_LoaderIdsDifferingOnlyInCase_IsRejected()
    {
        Directory.CreateDirectory(_tempRoot);
        await File.WriteAllTextAsync(_pathProvider.GetBoreaSettingsPath(), """
            [LoaderInstallations.StarMap]
            DirectoryPath = 'C:\Games\StarMap'
            IsAdopted = true

            [LoaderInstallations.starmap]
            DirectoryPath = 'C:\Games\Other'
            IsAdopted = true
            """);

        await Assert.ThrowsAsync<ArgumentException>(() => _repository.GetAsync());
    }

    [Fact]
    public async Task SaveAsync_Null_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.SaveAsync(null!));
    }

    [Fact]
    public async Task SaveAsync_Overwrites_PreviousValue()
    {
        await _repository.SaveAsync(new BoreaSettings(@"C:\Old"));
        await _repository.SaveAsync(new BoreaSettings(@"C:\New"));

        var reloaded = await _repository.GetAsync();

        Assert.Equal(@"C:\New", reloaded!.GameDirectoryPath);
    }

    private static LoaderInstallation Installation(
        string path,
        string version,
        string? rawVersion,
        bool isAdopted) => new(
        path,
        ModVersion.Parse(version),
        rawVersion,
        isAdopted);

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
