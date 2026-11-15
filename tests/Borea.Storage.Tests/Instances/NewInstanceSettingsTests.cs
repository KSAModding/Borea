using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Storage.Instances;
using Borea.Storage.Settings;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Instances;

public sealed class NewInstanceSettingsTests : IDisposable
{
    private const string GameSettings = "[Graphics]\nQuality = \"game\"\n";
    private const string SourceSettings = "[Graphics]\nQuality = \"source\"\n";
    private const string PresetSettings = "[Graphics]\nQuality = \"preset\"\n";

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly GameSettingsPresetRepository _presets;
    private readonly FileInstanceRepository _repository;

    public NewInstanceSettingsTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _presets = new GameSettingsPresetRepository(_paths);
        _repository = new FileInstanceRepository(_paths, settings: new NewInstanceSettings(_paths, _presets));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    public enum Source
    {
        /// <summary>No source passed, which means the game's settings.</summary>
        Default,
        GameDefaults,
        InstanceWithSettings,
        InstanceWithoutSettings,
        Preset,
    }

    [Theory]
    [InlineData(Source.Default, true, GameSettings)]
    [InlineData(Source.Default, false, null)]
    [InlineData(Source.GameDefaults, true, null)]
    [InlineData(Source.InstanceWithSettings, true, SourceSettings)]
    [InlineData(Source.InstanceWithoutSettings, true, GameSettings)]
    [InlineData(Source.Preset, true, PresetSettings)]
    public async Task Create_StartsWithTheSettingsOfItsSource(Source source, bool gameHasSettings, string? expected)
    {
        if (gameHasSettings)
            Write(Path.Combine(_paths.GetSharedProfileRoot(), "settings.toml"), GameSettings);
        var settings = await SettingsOfAsync(source);
        var profile = Snapshot(_paths.GetSharedProfileRoot());

        var created = await _repository.CreateAsync(new Instance("New", InstanceSource.Custom.Value), InstanceOrigin.New, settings);

        Assert.Null(created.SettingsError);
        var path = _paths.GetInstanceSettingsPath(created.Instance.InstanceId);
        Assert.Equal(expected, File.Exists(path) ? await File.ReadAllTextAsync(path) : null);
        Assert.Equal(profile, Snapshot(_paths.GetSharedProfileRoot()));
    }

    [Fact]
    public async Task Create_WhenTheCopyFails_CreatesTheInstanceAndSaysWhy()
    {
        var preset = Assert.IsType<InstanceSettingsSource.FromPreset>(await SettingsOfAsync(Source.Preset));
        File.Delete(Path.Combine(_paths.GetGameSettingsPresetsRoot(), preset.PresetId.ToString(), "settings.toml"));

        var created = await _repository.CreateAsync(new Instance("New", InstanceSource.Custom.Value), InstanceOrigin.New, preset);

        Assert.NotNull(created.SettingsError);
        Assert.NotNull(await _repository.GetByIdAsync(created.Instance.InstanceId));
        Assert.False(File.Exists(_paths.GetInstanceSettingsPath(created.Instance.InstanceId)));
    }

    private async Task<InstanceSettingsSource?> SettingsOfAsync(Source source)
    {
        switch (source)
        {
            case Source.GameDefaults:
                return InstanceSettingsSource.GameDefaults.Value;
            case Source.InstanceWithSettings or Source.InstanceWithoutSettings:
                var created = await _repository.CreateAsync(new Instance("Source", InstanceSource.Custom.Value), InstanceOrigin.New, InstanceSettingsSource.GameDefaults.Value);
                if (source == Source.InstanceWithSettings)
                    Write(_paths.GetInstanceSettingsPath(created.Instance.InstanceId), SourceSettings);
                return new InstanceSettingsSource.FromInstance(created.Instance.InstanceId);
            case Source.Preset:
                var file = Path.Combine(_tempRoot, "preset-settings.toml");
                Write(file, PresetSettings);
                var preset = await _presets.SaveAsync("Preset", GameVersion.Parse("2026.9.7.5402"), file);
                return new InstanceSettingsSource.FromPreset(preset.Id);
            default:
                return null;
        }
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>Every file below <paramref name="folder"/> with its text, so a test sees any file that was added, changed or removed.</summary>
    private static Dictionary<string, string> Snapshot(string folder) => Directory.Exists(folder)
        ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(folder, path), File.ReadAllText)
        : [];
}
