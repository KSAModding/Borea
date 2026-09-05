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
        var settings = new BoreaSettings(@"C:\Games\KSA", new Dictionary<string, string>
        {
            ["StarMap"] = @"C:\Games\StarMap",
            ["Cheese-Loader"] = @"C:\Games\Cheese",
        });

        await _repository.SaveAsync(settings);
        var reloaded = await _repository.GetAsync();

        Assert.NotNull(reloaded);
        Assert.Equal(@"C:\Games\KSA", reloaded!.GameDirectoryPath);
        Assert.Equal(2, reloaded.LoaderDirectoryPaths.Count);
        Assert.Equal(@"C:\Games\StarMap", reloaded.LoaderDirectoryPaths["StarMap"]);
        Assert.Equal(@"C:\Games\Cheese", reloaded.LoaderDirectoryPaths["Cheese-Loader"]);
    }

    [Fact]
    public async Task SaveThenGet_RoundTripsPartialSettings_GameOnly()
    {
        // Confirms the "installed one, not the other" scenario this whole
        // nullable design was built for actually persists correctly.
        var settings = new BoreaSettings(@"C:\Games\KSA");

        await _repository.SaveAsync(settings);
        var reloaded = await _repository.GetAsync();

        Assert.Equal(@"C:\Games\KSA", reloaded!.GameDirectoryPath);
        Assert.Empty(reloaded.LoaderDirectoryPaths);
    }

    [Fact]
    public async Task SaveThenGet_RoundTripsPartialSettings_LoaderOnly()
    {
        var settings = new BoreaSettings(null, new Dictionary<string, string> { ["StarMap"] = @"C:\Games\StarMap" });

        await _repository.SaveAsync(settings);
        var reloaded = await _repository.GetAsync();

        Assert.Null(reloaded!.GameDirectoryPath);
        Assert.Equal(@"C:\Games\StarMap", reloaded.LoaderDirectoryPaths["StarMap"]);
    }

    [Fact]
    public async Task SaveThenGet_RoundTripsNothingSet()
    {
        var settings = new BoreaSettings(null);

        await _repository.SaveAsync(settings);
        var reloaded = await _repository.GetAsync();

        Assert.NotNull(reloaded); // Distinct from "never saved" (null): a file exists, it just carries nothing.
        Assert.Null(reloaded!.GameDirectoryPath);
        Assert.Empty(reloaded.LoaderDirectoryPaths);
    }

    [Fact]
    public async Task SaveAsync_NoLoaders_WritesNoTable()
    {
        await _repository.SaveAsync(new BoreaSettings(@"C:\Games\KSA"));

        var text = await File.ReadAllTextAsync(_pathProvider.GetBoreaSettingsPath());

        Assert.DoesNotContain("LoaderDirectoryPaths", text);
    }

    [Fact]
    public async Task GetAsync_LoaderIdsDifferingOnlyInCase_IsRejected()
    {
        Directory.CreateDirectory(_tempRoot);
        await File.WriteAllTextAsync(_pathProvider.GetBoreaSettingsPath(), """
            [LoaderDirectoryPaths]
            StarMap = 'C:\Games\StarMap'
            starmap = 'C:\Games\Other'
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
        await _repository.SaveAsync(new BoreaSettings(@"C:\Old", null));
        await _repository.SaveAsync(new BoreaSettings(@"C:\New", null));

        var reloaded = await _repository.GetAsync();

        Assert.Equal(@"C:\New", reloaded!.GameDirectoryPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
