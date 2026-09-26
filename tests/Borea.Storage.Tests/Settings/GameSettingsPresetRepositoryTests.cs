using Borea.Core.Game;
using Borea.Storage.Settings;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Settings;

public sealed class GameSettingsPresetRepositoryTests : IDisposable
{
    private const string Settings = "[Graphics]\nQuality = \"high\"\n";

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly GameSettingsPresetRepository _repository;
    private readonly GameVersion _version = GameVersion.Parse("2026.9.7.5402");

    public GameSettingsPresetRepositoryTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _repository = new GameSettingsPresetRepository(_paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task SaveAsync_CopiesTheSettingsAndKeepsTheNameAndTheVersion()
    {
        var preset = await _repository.SaveAsync("Full screen", _version, WriteSettings());

        Assert.Equal("Full screen", preset.Name);
        Assert.Equal(_version, preset.Version);
        var read = await _repository.GetAsync(preset.Id);
        Assert.Equal(preset.Id, read?.Id);
        Assert.Equal("Full screen", read?.Name);
        Assert.Equal(_version, read?.Version);
        Assert.Equal(Settings, await File.ReadAllTextAsync(Path.Combine(PresetFolder(preset.Id), "settings.toml")));
    }

    [Fact]
    public async Task SaveAsync_SettingsThatDoNotParse_SavesNothing()
    {
        var path = WriteSettings("not = toml = at all\n");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.SaveAsync("Broken", _version, path));

        Assert.Empty(await _repository.ListAsync());
        Assert.False(Directory.Exists(_paths.GetGameSettingsPresetsRoot()) && Directory.EnumerateDirectories(_paths.GetGameSettingsPresetsRoot()).Any());
    }

    [Fact]
    public async Task ListAsync_WithoutAnyPreset_IsEmpty()
    {
        Assert.Empty(await _repository.ListAsync());
    }

    [Fact]
    public async Task ListAsync_SkipsAFolderThatIsNotAPreset()
    {
        var kept = await _repository.SaveAsync("Kept", _version, WriteSettings());
        Directory.CreateDirectory(Path.Combine(_paths.GetGameSettingsPresetsRoot(), "not-a-preset"));
        await File.WriteAllTextAsync(Path.Combine(PresetFolder(Guid.NewGuid(), create: true), "preset.toml"), "id = \"nonsense\"\n");

        var presets = await _repository.ListAsync();

        Assert.Equal(kept.Id, Assert.Single(presets).Id);
    }

    [Fact]
    public async Task ApplyAsync_WritesTheSettingsOfThePresetIntoTheInstance()
    {
        var preset = await _repository.SaveAsync("Full screen", _version, WriteSettings());
        var instanceId = Guid.NewGuid();
        var target = _paths.GetInstanceSettingsPath(instanceId);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, "[Graphics]\nQuality = \"low\"\n");

        await _repository.ApplyAsync(preset.Id, instanceId);

        Assert.Equal(Settings, await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task ApplyAsync_PresetThatIsNotThere_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.ApplyAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteAsync_RemovesThePresetAndLeavesTheOthers()
    {
        var removed = await _repository.SaveAsync("Removed", _version, WriteSettings());
        var kept = await _repository.SaveAsync("Kept", _version, WriteSettings());

        await _repository.DeleteAsync(removed.Id);

        Assert.Equal(kept.Id, Assert.Single(await _repository.ListAsync()).Id);
        Assert.Null(await _repository.GetAsync(removed.Id));
        Assert.False(Directory.Exists(PresetFolder(removed.Id)));
    }

    [Fact]
    public async Task DeleteAsync_PresetThatIsNotThere_DoesNothing()
    {
        await _repository.DeleteAsync(Guid.NewGuid());
    }

    private string PresetFolder(Guid id, bool create = false)
    {
        var folder = Path.Combine(_paths.GetGameSettingsPresetsRoot(), id.ToString());
        if (create)
            Directory.CreateDirectory(folder);
        return folder;
    }

    private string WriteSettings(string text = Settings)
    {
        var path = Path.Combine(_tempRoot, "source", Guid.NewGuid() + ".toml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }
}
