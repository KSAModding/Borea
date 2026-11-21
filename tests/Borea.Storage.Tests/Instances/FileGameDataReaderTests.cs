using Borea.Core.Instances;
using Borea.Storage.Instances;
using Borea.Storage.Screenshots;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Instances;

public sealed class FileGameDataReaderTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly FileGameDataReader _reader;
    private readonly Guid _instanceId = Guid.NewGuid();

    public FileGameDataReaderTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _reader = new FileGameDataReader(_paths, new FileScreenshotStore());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task ReadAsync_FixtureInstance_ListsEveryEntryWithItsSize()
    {
        Write(_paths.GetInstanceSettingsPath(_instanceId), 12);
        Write(Path.Combine(_paths.GetInstanceHudLayoutsFolder(_instanceId), "Default", "layout.toml"), 300);
        Write(Path.Combine(_paths.GetInstanceHudLayoutsFolder(_instanceId), "meta.toml"), 200);
        Directory.CreateDirectory(_paths.GetInstanceExportsFolder(_instanceId));

        var entries = await _reader.ReadAsync(_instanceId);

        Assert.Equal([GameDataKind.Settings, GameDataKind.HudLayouts, GameDataKind.CrashDumps, GameDataKind.Screenshots, GameDataKind.Exports], entries.Select(entry => entry.Kind));
        Assert.Equal(["settings.toml", "HUDLayouts", "crashdumps", "screenshots", "exports"], entries.Select(entry => entry.Name));
        Assert.Equal([12L, 500L, 0L, 0L, 0L], entries.Select(entry => entry.SizeBytes));
        Assert.Equal([true, true, false, false, true], entries.Select(entry => entry.Exists));
        Assert.Equal([false, true, true, true, true], entries.Select(entry => entry.IsFolder));
        Assert.Equal(_paths.GetInstanceHudLayoutsFolder(_instanceId), entries[1].Path);
    }

    [Fact]
    public async Task ReadAsync_ScreenshotsInsideExports_CountsThemOnTheirOwnRow()
    {
        var screenshots = _paths.GetInstanceScreenshotsFolder(_instanceId);
        Write(Path.Combine(screenshots, "ksa_20261120_101500_1920x1080.png"), 700);
        Write(Path.Combine(screenshots, "ksa_20261120_101600_1920x1080.png"), 300);
        Write(Path.Combine(screenshots, "notes.txt"), 40);
        Write(Path.Combine(_paths.GetInstanceExportsFolder(_instanceId), "planets", "earth.png"), 90);

        var entries = await _reader.ReadAsync(_instanceId);

        var screenshotsEntry = entries.Single(entry => entry.Kind == GameDataKind.Screenshots);
        Assert.Equal(2, screenshotsEntry.ScreenshotCount);
        Assert.Equal(1000, screenshotsEntry.SizeBytes);
        Assert.Equal(screenshots, screenshotsEntry.Path);
        var exports = entries.Single(entry => entry.Kind == GameDataKind.Exports);
        Assert.Equal(90, exports.SizeBytes);
        Assert.Equal(_paths.GetInstanceExportsFolder(_instanceId), exports.Path);
    }

    [Fact]
    public async Task ReadAsync_MissingInstanceFolder_ListsEveryEntryAsMissingAndEmpty()
    {
        var entries = await _reader.ReadAsync(_instanceId);

        Assert.Equal(5, entries.Count);
        Assert.All(entries, entry =>
        {
            Assert.False(entry.Exists);
            Assert.Equal(0, entry.SizeBytes);
        });
        Assert.False(Directory.Exists(_paths.GetInstanceRoot(_instanceId)));
    }

    private static void Write(string path, int length)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[length]);
    }
}
