using System.IO.Compression;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Storage.Instances;
using Borea.Storage.Tests.Launch;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Instances;

public sealed class FileGameSaveStoreTests : IDisposable
{
    private const string Stamp = "2026-09-15T123005Z";

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly FileGameSaveStore _store;
    private readonly FileInstalledContentRecords _records;
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly Guid _otherInstanceId = Guid.NewGuid();

    public FileGameSaveStoreTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _records = new FileInstalledContentRecords(_paths);
        _store = new FileGameSaveStore(_paths, new FixedTime(new DateTimeOffset(2026, 9, 15, 12, 30, 5, TimeSpan.Zero)), _records);
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
    public async Task HasSharedProfileItemsAsync_TrueOnlyForAKindWithAFolder()
    {
        Assert.False(await _store.HasSharedProfileItemsAsync(GameSaveKind.Save));

        WriteItem(Path.Combine(_paths.GetSharedProfileRoot(), "vehicles"), "Hover 1", "Hover 1", "2026-09-14T14:38:55.7142787", "v2026.9.7.5402", 40, "vehicle.xml");

        Assert.True(await _store.HasSharedProfileItemsAsync(GameSaveKind.Vehicle));
        Assert.False(await _store.HasSharedProfileItemsAsync(GameSaveKind.Save));
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

    [WindowsFact("Only Windows keeps a reader out while another handle writes the file.")]
    public async Task CopyAsync_FileOpenForWriting_FailsInsteadOfCopyingItHalfWritten()
    {
        var entry = await AddSaveAsync(_instanceId, "Orbit");
        using var writer = new FileStream(Path.Combine(entry.Path, "universe.xml"), FileMode.Open, FileAccess.Write, FileShare.Read);

        await Assert.ThrowsAsync<IOException>(() => _store.CopyAsync(entry, _otherInstanceId, replace: false));

        Assert.Empty(Directory.GetFileSystemEntries(_paths.GetInstanceRoot(_otherInstanceId)));
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
    public async Task CopyAsync_LockedFile_FailsNamingTheFileAndLeavesNoFolder()
    {
        var entry = await AddSaveAsync(_instanceId, "Orbit");
        var file = Path.Combine(entry.Path, "universe.xml");
        using var locked = Lock(file);

        var exception = await Assert.ThrowsAsync<IOException>(() => _store.CopyAsync(entry, _otherInstanceId, replace: false));

        Assert.Contains(file, exception.Message);
        Assert.Empty(Directory.GetFileSystemEntries(_paths.GetInstanceRoot(_otherInstanceId)));
    }

    [Fact]
    public async Task CopyAsync_FileOpenForReading_Copies()
    {
        var entry = await AddSaveAsync(_instanceId, "Orbit");
        using var reader = File.OpenRead(Path.Combine(entry.Path, "universe.xml"));

        Assert.Equal(GameSaveCopyOutcome.Copied, await _store.CopyAsync(entry, _otherInstanceId, replace: false));

        Assert.True(File.Exists(Path.Combine(_paths.GetInstanceSavesFolder(_otherInstanceId), "Orbit", "universe.xml")));
    }

    [WindowsFact("Only Windows refuses to move a folder while a handle below it is open.")]
    public async Task DeleteAsync_LockedFile_FailsAndKeepsTheFolder()
    {
        var entry = await AddSaveAsync(_instanceId, "Orbit");
        using var locked = Lock(Path.Combine(entry.Path, "meta.toml"));

        await Assert.ThrowsAnyAsync<IOException>(() => _store.DeleteAsync(_instanceId, entry));

        Assert.True(File.Exists(Path.Combine(entry.Path, "universe.xml")));
        Assert.Empty(BackupEntries(_instanceId));
    }

    [Fact]
    public async Task BackUpAsync_LockedFile_FailsNamingTheFileAndWritesNoZip()
    {
        var entry = await AddSaveAsync(_instanceId, "Orbit");
        var file = Path.Combine(entry.Path, "universe.xml");
        using var locked = Lock(file);

        var exception = await Assert.ThrowsAsync<IOException>(() => _store.BackUpAsync(_instanceId, entry));

        Assert.Contains(file, exception.Message);
        Assert.Empty(BackupEntries(_instanceId));
    }

    [Fact]
    public async Task BackUpAsync_FileOpenForReading_WritesTheZip()
    {
        var entry = await AddSaveAsync(_instanceId, "Orbit");
        using var reader = File.OpenRead(Path.Combine(entry.Path, "universe.xml"));

        var zip = await _store.BackUpAsync(_instanceId, entry);

        Assert.True(File.Exists(zip));
    }

    [Fact]
    public async Task BackUpAllAsync_LockedFileInTheLastSave_KeepsNoZip()
    {
        await AddSaveAsync(_instanceId, "Moon");
        var orbit = await AddSaveAsync(_instanceId, "Orbit");
        using var locked = Lock(Path.Combine(orbit.Path, "universe.xml"));

        await Assert.ThrowsAsync<IOException>(() => _store.BackUpAllAsync(_instanceId, GameSaveKind.Save));

        Assert.Empty(BackupEntries(_instanceId));
    }

    [WindowsFact("Only Windows refuses to move a folder while a handle below it is open.")]
    public async Task CopyAsync_ReplaceWithLockedTarget_FailsAndKeepsBothFolders()
    {
        var entry = await AddSaveAsync(_instanceId, "Orbit", universeBytes: 300);
        var existing = await AddSaveAsync(_otherInstanceId, "Orbit", universeBytes: 20);
        using var locked = Lock(Path.Combine(existing.Path, "meta.toml"));

        await Assert.ThrowsAnyAsync<IOException>(() => _store.CopyAsync(entry, _otherInstanceId, replace: true));

        Assert.Equal(300, new FileInfo(Path.Combine(entry.Path, "universe.xml")).Length);
        Assert.Equal(20, new FileInfo(Path.Combine(existing.Path, "universe.xml")).Length);
        Assert.Empty(BackupEntries(_otherInstanceId));
        Assert.Equal(["saves"], Directory.GetDirectories(_paths.GetInstanceRoot(_otherInstanceId)).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData(GameSaveKind.Save, "universe.xml")]
    [InlineData(GameSaveKind.Vehicle, "vehicle.xml")]
    public async Task RenameAsync_ChangesTheNameInMetaTomlAndTheFolderTogether(GameSaveKind kind, string dataFile)
    {
        var folder = kind == GameSaveKind.Save ? _paths.GetInstanceSavesFolder(_instanceId) : _paths.GetInstanceVehiclesFolder(_instanceId);
        WriteItem(folder, "Orbit", "Orbit", "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", 100, dataFile);
        var entry = Assert.Single(await _store.ListAsync(_instanceId, kind));
        var before = File.ReadAllText(Path.Combine(entry.Path, "meta.toml"));

        var outcome = await _store.RenameAsync(_instanceId, entry, "Mun landing 2");

        Assert.Equal(GameSaveRenameOutcome.Renamed, outcome);
        Assert.False(Directory.Exists(entry.Path));
        var renamed = Path.Combine(folder, "Mun landing 2");
        Assert.Equal(100, new FileInfo(Path.Combine(renamed, dataFile)).Length);
        Assert.Equal(before.Replace("name = \"Orbit\"", "name = \"Mun landing 2\"", StringComparison.Ordinal), File.ReadAllText(Path.Combine(renamed, "meta.toml")));
        Assert.Equal(["meta.toml", dataFile], Directory.GetFiles(renamed).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        var listed = Assert.Single(await _store.ListAsync(_instanceId, kind));
        Assert.Equal("Mun landing 2", listed.Name);
        Assert.Equal("Mun landing 2", listed.FolderName);
        Assert.Equal(entry.UpdatedAt, listed.UpdatedAt);
    }

    [Fact]
    public async Task RenameAsync_FolderOfAnotherName_FollowsTheName()
    {
        var saves = _paths.GetInstanceSavesFolder(_instanceId);
        WriteItem(saves, "orbit-test", "Orbit test", "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", 100);
        var entry = Assert.Single(await _store.ListAsync(_instanceId, GameSaveKind.Save));

        Assert.Equal(GameSaveRenameOutcome.Renamed, await _store.RenameAsync(_instanceId, entry, "Orbit test"));

        Assert.Equal("Orbit test", Assert.Single(await _store.ListAsync(_instanceId, GameSaveKind.Save)).FolderName);
    }

    [Fact]
    public async Task RenameAsync_OnlyTheLetterCase_RenamesTheFolderToo()
    {
        var entry = await AddSaveAsync(_instanceId, "Orbit");

        Assert.Equal(GameSaveRenameOutcome.Renamed, await _store.RenameAsync(_instanceId, entry, "ORBIT"));

        var listed = Assert.Single(await _store.ListAsync(_instanceId, GameSaveKind.Save));
        Assert.Equal("ORBIT", listed.Name);
        Assert.Equal("ORBIT", listed.FolderName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" Orbit 2")]
    [InlineData("Orbit  2")]
    [InlineData("Orbit/2")]
    [InlineData("Orbit.2")]
    [InlineData("CON")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task RenameAsync_NameTheGameWouldChange_RefusesAndLeavesTheFolder(string name)
    {
        var entry = await AddSaveAsync(_instanceId, "Orbit");
        var before = File.ReadAllBytes(Path.Combine(entry.Path, "meta.toml"));

        Assert.Equal(GameSaveRenameOutcome.InvalidName, await _store.RenameAsync(_instanceId, entry, name));

        Assert.Equal(before, File.ReadAllBytes(Path.Combine(entry.Path, "meta.toml")));
        Assert.Equal(["Orbit"], Directory.GetDirectories(_paths.GetInstanceSavesFolder(_instanceId)).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("orbit")]
    [InlineData("Mun Base")]
    [InlineData("mun base")]
    [InlineData("MUN-BASE")]
    public async Task RenameAsync_NameOrFolderOfAnotherSave_RefusesAndLeavesTheFolder(string name)
    {
        var saves = _paths.GetInstanceSavesFolder(_instanceId);
        await AddSaveAsync(_instanceId, "Orbit");
        WriteItem(saves, "mun-base", "Mun Base", "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", 100);
        var moon = await AddSaveAsync(_instanceId, "Moon");
        var before = File.ReadAllBytes(Path.Combine(moon.Path, "meta.toml"));

        Assert.Equal(GameSaveRenameOutcome.NameTaken, await _store.RenameAsync(_instanceId, moon, name));

        Assert.Equal(before, File.ReadAllBytes(Path.Combine(moon.Path, "meta.toml")));
        Assert.Equal(["Moon", "Orbit", "mun-base"], Directory.GetDirectories(saves).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task RenameAsync_NameOfAVehicle_RenamesTheSave()
    {
        WriteItem(_paths.GetInstanceVehiclesFolder(_instanceId), "Rocket", "Rocket", "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", 10, "vehicle.xml");
        var entry = await AddSaveAsync(_instanceId, "Orbit");

        Assert.Equal(GameSaveRenameOutcome.Renamed, await _store.RenameAsync(_instanceId, entry, "Rocket"));
    }

    [Fact]
    public async Task RenameAsync_MoveFails_PutsTheOldNameBack()
    {
        var entry = await AddSaveAsync(_instanceId, "Orbit");
        var metadata = Path.Combine(entry.Path, "meta.toml");
        var before = File.ReadAllBytes(metadata);
        File.WriteAllText(Path.Combine(_paths.GetInstanceSavesFolder(_instanceId), "Mun"), "not a save");

        await Assert.ThrowsAnyAsync<IOException>(() => _store.RenameAsync(_instanceId, entry, "Mun"));

        Assert.Equal(before, File.ReadAllBytes(metadata));
        Assert.Equal(["meta.toml", "universe.xml"], Directory.GetFiles(entry.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal("Orbit", Assert.Single(await _store.ListAsync(_instanceId, GameSaveKind.Save)).Name);
    }

    [WindowsFact("Only Windows refuses to move a folder while a handle below it is open.")]
    public async Task RenameAsync_LockedFile_FailsAndPutsTheOldNameBack()
    {
        var entry = await AddSaveAsync(_instanceId, "Orbit");
        var metadata = Path.Combine(entry.Path, "meta.toml");
        var before = File.ReadAllBytes(metadata);

        using (Lock(Path.Combine(entry.Path, "universe.xml")))
            await Assert.ThrowsAnyAsync<IOException>(() => _store.RenameAsync(_instanceId, entry, "Mun"));

        Assert.Equal(before, File.ReadAllBytes(metadata));
        Assert.Equal(["Orbit"], Directory.GetDirectories(_paths.GetInstanceSavesFolder(_instanceId)).Select(Path.GetFileName));
    }

    [Fact]
    public async Task RenameAsync_NoMetaToml_RefusesAndLeavesTheFolder()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_paths.GetInstanceSavesFolder(_instanceId), "Orbit")).FullName;
        var entry = Assert.Single(await _store.ListAsync(_instanceId, GameSaveKind.Save));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _store.RenameAsync(_instanceId, entry, "Mun"));

        Assert.Contains("meta.toml", exception.Message);
        Assert.True(Directory.Exists(folder));
        Assert.Empty(Directory.GetFileSystemEntries(folder));
    }

    [Fact]
    public async Task RenameAsync_FolderOfAnotherInstance_RefusesAndKeepsIt()
    {
        var entry = await AddSaveAsync(_otherInstanceId, "Orbit");

        await Assert.ThrowsAsync<ArgumentException>(() => _store.RenameAsync(_instanceId, entry, "Mun"));

        Assert.True(Directory.Exists(entry.Path));
    }

    [Fact]
    public void WithName_KeepsCommentsAndEveryOtherValue()
    {
        const string text = "# written by the game\r\nname = \"Orbit\" # the save\r\ncreated = 2026-07-03T09:15:02.1200000\r\nsystems = [ \"Sol\", ]\r\n";

        Assert.Equal(text.Replace("\"Orbit\"", "\"Mun\"", StringComparison.Ordinal), FileGameSaveStore.WithName(text, "Mun"));
    }

    [Fact]
    public void WithName_NoName_AddsItAtTheTop()
    {
        Assert.Equal("name = \"Mun\"\nversion = \"v2026.8.3.5117\"\n", FileGameSaveStore.WithName("version = \"v2026.8.3.5117\"\n", "Mun"));
    }

    [Theory]
    [InlineData("name = [")]
    [InlineData("name = \"Orbit\"\nname = \"Moon\"\n")]
    [InlineData("name = \"\"\"Orbit\"\"\"\n")]
    [InlineData("note = \"\"\"\nname = \"Orbit\"\n\"\"\"\nname = \"Mun\"\n")]
    public void WithName_TextBoreaCannotEdit_ReturnsNull(string text)
    {
        Assert.Null(FileGameSaveStore.WithName(text, "Mun"));
    }

    [Fact]
    public async Task RenameAsync_RecordedVehicle_MovesTheRecordAndKeepsTheHashes()
    {
        var record = await AddRecordedVehicleAsync(_instanceId, "Rover");
        var entry = Assert.Single(await _store.ListAsync(_instanceId, GameSaveKind.Vehicle));

        Assert.Equal(GameSaveRenameOutcome.Renamed, await _store.RenameAsync(_instanceId, entry, "Rover Two"));

        var renamed = Assert.Single(await _records.ListAsync(_instanceId));
        Assert.Equal("Rover Two", renamed.FolderName);
        Assert.Equal(record.Files, renamed.Files);
        Assert.Equal(["meta.toml"], (await _records.CompareAsync(_instanceId, renamed)).Changed);
    }

    [Fact]
    public async Task RenameAsync_RecordWriteFails_PutsTheFolderAndMetaTomlBack()
    {
        var store = new FileGameSaveStore(_paths, records: new RenameFails(_records));
        await AddRecordedVehicleAsync(_instanceId, "Rover");
        var entry = Assert.Single(await store.ListAsync(_instanceId, GameSaveKind.Vehicle));
        var metadata = Path.Combine(entry.Path, "meta.toml");
        var before = File.ReadAllBytes(metadata);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RenameAsync(_instanceId, entry, "Rover Two"));

        Assert.Equal(before, File.ReadAllBytes(metadata));
        Assert.Equal(["Rover"], Directory.GetDirectories(_paths.GetInstanceVehiclesFolder(_instanceId)).Select(Path.GetFileName));
        Assert.Equal("Rover", Assert.Single(await _records.ListAsync(_instanceId)).FolderName);
    }

    [Fact]
    public async Task RenameAsync_UnreadableRecords_RefusesBeforeItChangesAnything()
    {
        await AddRecordedVehicleAsync(_instanceId, "Rover");
        var entry = Assert.Single(await _store.ListAsync(_instanceId, GameSaveKind.Vehicle));
        var metadata = Path.Combine(entry.Path, "meta.toml");
        var before = File.ReadAllBytes(metadata);
        await File.WriteAllTextAsync(_paths.GetInstanceInstalledContentPath(_instanceId), "[[Items]\nType = ");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.RenameAsync(_instanceId, entry, "Rover Two"));

        Assert.Equal(before, File.ReadAllBytes(metadata));
        Assert.Equal(["Rover"], Directory.GetDirectories(_paths.GetInstanceVehiclesFolder(_instanceId)).Select(Path.GetFileName));
    }

    [Fact]
    public async Task RenameAsync_RecordedVehicleOnlyTheLetterCase_KeepsItsRecord()
    {
        await AddRecordedVehicleAsync(_instanceId, "Rover");
        var entry = Assert.Single(await _store.ListAsync(_instanceId, GameSaveKind.Vehicle));

        Assert.Equal(GameSaveRenameOutcome.Renamed, await _store.RenameAsync(_instanceId, entry, "ROVER"));

        Assert.Equal("ROVER", Assert.Single(await _records.ListAsync(_instanceId)).FolderName);
    }

    [Fact]
    public async Task RenameAsync_NameOfARecordWhoseFolderIsGone_RenamesAndDropsThatRecord()
    {
        var rover = await AddRecordedVehicleAsync(_instanceId, "Rover");
        await AddRecordedVehicleAsync(_instanceId, "Lander");
        Directory.Delete(Path.Combine(_paths.GetInstanceVehiclesFolder(_instanceId), "Lander"), recursive: true);
        var entry = Assert.Single(await _store.ListAsync(_instanceId, GameSaveKind.Vehicle));

        Assert.Equal(GameSaveRenameOutcome.Renamed, await _store.RenameAsync(_instanceId, entry, "Lander"));

        var renamed = Assert.Single(await _records.ListAsync(_instanceId));
        Assert.Equal("Lander", renamed.FolderName);
        Assert.Equal(rover.Files, renamed.Files);
    }

    [Fact]
    public async Task CopyAsync_OverARecordedFolder_RemovesItsRecord_BecauseTheCopyIsThePlayersOwn()
    {
        var entry = await AddSaveAsync(_instanceId, "Orbit");
        await AddRecordedSaveAsync(_otherInstanceId, "Orbit");

        Assert.Equal(GameSaveCopyOutcome.Copied, await _store.CopyAsync(entry, _otherInstanceId, replace: true));

        Assert.Empty(await _records.ListAsync(_otherInstanceId));
    }

    [Fact]
    public async Task CopyAsync_NameExistsAndNoReplace_KeepsTheRecord()
    {
        var entry = await AddSaveAsync(_instanceId, "Orbit");
        await AddRecordedSaveAsync(_otherInstanceId, "Orbit");

        Assert.Equal(GameSaveCopyOutcome.Exists, await _store.CopyAsync(entry, _otherInstanceId, replace: false));

        Assert.Single(await _records.ListAsync(_otherInstanceId));
    }

    [Fact]
    public async Task DeleteAsync_RecordedFolder_RemovesItsRecord()
    {
        await AddRecordedSaveAsync(_instanceId, "Orbit");
        await AddRecordedSaveAsync(_instanceId, "Moon");
        var entry = (await _store.ListAsync(_instanceId, GameSaveKind.Save)).Single(item => item.FolderName == "Orbit");

        await _store.DeleteAsync(_instanceId, entry);

        Assert.Equal("Moon", Assert.Single(await _records.ListAsync(_instanceId)).FolderName);
    }

    private async Task<InstalledContentRecord> AddRecordedVehicleAsync(Guid instanceId, string name)
    {
        WriteItem(_paths.GetInstanceVehiclesFolder(instanceId), name, name, "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", 10, "vehicle.xml");
        return await AddRecordAsync(instanceId, ContentType.Vehicle, Path.Combine(_paths.GetInstanceVehiclesFolder(instanceId), name));
    }

    private async Task<InstalledContentRecord> AddRecordedSaveAsync(Guid instanceId, string name)
    {
        await AddSaveAsync(instanceId, name);
        return await AddRecordAsync(instanceId, ContentType.Save, Path.Combine(_paths.GetInstanceSavesFolder(instanceId), name));
    }

    private async Task<InstalledContentRecord> AddRecordAsync(Guid instanceId, ContentType type, string folder)
    {
        var name = Path.GetFileName(folder);
        var record = new InstalledContentRecord(
            FileInstalledContentRecordsTests.Release(type, folder: name),
            name,
            DateTimeOffset.UnixEpoch,
            await InstalledContentHashes.ComputeAsync(folder));
        await _records.AddAsync(instanceId, record);
        return record;
    }

    private async Task<GameSaveEntry> AddSaveAsync(Guid instanceId, string name, int universeBytes = 100)
    {
        WriteItem(_paths.GetInstanceSavesFolder(instanceId), name, name, "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", universeBytes);
        return (await _store.ListAsync(instanceId, GameSaveKind.Save)).Single(entry => entry.FolderName == name);
    }

    private static FileStream Lock(string path) => new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

    private string[] BackupEntries(Guid instanceId)
    {
        var folder = Path.Combine(_paths.GetBackupsRoot(), instanceId.ToString(), "saves");
        return Directory.Exists(folder) ? Directory.GetFileSystemEntries(folder) : [];
    }

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

    /// <summary>Fails the record write of a rename after the folder has moved.</summary>
    private sealed class RenameFails(IInstalledContentRecords inner) : IInstalledContentRecords
    {
        public Task<IReadOnlyList<InstalledContentRecord>> ListAsync(Guid instanceId, CancellationToken cancellationToken = default) => inner.ListAsync(instanceId, cancellationToken);

        public Task<InstalledContentRecord?> FindAsync(Guid instanceId, ContentType type, string folderName, CancellationToken cancellationToken = default) => inner.FindAsync(instanceId, type, folderName, cancellationToken);

        public Task AddAsync(Guid instanceId, InstalledContentRecord record, CancellationToken cancellationToken = default) => inner.AddAsync(instanceId, record, cancellationToken);

        public Task ReplaceAsync(Guid instanceId, InstalledContentRecord record, CancellationToken cancellationToken = default) => inner.ReplaceAsync(instanceId, record, cancellationToken);

        public Task<bool> RemoveAsync(Guid instanceId, ContentType type, string folderName, CancellationToken cancellationToken = default) => inner.RemoveAsync(instanceId, type, folderName, cancellationToken);

        public Task<bool> RenameFolderAsync(Guid instanceId, ContentType type, string oldFolderName, string newFolderName, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The record write failed.");

        public Task<InstalledContentChanges> CompareAsync(Guid instanceId, InstalledContentRecord record, CancellationToken cancellationToken = default) => inner.CompareAsync(instanceId, record, cancellationToken);

        public Task<IReadOnlyList<InstalledContentRecord>> DropMissingAsync(Guid instanceId, CancellationToken cancellationToken = default) => inner.DropMissingAsync(instanceId, cancellationToken);
    }
}
