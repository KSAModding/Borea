using Borea.Storage.Instances;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Instances;

public sealed class FileInstanceSizeReaderTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly FileInstanceSizeReader _reader;
    private readonly Guid _instanceId = Guid.NewGuid();

    public FileInstanceSizeReaderTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _reader = new FileInstanceSizeReader(_paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task ReadAsync_AddsUpEveryFileOfTheInstanceAndOfEachModFolder()
    {
        var mods = _paths.GetInstanceModsFolder(_instanceId);
        Write(Path.Combine(mods, "AdvancedFlightComputer", "mod.toml"), 100);
        Write(Path.Combine(mods, "AdvancedFlightComputer", "bin", "afc.dll"), 4000);
        Write(Path.Combine(mods, "KSArmory", "mod.toml"), 50);
        Write(_paths.GetInstanceSettingsPath(_instanceId), 12);
        Write(Path.Combine(_paths.GetInstanceSavesFolder(_instanceId), "Main", "save.json"), 700);

        var sizes = await _reader.ReadAsync(_instanceId);

        Assert.Equal(4862L, sizes.TotalBytes);
        Assert.Equal(4100L, sizes.ModBytes["advancedflightcomputer"]);
        Assert.Equal(50L, sizes.ModBytes["KSArmory"]);
        Assert.Equal(2, sizes.ModBytes.Count);
    }

    [Fact]
    public async Task ReadAsync_InstanceWithoutFolders_IsEmpty()
    {
        var sizes = await _reader.ReadAsync(_instanceId);

        Assert.Equal(0L, sizes.TotalBytes);
        Assert.Empty(sizes.ModBytes);
    }

    private static void Write(string path, int length)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[length]);
    }
}
