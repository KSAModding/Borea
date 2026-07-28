using Borea.Core.Settings;
using Borea.Storage.Settings;

namespace Borea.Storage.Tests;

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
    public async Task SaveThenGet_RoundTripsBothPaths()
    {
        var settings = new BoreaSettings(@"C:\Games\KSA", @"C:\Games\StarMap");

        await _repository.SaveAsync(settings);
        var reloaded = await _repository.GetAsync();

        Assert.NotNull(reloaded);
        Assert.Equal(@"C:\Games\KSA", reloaded!.GameDirectoryPath);
        Assert.Equal(@"C:\Games\StarMap", reloaded.StarMapDirectoryPath);
    }

    [Fact]
    public async Task SaveThenGet_RoundTripsPartialSettings_GameOnly()
    {
        // Confirms the "installed one, not the other" scenario this whole
        // nullable design was built for actually persists correctly.
        var settings = new BoreaSettings(@"C:\Games\KSA", null);

        await _repository.SaveAsync(settings);
        var reloaded = await _repository.GetAsync();

        Assert.Equal(@"C:\Games\KSA", reloaded!.GameDirectoryPath);
        Assert.Null(reloaded.StarMapDirectoryPath);
    }

    [Fact]
    public async Task SaveThenGet_RoundTripsPartialSettings_StarMapOnly()
    {
        var settings = new BoreaSettings(null, @"C:\Games\StarMap");

        await _repository.SaveAsync(settings);
        var reloaded = await _repository.GetAsync();

        Assert.Null(reloaded!.GameDirectoryPath);
        Assert.Equal(@"C:\Games\StarMap", reloaded.StarMapDirectoryPath);
    }

    [Fact]
    public async Task SaveThenGet_RoundTripsBothNull()
    {
        var settings = new BoreaSettings(null, null);

        await _repository.SaveAsync(settings);
        var reloaded = await _repository.GetAsync();

        Assert.NotNull(reloaded); // Distinct from "never saved" (null) — a file exists, both fields just happen to be empty.
        Assert.Null(reloaded!.GameDirectoryPath);
        Assert.Null(reloaded.StarMapDirectoryPath);
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