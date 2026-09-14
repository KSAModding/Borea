using Borea.Storage.Instances;
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
        _reader = new FileGameDataReader(_paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task ReadAsync_FixtureInstance_ListsEveryEntryWithItsSize()
    {
        Write(Path.Combine(_paths.GetInstanceSavesFolder(_instanceId), "Orbit", "save.dat"), 300);
        Write(Path.Combine(_paths.GetInstanceSavesFolder(_instanceId), "quick.dat"), 200);
        Write(Path.Combine(_paths.GetInstanceVehiclesFolder(_instanceId), "Rocket.xml"), 50);
        Write(_paths.GetInstanceSettingsPath(_instanceId), 12);
        Directory.CreateDirectory(_paths.GetInstanceHudLayoutsFolder(_instanceId));

        var entries = await _reader.ReadAsync(_instanceId);

        Assert.Equal(["saves", "Vehicles", "settings.toml", "HUDLayouts", "crashdumps", "exports"], entries.Select(entry => entry.Name));
        Assert.Equal([500L, 50L, 12L, 0L, 0L, 0L], entries.Select(entry => entry.SizeBytes));
        Assert.Equal([true, true, true, true, false, false], entries.Select(entry => entry.Exists));
        Assert.Equal([true, true, false, true, true, true], entries.Select(entry => entry.IsFolder));
        Assert.Equal(_paths.GetInstanceSavesFolder(_instanceId), entries[0].Path);
    }

    [Fact]
    public async Task ReadAsync_MissingInstanceFolder_ListsEveryEntryAsMissingAndEmpty()
    {
        var entries = await _reader.ReadAsync(_instanceId);

        Assert.Equal(6, entries.Count);
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
