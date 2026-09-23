using Borea.Core.Instances;
using Borea.Storage.Instances;
using Borea.Storage.Tests.Launch;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Instances;

public sealed class FileGameSaveBackupStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 30, 5, TimeSpan.Zero);

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly MovingTime _time = new(Now);
    private readonly FileGameSaveStore _saves;
    private readonly FileGameSaveBackupStore _store;
    private readonly Guid _instanceId = Guid.NewGuid();

    public FileGameSaveBackupStoreTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _saves = new FileGameSaveStore(_paths, _time);
        _store = new FileGameSaveBackupStore(_paths, _time);
        Directory.CreateDirectory(_paths.GetInstanceRoot(_instanceId));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private string InstanceBackups => Path.Combine(_paths.GetBackupsRoot(), _instanceId.ToString());

    [Fact]
    public async Task ListAsync_BackupsWithRecords_SaysWhatWhereAndWhen()
    {
        var orbit = await AddSaveAsync("orbit-test", "Orbit test");
        await _saves.BackUpAsync(_instanceId, orbit);
        _time.Now = Now.AddHours(1);
        await _saves.DeleteAsync(_instanceId, orbit);

        var backups = await _store.ListAsync(_instanceId);

        Assert.Equal(["saves/orbit-test-2026-09-15T133005Z", "saves/orbit-test-2026-09-15T123005Z.zip"], backups.Select(backup => backup.Id));
        var deleted = backups[0];
        Assert.Equal((GameSaveKind?)GameSaveKind.Save, deleted.Kind);
        Assert.Equal("orbit-test", deleted.FolderName);
        Assert.Equal("Orbit test", deleted.Name);
        Assert.Equal(Now.AddHours(1), deleted.CreatedAt);
        Assert.Equal(GameSaveBackupReason.Deleted, deleted.Reason);
        Assert.False(deleted.IsArchive);
        Assert.True(deleted.CanRestore);
        var zip = backups[1];
        Assert.Equal("Orbit test", zip.Name);
        Assert.Equal(Now, zip.CreatedAt);
        Assert.Equal(GameSaveBackupReason.BackedUp, zip.Reason);
        Assert.True(zip.IsArchive);
        Assert.Equal(new FileInfo(zip.Path).Length, zip.SizeBytes);
    }

    [Fact]
    public async Task ListAsync_BackupsWithoutRecords_DerivesFromTheNamesAndFolders()
    {
        WriteItem(Path.Combine(InstanceBackups, "Vehicles"), "Rocket-2026-09-01T080000Z", "Rocket", 10, "vehicle.xml");
        WriteItem(Path.Combine(InstanceBackups, "saves"), "Moon-2026-09-02T080000Z-2", "Moon base", 10);
        var loose = Directory.CreateDirectory(Path.Combine(InstanceBackups, "saves", "copied by hand")).FullName;
        var looseTime = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);
        Directory.SetLastWriteTimeUtc(loose, looseTime);
        var stray = Path.Combine(InstanceBackups, "notes.txt");
        File.WriteAllText(stray, "kept by hand");
        File.SetLastWriteTimeUtc(stray, looseTime.AddDays(-1));

        var backups = await _store.ListAsync(_instanceId);

        Assert.Equal(["saves/Moon-2026-09-02T080000Z-2", "Vehicles/Rocket-2026-09-01T080000Z", "saves/copied by hand", "notes.txt"], backups.Select(backup => backup.Id));
        var moon = backups[0];
        Assert.Equal("Moon", moon.FolderName);
        Assert.Equal("Moon base", moon.Name);
        Assert.Equal(new DateTimeOffset(2026, 9, 2, 8, 0, 0, TimeSpan.Zero), moon.CreatedAt);
        Assert.Equal(GameSaveBackupReason.Unknown, moon.Reason);
        Assert.Equal((GameSaveKind?)GameSaveKind.Vehicle, backups[1].Kind);
        Assert.Equal("Rocket", backups[1].FolderName);
        Assert.All(backups.Skip(2), backup =>
        {
            Assert.Null(backup.Kind);
            Assert.Null(backup.FolderName);
            Assert.False(backup.CanRestore);
        });
        Assert.Equal(new DateTimeOffset(looseTime), backups[2].CreatedAt);
    }

    [Fact]
    public async Task ListAsync_ReplacingCopy_RecordsTheReplacedFolder()
    {
        var otherInstance = Guid.NewGuid();
        WriteItem(_paths.GetInstanceSavesFolder(otherInstance), "Orbit", "Orbit copy", 300);
        var source = Assert.Single(await _saves.ListAsync(otherInstance, GameSaveKind.Save));
        await AddSaveAsync("Orbit", "Orbit", universeBytes: 20);

        await _saves.CopyAsync(source, _instanceId, replace: true);

        var replaced = Assert.Single(await _store.ListAsync(_instanceId));
        Assert.Equal(GameSaveBackupReason.Replaced, replaced.Reason);
        Assert.Equal("Orbit", replaced.FolderName);
        Assert.Equal(Now, replaced.CreatedAt);
        Assert.Equal(20, new FileInfo(Path.Combine(replaced.Path, "universe.xml")).Length);
        Assert.True(File.Exists(replaced.Path + GameSaveBackupFolder.RecordSuffix));
    }

    [Fact]
    public async Task ListAsync_ZipWithoutRecordOrStamp_IsUnknown()
    {
        var zip = Path.Combine(Directory.CreateDirectory(Path.Combine(InstanceBackups, "saves")).FullName, "by hand.zip");
        File.WriteAllBytes(zip, [1, 2, 3, 4]);

        var backup = Assert.Single(await _store.ListAsync(_instanceId));

        Assert.Equal(GameSaveBackupReason.Unknown, backup.Reason);
        Assert.False(backup.CanRestore);
    }

    [Fact]
    public async Task ListAsync_NoBackups_ListsNothing()
    {
        Assert.Empty(await _store.ListAsync(_instanceId));
    }

    [Fact]
    public async Task RestoreAsync_EmptyTarget_MovesTheFolderBack()
    {
        var orbit = await AddSaveAsync("Orbit", "Orbit", universeBytes: 300);
        await _saves.DeleteAsync(_instanceId, orbit);
        var backup = Assert.Single(await _store.ListAsync(_instanceId));

        var outcome = await _store.RestoreAsync(backup, replace: false);

        Assert.Equal(GameSaveRestoreOutcome.Restored, outcome);
        Assert.Equal(300, new FileInfo(Path.Combine(orbit.Path, "universe.xml")).Length);
        Assert.Empty(await _store.ListAsync(_instanceId));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(InstanceBackups, "saves")));
    }

    [Fact]
    public async Task RestoreAsync_OccupiedTarget_AsksFirstAndThenBacksUpTheCurrentOne()
    {
        var orbit = await AddSaveAsync("Orbit", "Orbit", universeBytes: 300);
        await _saves.DeleteAsync(_instanceId, orbit);
        var backup = Assert.Single(await _store.ListAsync(_instanceId));
        await AddSaveAsync("Orbit", "Orbit", universeBytes: 20);
        _time.Now = Now.AddMinutes(1);

        Assert.Equal(GameSaveRestoreOutcome.Exists, await _store.RestoreAsync(backup, replace: false));
        Assert.Equal(20, new FileInfo(Path.Combine(orbit.Path, "universe.xml")).Length);

        Assert.Equal(GameSaveRestoreOutcome.Restored, await _store.RestoreAsync(backup, replace: true));

        Assert.Equal(300, new FileInfo(Path.Combine(orbit.Path, "universe.xml")).Length);
        var replaced = Assert.Single(await _store.ListAsync(_instanceId));
        Assert.Equal(GameSaveBackupReason.Replaced, replaced.Reason);
        Assert.Equal("Orbit", replaced.FolderName);
        Assert.Equal(20, new FileInfo(Path.Combine(replaced.Path, "universe.xml")).Length);
    }

    [Fact]
    public async Task RestoreAsync_Zip_ExtractsTheFolderAndKeepsTheZip()
    {
        var rocket = await AddVehicleAsync("Rocket");
        var zip = await _saves.BackUpAsync(_instanceId, rocket);
        Directory.Delete(rocket.Path, recursive: true);
        var backup = Assert.Single(await _store.ListAsync(_instanceId));

        await _store.RestoreAsync(backup, replace: false);

        Assert.True(File.Exists(Path.Combine(rocket.Path, "vehicle.xml")));
        Assert.True(File.Exists(zip));
        Assert.Equal(["Vehicles"], Directory.GetDirectories(_paths.GetInstanceRoot(_instanceId)).Select(Path.GetFileName));
    }

    [Fact]
    public async Task RestoreAsync_BrokenZip_LeavesTheBackupAndTheTargetAsTheyWere()
    {
        var orbit = await AddSaveAsync("Orbit", "Orbit", universeBytes: 20);
        var zip = Path.Combine(Directory.CreateDirectory(Path.Combine(InstanceBackups, "saves")).FullName, "Orbit-2026-09-01T080000Z.zip");
        File.WriteAllBytes(zip, [1, 2, 3, 4]);
        var backup = Assert.Single(await _store.ListAsync(_instanceId));

        await Assert.ThrowsAnyAsync<IOException>(() => _store.RestoreAsync(backup, replace: true));

        Assert.Equal(20, new FileInfo(Path.Combine(orbit.Path, "universe.xml")).Length);
        Assert.Equal([4L], Directory.GetFiles(Path.Combine(InstanceBackups, "saves")).Select(file => new FileInfo(file).Length));
        Assert.Equal(["saves"], Directory.GetFileSystemEntries(_paths.GetInstanceRoot(_instanceId)).Select(Path.GetFileName));
    }

    [WindowsFact("Only Windows refuses to move a folder while a handle below it is open.")]
    public async Task RestoreAsync_LockedFileInTheTarget_FailsAndChangesNothing()
    {
        var orbit = await AddSaveAsync("Orbit", "Orbit", universeBytes: 300);
        await _saves.DeleteAsync(_instanceId, orbit);
        var backup = Assert.Single(await _store.ListAsync(_instanceId));
        await AddSaveAsync("Orbit", "Orbit", universeBytes: 20);
        using (new FileStream(Path.Combine(orbit.Path, "universe.xml"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsAnyAsync<IOException>(() => _store.RestoreAsync(backup, replace: true));

        Assert.Equal(20, new FileInfo(Path.Combine(orbit.Path, "universe.xml")).Length);
        Assert.Equal(backup, Assert.Single(await _store.ListAsync(_instanceId)));
    }

    [WindowsFact("Only Windows refuses to move a folder while a handle below it is open.")]
    public async Task RestoreAsync_MoveIntoPlaceFails_PutsTheCurrentFolderBack()
    {
        var orbit = await AddSaveAsync("Orbit", "Orbit", universeBytes: 300);
        await _saves.DeleteAsync(_instanceId, orbit);
        var held = Directory.CreateDirectory(Path.Combine(InstanceBackups, "saves", "Orbit-2026-09-15T123005Z", "held")).FullName;
        var backup = Assert.Single(await _store.ListAsync(_instanceId));
        await AddSaveAsync("Orbit", "Orbit", universeBytes: 20);
        _time.Now = Now.AddMinutes(1);

        using (new FileSystemWatcher(held) { EnableRaisingEvents = true })
            await Assert.ThrowsAnyAsync<IOException>(() => _store.RestoreAsync(backup, replace: true));

        Assert.Equal(20, new FileInfo(Path.Combine(orbit.Path, "universe.xml")).Length);
        Assert.Equal(backup, Assert.Single(await _store.ListAsync(_instanceId)));
        Assert.Equal([backup.Path + GameSaveBackupFolder.RecordSuffix], Directory.GetFiles(Path.Combine(InstanceBackups, "saves")));
    }

    [Fact]
    public async Task RestoreAsync_UnknownOrigin_Refuses()
    {
        Directory.CreateDirectory(Path.Combine(InstanceBackups, "saves", "copied by hand"));
        var backup = Assert.Single(await _store.ListAsync(_instanceId));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.RestoreAsync(backup, replace: true));

        Assert.False(Directory.Exists(_paths.GetInstanceSavesFolder(_instanceId)));
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheBackupAndItsRecord()
    {
        var orbit = await AddSaveAsync("Orbit", "Orbit");
        await _saves.BackUpAsync(_instanceId, orbit);
        var backup = Assert.Single(await _store.ListAsync(_instanceId));

        await _store.DeleteAsync(backup);

        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(InstanceBackups, "saves")));
        Assert.True(Directory.Exists(orbit.Path));
    }

    [Fact]
    public async Task DeleteAsync_IdOutsideTheBackups_Refuses()
    {
        var orbit = await AddSaveAsync("Orbit", "Orbit");
        var outside = new GameSaveBackup(_instanceId, "../../Instances/" + _instanceId + "/saves/Orbit", orbit.Path, GameSaveKind.Save, "Orbit", "Orbit", Now, GameSaveBackupReason.Deleted, false, 0);

        await Assert.ThrowsAsync<ArgumentException>(() => _store.DeleteAsync(outside));

        Assert.True(Directory.Exists(orbit.Path));
    }

    [Fact]
    public async Task DeleteOlderThanAsync_DeletesOnlyTheOlderBackupsOfEveryInstance()
    {
        var otherInstance = Guid.NewGuid();
        WriteItem(Path.Combine(InstanceBackups, "saves"), "Old-2026-06-01T080000Z", "Old", 10);
        WriteItem(Path.Combine(InstanceBackups, "saves"), "New-2026-09-10T080000Z", "New", 10);
        WriteItem(Path.Combine(_paths.GetBackupsRoot(), otherInstance.ToString(), "Vehicles"), "Rocket-2026-05-01T080000Z", "Rocket", 10, "vehicle.xml");
        Directory.CreateDirectory(Path.Combine(_paths.GetBackupsRoot(), "not an instance", "saves", "Kept-2020-01-01T000000Z"));

        var deleted = await _store.DeleteOlderThanAsync(Now.AddDays(-30));

        Assert.Equal(2, deleted);
        Assert.Equal(["New"], (await _store.ListAsync(_instanceId)).Select(backup => backup.FolderName));
        Assert.Empty(await _store.ListAsync(otherInstance));
        Assert.True(Directory.Exists(Path.Combine(_paths.GetBackupsRoot(), "not an instance", "saves", "Kept-2020-01-01T000000Z")));
    }

    [Fact]
    public async Task DeleteOlderThanAsync_NoBackupsFolder_DeletesNothing()
    {
        Assert.Equal(0, await _store.DeleteOlderThanAsync(Now));
    }

    private async Task<GameSaveEntry> AddSaveAsync(string folderName, string name, int universeBytes = 100)
    {
        WriteItem(_paths.GetInstanceSavesFolder(_instanceId), folderName, name, universeBytes);
        return (await _saves.ListAsync(_instanceId, GameSaveKind.Save)).Single(entry => entry.FolderName == folderName);
    }

    private async Task<GameSaveEntry> AddVehicleAsync(string folderName)
    {
        WriteItem(_paths.GetInstanceVehiclesFolder(_instanceId), folderName, folderName, 40, "vehicle.xml");
        return (await _saves.ListAsync(_instanceId, GameSaveKind.Vehicle)).Single(entry => entry.FolderName == folderName);
    }

    private static void WriteItem(string parent, string folderName, string name, int dataBytes, string dataFile = "universe.xml")
    {
        var folder = Directory.CreateDirectory(Path.Combine(parent, folderName)).FullName;
        File.WriteAllText(Path.Combine(folder, "meta.toml"), $"""
            name = "{name}"
            updated = 2026-08-01T14:34:32.4054896
            version = "v2026.8.3.5117"

            """);
        File.WriteAllBytes(Path.Combine(folder, dataFile), new byte[dataBytes]);
    }

    private sealed class MovingTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
