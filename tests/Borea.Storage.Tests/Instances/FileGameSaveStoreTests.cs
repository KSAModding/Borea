using System.IO.Compression;
using Borea.Core.Instances;
using Borea.Storage.Instances;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Instances;

public sealed class FileGameSaveStoreTests : IDisposable
{
    private const string Stamp = "2026-09-15T123005Z";

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly FileGameSaveStore _store;
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly Guid _otherInstanceId = Guid.NewGuid();

    public FileGameSaveStoreTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _store = new FileGameSaveStore(_paths, new FixedTime(new DateTimeOffset(2026, 9, 15, 12, 30, 5, TimeSpan.Zero)));
        Directory.CreateDirectory(_paths.GetInstanceRoot(_instanceId));
        Directory.CreateDirectory(_paths.GetInstanceRoot(_otherInstanceId));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task ListAsync_Saves_ReadsNameUpdatedTimeBuildAndSize()
    {
        var saves = _paths.GetInstanceSavesFolder(_instanceId);
        WriteItem(saves, "Earth Launch 1", "Earth Launch 1", "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", 300);
        WriteItem(saves, "orbit-test", "Orbit test", "2026-09-14T14:38:55.7142787", "v2026.9.7.5402", 100);

        var entries = await _store.ListAsync(_instanceId, GameSaveKind.Save);

        Assert.Equal(["Orbit test", "Earth Launch 1"], entries.Select(entry => entry.Name));
        var earth = entries[1];
        Assert.Equal(GameSaveKind.Save, earth.Kind);
        Assert.Equal("Earth Launch 1", earth.FolderName);
        Assert.Equal(Path.Combine(saves, "Earth Launch 1"), earth.Path);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 14, 34, 32, TimeSpan.Zero).AddTicks(4054896), earth.UpdatedAt);
        Assert.Equal("v2026.8.3.5117", earth.GameBuild);
        Assert.Equal(300 + new FileInfo(Path.Combine(earth.Path, "meta.toml")).Length, earth.SizeBytes);
    }

    [Fact]
    public async Task ListAsync_LowercaseVehiclesFolder_FindsTheVehicles()
    {
        WriteItem(Path.Combine(_paths.GetInstanceRoot(_instanceId), "vehicles"), "Rocket", "Rocket", "2026-08-10T06:44:36.6429982", "v2026.8.1.5240-DEBUG--dev-baker", 50, "vehicle.xml");

        var entry = Assert.Single(await _store.ListAsync(_instanceId, GameSaveKind.Vehicle));

        Assert.Equal("Rocket", entry.Name);
        Assert.Equal(GameSaveKind.Vehicle, entry.Kind);
        Assert.Equal("vehicles", Path.GetFileName(Path.GetDirectoryName(entry.Path)));
    }

    [Fact]
    public async Task ListAsync_UnreadableMetadata_ListsTheFolderNameAndDate()
    {
        var saves = _paths.GetInstanceSavesFolder(_instanceId);
        var broken = Directory.CreateDirectory(Path.Combine(saves, "Broken")).FullName;
        File.WriteAllText(Path.Combine(broken, "meta.toml"), "name = [");
        var missing = Directory.CreateDirectory(Path.Combine(saves, "No meta")).FullName;
        var brokenDate = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        var missingDate = new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc);
        Directory.SetLastWriteTimeUtc(broken, brokenDate);
        Directory.SetLastWriteTimeUtc(missing, missingDate);

        var entries = await _store.ListAsync(_instanceId, GameSaveKind.Save);

        Assert.Equal(["No meta", "Broken"], entries.Select(entry => entry.Name));
        Assert.Equal([new DateTimeOffset(missingDate), new DateTimeOffset(brokenDate)], entries.Select(entry => entry.UpdatedAt));
        Assert.All(entries, entry => Assert.Null(entry.GameBuild));
    }

    [Fact]
    public async Task ListAsync_NoFolder_ListsNothing()
    {
        Assert.Empty(await _store.ListAsync(_instanceId, GameSaveKind.Save));
    }

    [Fact]
    public async Task ListSharedProfileAsync_ReadsTheGameProfile()
    {
        WriteItem(Path.Combine(_paths.GetSharedProfileRoot(), "saves"), "Earth Launch 1", "Earth Launch 1", "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", 10);

        var entry = Assert.Single(await _store.ListSharedProfileAsync(GameSaveKind.Save));

        Assert.Equal(Path.Combine(_paths.GetSharedProfileRoot(), "saves", "Earth Launch 1"), entry.Path);
    }

    [Fact]
    public async Task BackUpAsync_ZipsTheFolderAndKeepsAnEarlierZip()
    {
        var entry = await AddSaveAsync(_instanceId, "Orbit");

        var first = await _store.BackUpAsync(_instanceId, entry);
        var second = await _store.BackUpAsync(_instanceId, entry);

        var backups = Path.Combine(_paths.GetBackupsRoot(), _instanceId.ToString(), "saves");
        Assert.Equal(Path.Combine(backups, $"Orbit-{Stamp}.zip"), first);
        Assert.Equal(Path.Combine(backups, $"Orbit-{Stamp}-2.zip"), second);
        using var zip = ZipFile.OpenRead(first);
        Assert.Equal(["Orbit/meta.toml", "Orbit/universe.xml"], zip.Entries.Select(item => item.FullName).Order(StringComparer.Ordinal));
        Assert.True(Directory.Exists(entry.Path));
    }

    [Fact]
    public async Task BackUpAllAsync_WritesOneZipPerSave()
    {
        await AddSaveAsync(_instanceId, "Orbit");
        await AddSaveAsync(_instanceId, "Moon");

        var zips = await _store.BackUpAllAsync(_instanceId, GameSaveKind.Save);

        Assert.Equal([$"Moon-{Stamp}.zip", $"Orbit-{Stamp}.zip"], zips.Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.All(zips, zip => Assert.True(File.Exists(zip)));
    }

    [Fact]
    public async Task CopyAsync_ToAnotherInstance_CopiesTheWholeFolder()
    {
        var entry = await AddSaveAsync(_instanceId, "Orbit");

        var outcome = await _store.CopyAsync(entry, _otherInstanceId, replace: false);

        Assert.Equal(GameSaveCopyOutcome.Copied, outcome);
        var copied = Path.Combine(_paths.GetInstanceSavesFolder(_otherInstanceId), "Orbit");
        Assert.Equal(File.ReadAllBytes(Path.Combine(entry.Path, "universe.xml")), File.ReadAllBytes(Path.Combine(copied, "universe.xml")));
        Assert.True(File.Exists(Path.Combine(copied, "meta.toml")));
        Assert.True(Directory.Exists(entry.Path));
        Assert.Equal(["saves"], Directory.GetDirectories(_paths.GetInstanceRoot(_otherInstanceId)).Select(Path.GetFileName));
    }

    [Fact]
    public async Task CopyAsync_NameExists_ReplacesOnlyWhenAskedAndKeepsTheOldFolder()
    {
        var entry = await AddSaveAsync(_instanceId, "Orbit", universeBytes: 300);
        var existing = await AddSaveAsync(_otherInstanceId, "Orbit", universeBytes: 20);

        Assert.Equal(GameSaveCopyOutcome.Exists, await _store.CopyAsync(entry, _otherInstanceId, replace: false));
        Assert.Equal(20, new FileInfo(Path.Combine(existing.Path, "universe.xml")).Length);

        Assert.Equal(GameSaveCopyOutcome.Copied, await _store.CopyAsync(entry, _otherInstanceId, replace: true));

        Assert.Equal(300, new FileInfo(Path.Combine(existing.Path, "universe.xml")).Length);
        var kept = Path.Combine(_paths.GetBackupsRoot(), _otherInstanceId.ToString(), "saves", $"Orbit-{Stamp}");
        Assert.Equal(20, new FileInfo(Path.Combine(kept, "universe.xml")).Length);
    }

    [Fact]
    public async Task CopyAsync_FromTheGameProfile_CopiesIntoTheInstance()
    {
        WriteItem(Path.Combine(_paths.GetSharedProfileRoot(), "Vehicles"), "Hover 1", "Hover 1", "2026-09-14T14:38:55.7142787", "v2026.9.7.5402", 40, "vehicle.xml");
        var entry = Assert.Single(await _store.ListSharedProfileAsync(GameSaveKind.Vehicle));

        await _store.CopyAsync(entry, _instanceId, replace: false);

        Assert.Equal("Hover 1", Assert.Single(await _store.ListAsync(_instanceId, GameSaveKind.Vehicle)).Name);
        Assert.True(Directory.Exists(entry.Path));
    }

    [Fact]
    public async Task DeleteAsync_MovesTheFolderIntoTheBackups()
    {
        var entry = await AddSaveAsync(_instanceId, "Orbit");

        var moved = await _store.DeleteAsync(_instanceId, entry);

        Assert.False(Directory.Exists(entry.Path));
        Assert.Equal(Path.Combine(_paths.GetBackupsRoot(), _instanceId.ToString(), "saves", $"Orbit-{Stamp}"), moved);
        Assert.True(File.Exists(Path.Combine(moved, "universe.xml")));
    }

    [Fact]
    public async Task DeleteAsync_FolderOfAnotherInstance_RefusesAndKeepsIt()
    {
        var entry = await AddSaveAsync(_otherInstanceId, "Orbit");

        await Assert.ThrowsAsync<ArgumentException>(() => _store.DeleteAsync(_instanceId, entry));

        Assert.True(Directory.Exists(entry.Path));
    }

    [Fact]
    public async Task CopyAsync_LockedFile_RefusesAndLeavesNoFolder()
    {
        var entry = await AddSaveAsync(_instanceId, "Orbit");
        using var locked = Lock(Path.Combine(entry.Path, "universe.xml"));

        await Assert.ThrowsAsync<GameSaveInUseException>(() => _store.CopyAsync(entry, _otherInstanceId, replace: false));

        Assert.Empty(Directory.GetFileSystemEntries(_paths.GetInstanceRoot(_otherInstanceId)));
    }

    [Fact]
    public async Task DeleteAsync_LockedFile_RefusesAndKeepsTheFolder()
    {
        var entry = await AddSaveAsync(_instanceId, "Orbit");
        using var locked = Lock(Path.Combine(entry.Path, "meta.toml"));

        await Assert.ThrowsAsync<GameSaveInUseException>(() => _store.DeleteAsync(_instanceId, entry));

        Assert.True(File.Exists(Path.Combine(entry.Path, "universe.xml")));
        Assert.False(Directory.Exists(_paths.GetBackupsRoot()));
    }

    [Fact]
    public async Task BackUpAsync_LockedFile_RefusesAndWritesNoZip()
    {
        var entry = await AddSaveAsync(_instanceId, "Orbit");
        using var locked = Lock(Path.Combine(entry.Path, "universe.xml"));

        await Assert.ThrowsAsync<GameSaveInUseException>(() => _store.BackUpAsync(_instanceId, entry));

        Assert.False(Directory.Exists(_paths.GetBackupsRoot()));
    }

    [Fact]
    public async Task BackUpAllAsync_LockedFileInOneSave_WritesNoZip()
    {
        await AddSaveAsync(_instanceId, "Moon");
        var orbit = await AddSaveAsync(_instanceId, "Orbit");
        using var locked = Lock(Path.Combine(orbit.Path, "universe.xml"));

        await Assert.ThrowsAsync<GameSaveInUseException>(() => _store.BackUpAllAsync(_instanceId, GameSaveKind.Save));

        Assert.False(Directory.Exists(_paths.GetBackupsRoot()));
    }

    [Fact]
    public async Task CopyAsync_ReplaceWithLockedTarget_RefusesAndKeepsBothFolders()
    {
        var entry = await AddSaveAsync(_instanceId, "Orbit", universeBytes: 300);
        var existing = await AddSaveAsync(_otherInstanceId, "Orbit", universeBytes: 20);
        using var locked = Lock(Path.Combine(existing.Path, "meta.toml"));

        await Assert.ThrowsAsync<GameSaveInUseException>(() => _store.CopyAsync(entry, _otherInstanceId, replace: true));

        Assert.Equal(300, new FileInfo(Path.Combine(entry.Path, "universe.xml")).Length);
        Assert.Equal(20, new FileInfo(Path.Combine(existing.Path, "universe.xml")).Length);
        Assert.False(Directory.Exists(_paths.GetBackupsRoot()));
        Assert.Equal(["saves"], Directory.GetDirectories(_paths.GetInstanceRoot(_otherInstanceId)).Select(Path.GetFileName));
    }

    private async Task<GameSaveEntry> AddSaveAsync(Guid instanceId, string name, int universeBytes = 100)
    {
        WriteItem(_paths.GetInstanceSavesFolder(instanceId), name, name, "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", universeBytes);
        return (await _store.ListAsync(instanceId, GameSaveKind.Save)).Single(entry => entry.FolderName == name);
    }

    private static FileStream Lock(string path) => new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

    /// <summary>A folder the way the game writes it, with a meta.toml in the shape of SaveMetaData.</summary>
    private static void WriteItem(string kindFolder, string folderName, string name, string updated, string version, int dataBytes, string dataFile = "universe.xml")
    {
        var folder = Directory.CreateDirectory(Path.Combine(kindFolder, folderName)).FullName;
        File.WriteAllText(Path.Combine(folder, "meta.toml"), $"""
            name = "{name}"
            created = 2026-07-03T09:15:02.1200000
            updated = {updated}
            version = "{version}"
            systems = [ "Sol", ]

            """);
        File.WriteAllBytes(Path.Combine(folder, dataFile), new byte[dataBytes]);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
