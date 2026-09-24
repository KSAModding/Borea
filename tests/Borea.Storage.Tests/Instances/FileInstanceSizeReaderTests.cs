using Borea.Storage.Files;
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
        DirectoryLinks.DeleteTreeWithoutFollowingLinks(_tempRoot);
    }

    [Fact]
    public async Task ReadAsync_AddsUpEveryFileOfTheInstanceAndOfEachModFolder()
    {
        var mods = _paths.GetInstanceModsFolder(_instanceId);
        Write(Path.Combine(mods, "AdvancedFlightComputer", "mod.toml"), 100);
        Write(Path.Combine(mods, "AdvancedFlightComputer", "bin", "afc.dll"), 4000);
        Write(Path.Combine(mods, "KSArmory", "mod.toml"), 50);
        Write(Path.Combine(mods, "stray.txt"), 8);
        Write(_paths.GetInstanceSettingsPath(_instanceId), 12);
        Write(Path.Combine(_paths.GetInstanceSavesFolder(_instanceId), "Main", "save.json"), 700);

        var sizes = await _reader.ReadAsync(_instanceId);

        // every file once: the mod folders are not walked again as part of the root
        Assert.Equal(4870L, sizes.TotalBytes);
        Assert.Equal(4100L, sizes.ModBytes["advancedflightcomputer"]);
        Assert.Equal(50L, sizes.ModBytes["KSArmory"]);
        Assert.Equal(2, sizes.ModBytes.Count);
    }

    [Fact]
    public async Task ReadAsync_ModFolderLinkedToTheStore_CountsTheStoredRelease()
    {
        var stored = Path.Combine(_paths.GetStaticModFilesRoot(), "KSArmory", "1.0.0");
        Write(Path.Combine(stored, "mod.toml"), 50);
        Write(Path.Combine(stored, "bin", "armory.dll"), 3000);
        Assert.True(new DirectoryLinker().TryCreate(Path.Combine(_paths.GetInstanceModsFolder(_instanceId), "KSArmory"), stored).Linked);
        Write(_paths.GetInstanceSettingsPath(_instanceId), 12);

        var sizes = await _reader.ReadAsync(_instanceId);

        Assert.Equal(3050L, sizes.ModBytes["KSArmory"]);
        Assert.Equal(3062L, sizes.TotalBytes);
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
