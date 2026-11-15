using Borea.Core.Settings;
using Borea.Storage.Settings;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Settings;

public sealed class SharedProfileSettingsTests : IDisposable
{
    private const string GameSettings = "[Graphics]\nQuality = \"game\"\n";
    private const string InstanceSettings = "[Graphics]\nQuality = \"instance\"\n";

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly Guid _instanceId = Guid.NewGuid();

    public SharedProfileSettingsTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        Write(Path.Combine(_paths.GetSharedProfileRoot(), "settings.toml"), GameSettings);
        Write(Path.Combine(_paths.GetSharedProfileRoot(), "Saves", "Orbit.sav"), "save");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task CopyFromInstance_KeepsTheOldFileAsABackupAndReplacesTheGameFile()
    {
        Write(_paths.GetInstanceSettingsPath(_instanceId), InstanceSettings);
        var settings = new SharedProfileSettings(_paths, () => false);
        var profileFiles = Files(_paths.GetSharedProfileRoot());

        var copy = await settings.CopyFromInstanceAsync(_instanceId);

        Assert.Equal(SharedProfileSettingsCopyOutcome.Copied, copy.Outcome);
        Assert.Equal(settings.BackupFolder, Path.GetDirectoryName(copy.BackupPath));
        Assert.Equal(GameSettings, await File.ReadAllTextAsync(copy.BackupPath!));
        Assert.Equal(InstanceSettings, await File.ReadAllTextAsync(Path.Combine(_paths.GetSharedProfileRoot(), "settings.toml")));
        Assert.Equal(profileFiles, Files(_paths.GetSharedProfileRoot()));
        Assert.Equal("save", await File.ReadAllTextAsync(Path.Combine(_paths.GetSharedProfileRoot(), "Saves", "Orbit.sav")));
    }

    [Theory]
    [InlineData(true, true, SharedProfileSettingsCopyOutcome.GameRunning)]
    [InlineData(false, false, SharedProfileSettingsCopyOutcome.NoInstanceSettings)]
    public async Task CopyFromInstance_IsRefused_WhileTheGameRunsOrWithoutInstanceSettings(bool gameRunning, bool instanceHasSettings, SharedProfileSettingsCopyOutcome expected)
    {
        if (instanceHasSettings)
            Write(_paths.GetInstanceSettingsPath(_instanceId), InstanceSettings);
        var settings = new SharedProfileSettings(_paths, () => gameRunning);

        var copy = await settings.CopyFromInstanceAsync(_instanceId);

        Assert.Equal(expected, copy.Outcome);
        Assert.Null(copy.BackupPath);
        Assert.Equal(GameSettings, await File.ReadAllTextAsync(Path.Combine(_paths.GetSharedProfileRoot(), "settings.toml")));
        Assert.False(Directory.Exists(settings.BackupFolder));
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static string[] Files(string folder) =>
        [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(folder, path)).Order(StringComparer.Ordinal)];
}
