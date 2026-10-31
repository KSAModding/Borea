using System.IO.Compression;
using System.Text;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.UserContent;
using Borea.Storage.Instances;
using Borea.Storage.Tests.Mods;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Instances;

public sealed class FileUserContentInstallerTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly FileInstalledContentRecords _records;
    private readonly FakeModDownloader _downloader = new();
    private readonly Guid _instanceId = Guid.NewGuid();
    private bool _gameRunning;

    public FileUserContentInstallerTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _records = new FileInstalledContentRecords(_paths);
        Directory.CreateDirectory(_paths.GetInstanceRoot(_instanceId));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    public static TheoryData<string?, string?> Layouts => new()
    {
        { "Example Rover/", null },
        { "", null },
        { "build/out/", "build/out" },
    };

    [Theory]
    [MemberData(nameof(Layouts))]
    public async Task Install_Vehicle_PutsTheFolderUnderVehiclesWithItsRecordAndHashes(string? prefix, string? root)
    {
        _downloader.Bytes = Archive(ContentType.Vehicle, "Example Rover", prefix!);

        var result = await Installer().InstallAsync(_instanceId, Release(root: root), name: null);

        Assert.Equal(UserContentOutcome.Installed, result.Outcome);
        var folder = VehicleFolder("Example Rover");
        Assert.Equal("<Vehicle />", File.ReadAllText(Path.Combine(folder, "vehicle.xml")));
        var record = (await _records.FindAsync(_instanceId, ContentType.Vehicle, "Example Rover"))!;
        Assert.Equal("ExampleRover", record.ListingId);
        Assert.Equal(await InstalledContentHashes.ComputeAsync(folder), record.Files);
        Assert.Equal(["meta.toml", "parts/notes.txt", "vehicle.xml"], record.Files.Keys.Order(StringComparer.Ordinal));
        AssertNoStaging();
    }

    [Fact]
    public async Task Install_Save_PutsTheFolderUnderSaves()
    {
        _downloader.Bytes = Archive(ContentType.Save, "First Orbit", "First Orbit/");

        var result = await Installer().InstallAsync(_instanceId, Release(ContentType.Save, "FirstOrbit", "First Orbit"), name: null);

        Assert.Equal(UserContentOutcome.Installed, result.Outcome);
        Assert.True(File.Exists(Path.Combine(_paths.GetInstanceSavesFolder(_instanceId), "First Orbit", "universe.xml")));
        Assert.NotNull(await _records.FindAsync(_instanceId, ContentType.Save, "First Orbit"));
    }

    [Fact]
    public async Task Install_FolderOfTheSameNameInAnotherCase_IsNameTakenAndChangesNothing()
    {
        WriteVehicle("example rover", "Another Name");
        var before = Snapshot();

        var result = await Installer().InstallAsync(_instanceId, Release(), name: null);

        Assert.Equal(UserContentOutcome.NameTaken, result.Outcome);
        Assert.Empty(_downloader.ArchivePaths);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Install_MetaTomlWithThatNameUnderAnotherFolderName_IsNameTakenAndChangesNothing()
    {
        WriteVehicle("Old Copy", "EXAMPLE ROVER");
        var before = Snapshot();

        var result = await Installer().InstallAsync(_instanceId, Release(), name: null);

        Assert.Equal(UserContentOutcome.NameTaken, result.Outcome);
        Assert.Empty(_downloader.ArchivePaths);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Install_WithANewName_TheFolderAndMetaTomlCarryIt_AndTheOtherFolderStays()
    {
        WriteVehicle("Example Rover", "Example Rover");
        var other = File.ReadAllBytes(Path.Combine(VehicleFolder("Example Rover"), "meta.toml"));
        _downloader.Bytes = Archive(ContentType.Vehicle, "Example Rover", "Example Rover/");

        var result = await Installer().InstallAsync(_instanceId, Release(), name: "Example Rover 2");

        Assert.Equal((UserContentOutcome.Installed, "Example Rover 2"), (result.Outcome, result.FolderName));
        Assert.StartsWith("name = \"Example Rover 2\"", File.ReadAllText(Path.Combine(VehicleFolder("Example Rover 2"), "meta.toml")), StringComparison.Ordinal);
        Assert.Equal(other, File.ReadAllBytes(Path.Combine(VehicleFolder("Example Rover"), "meta.toml")));
        var record = (await _records.FindAsync(_instanceId, ContentType.Vehicle, "Example Rover 2"))!;
        Assert.Equal(await InstalledContentHashes.ComputeAsync(VehicleFolder("Example Rover 2")), record.Files);
        Assert.Null(await _records.FindAsync(_instanceId, ContentType.Vehicle, "Example Rover"));
    }

    [Fact]
    public async Task Install_NameTheGameWouldChange_IsInvalidName()
    {
        var result = await Installer().InstallAsync(_instanceId, Release(), name: "Rover/2");

        Assert.Equal(UserContentOutcome.InvalidName, result.Outcome);
        Assert.Empty(_downloader.ArchivePaths);
    }

    public static TheoryData<string, string, string> BrokenArchives => new()
    {
        { "name = \"Example Rover", "<Vehicle />", "not TOML" },
        { "name = \"Another Rover\"\n", "<Vehicle />", "has the name 'Another Rover'" },
        { "name = \"Example Rover\"\n", "", "has no vehicle.xml" },
        { "name = \"Example Rover\"\n", "<Vehicle><Part>", "not well-formed XML" },
    };

    [Theory]
    [MemberData(nameof(BrokenArchives))]
    public async Task Install_ArchiveTheGameWouldNotRead_ChangesNothing(string metadata, string vehicle, string message)
    {
        var entries = new List<(string, string)> { ("meta.toml", metadata), ("parts/notes.txt", "notes") };
        if (vehicle.Length > 0)
            entries.Add(("vehicle.xml", vehicle));
        _downloader.Bytes = Zip([.. entries]);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Installer().InstallAsync(_instanceId, Release(), name: null));

        Assert.Contains(message, failure.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_paths.GetInstanceVehiclesFolder(_instanceId)));
        Assert.Empty(await _records.ListAsync(_instanceId));
        AssertNoStaging();
    }

    [Fact]
    public async Task Install_GameFileInAnotherLetterCase_IsRefused()
    {
        _downloader.Bytes = Zip(("meta.toml", "name = \"Example Rover\"\n"), ("Vehicle.xml", "<Vehicle />"));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Installer().InstallAsync(_instanceId, Release(), name: null));

        Assert.Contains("has no vehicle.xml", failure.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, string, string> UnsafeFiles => new()
    {
        { "Plugins/Rover.dll", "MZ", "'Plugins/Rover.dll', a .dll file" },
        { "mods/ExampleMod/state/deep/run.ps1", "Write-Host", "'mods/ExampleMod/state/deep/run.ps1', a .ps1 file" },
        { "parts/SETUP.EXE", "MZ", "'parts/SETUP.EXE', a .exe file" },
        { "parts/Setup.exe. ", "MZ", "a .exe file" },
        { "Rover.app/Contents/MacOS/Rover", "", "'Rover.app', a .app folder" },
        { "parts/notes.txt:Zone.Identifier", "[ZoneTransfer]", "':'" },
        { "mods/ExampleMod/state%41.xml", "<!DOCTYPE x [<!ENTITY a \"b\">]><x>&a;</x>", "'mods/ExampleMod/state%41.xml'" },
    };

    [Theory]
    [MemberData(nameof(UnsafeFiles))]
    public async Task Install_ArchiveWithCodeOrUnsafeNameOrXml_ChangesNothing(string entry, string content, string message)
    {
        _downloader.Bytes = Zip(("meta.toml", "name = \"Example Rover\"\n"), ("vehicle.xml", "<Vehicle />"), (entry, content));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Installer().InstallAsync(_instanceId, Release(), name: null));

        Assert.Contains(message, failure.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_paths.GetInstanceVehiclesFolder(_instanceId)));
        Assert.Empty(await _records.ListAsync(_instanceId));
        AssertNoStaging();
    }

    [Fact]
    public async Task Install_HashMismatch_ChangesNothing()
    {
        _downloader.Failure = new DownloadFailedException("The archive did not match its hash.");

        await Assert.ThrowsAsync<DownloadFailedException>(() => Installer().InstallAsync(_instanceId, Release(), name: null));

        Assert.False(Directory.Exists(_paths.GetInstanceVehiclesFolder(_instanceId)));
        Assert.Empty(await _records.ListAsync(_instanceId));
        AssertNoStaging();
    }

    [Fact]
    public async Task Install_WhileTheGameRuns_ChangesNothing()
    {
        _gameRunning = true;

        var result = await Installer().InstallAsync(_instanceId, Release(), name: null);

        Assert.Equal(UserContentOutcome.GameRunning, result.Outcome);
        Assert.Empty(_downloader.ArchivePaths);
    }

    [Fact]
    public async Task Install_RunsTheStepBeforeTheFolderMoves()
    {
        _downloader.Bytes = Archive(ContentType.Vehicle, "Example Rover", "Example Rover/");
        var folderDuringStep = true;

        await Installer().InstallAsync(_instanceId, Release(), name: null, beforeMove: (_, _) =>
        {
            folderDuringStep = Directory.Exists(VehicleFolder("Example Rover"));
            return Task.CompletedTask;
        });

        Assert.False(folderDuringStep);
        Assert.True(Directory.Exists(VehicleFolder("Example Rover")));
    }

    [Fact]
    public async Task Install_ListingThatIsRecorded_IsAlreadyInstalled()
    {
        _downloader.Bytes = Archive(ContentType.Vehicle, "Example Rover", "Example Rover/");
        await Installer().InstallAsync(_instanceId, Release(), name: "Renamed Rover");

        var result = await Installer().CheckInstallAsync(_instanceId, Release(version: "2.0.0"), name: null);

        Assert.Equal((UserContentOutcome.AlreadyInstalled, "Renamed Rover"), (result.Outcome, result.FolderName));
    }

    [Fact]
    public async Task Update_UnchangedItem_ReplacesTheFolderAndKeepsTheOldOneInTheBackups()
    {
        var installed = await InstallAsync();
        _downloader.Bytes = Archive(ContentType.Vehicle, "Example Rover", "Example Rover/", vehicle: "<Vehicle version=\"2\" />");

        var result = await Installer().UpdateAsync(_instanceId, installed, Release(version: "2.0.0"), name: null, discardChanges: false);

        Assert.Equal(UserContentOutcome.Updated, result.Outcome);
        Assert.Equal("<Vehicle version=\"2\" />", File.ReadAllText(Path.Combine(VehicleFolder("Example Rover"), "vehicle.xml")));
        Assert.Equal(ModVersion.Parse("2.0.0"), (await _records.FindAsync(_instanceId, ContentType.Vehicle, "Example Rover"))!.Version);
        var backup = Assert.Single(Directory.GetDirectories(BackupFolder()));
        Assert.Equal("<Vehicle />", File.ReadAllText(Path.Combine(backup, "vehicle.xml")));
        Assert.Equal("replaced", (await GameSaveBackupFolder.TryReadRecordAsync(backup, CancellationToken.None))!.Reason);
    }

    [Fact]
    public async Task Update_ChangedItem_NamesTheFilesAndChangesNothing_UntilTheChangesMayGo()
    {
        var installed = await InstallAsync();
        File.WriteAllText(Path.Combine(VehicleFolder("Example Rover"), "vehicle.xml"), "<Vehicle edited=\"yes\" />");
        _downloader.Bytes = Archive(ContentType.Vehicle, "Example Rover", "Example Rover/", vehicle: "<Vehicle version=\"2\" />");

        var declined = await Installer().UpdateAsync(_instanceId, installed, Release(version: "2.0.0"), name: null, discardChanges: false);

        Assert.Equal(UserContentOutcome.Changed, declined.Outcome);
        Assert.Equal(["vehicle.xml"], declined.Changes!.Changed);
        Assert.Empty(_downloader.ArchivePaths.Skip(1));
        Assert.Equal(ModVersion.Parse("1.0.0"), (await _records.FindAsync(_instanceId, ContentType.Vehicle, "Example Rover"))!.Version);

        var confirmed = await Installer().UpdateAsync(_instanceId, installed, Release(version: "2.0.0"), name: null, discardChanges: true);

        Assert.Equal(UserContentOutcome.Updated, confirmed.Outcome);
        Assert.Equal("<Vehicle edited=\"yes\" />", File.ReadAllText(Path.Combine(Assert.Single(Directory.GetDirectories(BackupFolder())), "vehicle.xml")));
    }

    [Fact]
    public async Task Remove_ChangedItem_NeedsTheGo_ThenMovesItIntoTheBackupsAndDropsTheRecord()
    {
        var installed = await InstallAsync();
        File.WriteAllText(Path.Combine(VehicleFolder("Example Rover"), "added.txt"), "mine");

        var declined = await Installer().RemoveAsync(_instanceId, installed, discardChanges: false);

        Assert.Equal(UserContentOutcome.Changed, declined.Outcome);
        Assert.Equal(["added.txt"], declined.Changes!.Added);
        Assert.True(Directory.Exists(VehicleFolder("Example Rover")));

        var removed = await Installer().RemoveAsync(_instanceId, installed, discardChanges: true);

        Assert.Equal(UserContentOutcome.Removed, removed.Outcome);
        Assert.False(Directory.Exists(VehicleFolder("Example Rover")));
        Assert.Empty(await _records.ListAsync(_instanceId));
        var backup = Assert.Single(Directory.GetDirectories(BackupFolder()));
        Assert.Equal("deleted", (await GameSaveBackupFolder.TryReadRecordAsync(backup, CancellationToken.None))!.Reason);
    }

    [Fact]
    public async Task Update_FailedMove_RestoresTheOldFolderAndRecord()
    {
        var installed = await InstallAsync();
        var oldHashes = installed.Files;
        _downloader.Bytes = Archive(ContentType.Vehicle, "New Rover", "New Rover/");

        // A file is no item of the game, so the name check lets it pass, and the move onto it fails.
        File.WriteAllText(VehicleFolder("New Rover"), "in the way");

        await Assert.ThrowsAnyAsync<IOException>(() => Installer().UpdateAsync(_instanceId, installed, Release(version: "2.0.0", folder: "New Rover"), name: null, discardChanges: false));

        Assert.Equal(oldHashes, await InstalledContentHashes.ComputeAsync(VehicleFolder("Example Rover")));
        Assert.Equal(ModVersion.Parse("1.0.0"), (await _records.FindAsync(_instanceId, ContentType.Vehicle, "Example Rover"))!.Version);
        Assert.Empty(Directory.GetFileSystemEntries(BackupFolder()));
        AssertNoStaging();
    }

    [Fact]
    public async Task Update_FailedRecordWrite_RestoresTheOldFolder()
    {
        var installed = await InstallAsync();
        _downloader.Bytes = Archive(ContentType.Vehicle, "Example Rover", "Example Rover/", vehicle: "<Vehicle version=\"2\" />");
        var installer = new FileUserContentInstaller(_paths, _downloader, new FailingReplace(_records), () => _gameRunning);

        await Assert.ThrowsAsync<IOException>(() => installer.UpdateAsync(_instanceId, installed, Release(version: "2.0.0"), name: null, discardChanges: false));

        Assert.Equal("<Vehicle />", File.ReadAllText(Path.Combine(VehicleFolder("Example Rover"), "vehicle.xml")));
        Assert.Equal(ModVersion.Parse("1.0.0"), (await _records.FindAsync(_instanceId, ContentType.Vehicle, "Example Rover"))!.Version);
        Assert.Empty(Directory.GetFileSystemEntries(BackupFolder()));
        AssertNoStaging();
    }

    [Fact]
    public async Task Update_NameChosenAtInstall_SurvivesAReleaseWithAnotherFolder()
    {
        WriteVehicle("Example Rover", "Example Rover");
        var installed = await InstallAsync(name: "My Rover");
        _downloader.Bytes = Archive(ContentType.Vehicle, "Rover Mk2", "Rover Mk2/");

        var result = await Installer().UpdateAsync(_instanceId, installed, Release(version: "2.0.0", folder: "Rover Mk2"), name: null, discardChanges: false);

        Assert.Equal((UserContentOutcome.Updated, "My Rover"), (result.Outcome, result.FolderName));
        Assert.StartsWith("name = \"My Rover\"", File.ReadAllText(Path.Combine(VehicleFolder("My Rover"), "meta.toml")), StringComparison.Ordinal);
        Assert.False(Directory.Exists(VehicleFolder("Rover Mk2")));
    }

    [Fact]
    public async Task Update_ReleaseWithAnotherFolder_MovesTheItemUnderTheNewName()
    {
        var installed = await InstallAsync();
        _downloader.Bytes = Archive(ContentType.Vehicle, "Rover Mk2", "Rover Mk2/");

        var result = await Installer().UpdateAsync(_instanceId, installed, Release(version: "2.0.0", folder: "Rover Mk2"), name: null, discardChanges: false);

        Assert.Equal((UserContentOutcome.Updated, "Rover Mk2"), (result.Outcome, result.FolderName));
        Assert.False(Directory.Exists(VehicleFolder("Example Rover")));
        Assert.Equal("Rover Mk2", Assert.Single(await _records.ListAsync(_instanceId)).FolderName);
    }

    [Fact]
    public async Task Update_ToTheNameOfARecordWhoseFolderIsGone_DropsThatRecord()
    {
        var installed = await InstallAsync();
        _downloader.Bytes = Archive(ContentType.Vehicle, "Rover Mk2", "Rover Mk2/");
        await Installer().InstallAsync(_instanceId, Release(id: "OtherRover", folder: "Rover Mk2"), name: null);
        Directory.Delete(VehicleFolder("Rover Mk2"), recursive: true);

        var result = await Installer().UpdateAsync(_instanceId, installed, Release(version: "2.0.0", folder: "Rover Mk2"), name: null, discardChanges: false);

        Assert.Equal((UserContentOutcome.Updated, "Rover Mk2"), (result.Outcome, result.FolderName));
        var record = Assert.Single(await _records.ListAsync(_instanceId));
        Assert.Equal(("ExampleRover", "Rover Mk2"), (record.ListingId, record.FolderName));
    }

    [Fact]
    public async Task UpdateAndRemove_FolderWithoutARecord_IsNotInstalled()
    {
        var installed = await InstallAsync();
        await _records.RemoveAsync(_instanceId, ContentType.Vehicle, "Example Rover");

        Assert.Equal(UserContentOutcome.NotInstalled, (await Installer().RemoveAsync(_instanceId, installed, discardChanges: true)).Outcome);
        Assert.Equal(UserContentOutcome.NotInstalled, (await Installer().UpdateAsync(_instanceId, installed, Release(version: "2.0.0"), name: null, discardChanges: true)).Outcome);
        Assert.True(Directory.Exists(VehicleFolder("Example Rover")));
    }

    private FileUserContentInstaller Installer() => new(_paths, _downloader, _records, () => _gameRunning);

    private async Task<InstalledContentRecord> InstallAsync(string? name = null)
    {
        _downloader.Bytes = Archive(ContentType.Vehicle, "Example Rover", "Example Rover/");
        var result = await Installer().InstallAsync(_instanceId, Release(), name);
        Assert.Equal(UserContentOutcome.Installed, result.Outcome);
        return result.Record!;
    }

    internal static ModVersionMetadata Release(ContentType type = ContentType.Vehicle, string id = "ExampleRover", string folder = "Example Rover", string version = "1.0.0", string? root = null) => new(
        specVersion: 1,
        modId: id,
        version: ModVersion.Parse(version),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: MetadataFixtures.SampleTimestamp(),
        gameMin: "2026.10.19.5638",
        gameMinRevision: 5638,
        download: MetadataFixtures.SampleDownload(),
        installSizeBytes: 4096,
        dependencies: [],
        type: type,
        install: new InstallInfo(root, derived: false, folder: folder));

    internal static byte[] Archive(ContentType type, string name, string prefix, string vehicle = "<Vehicle />") =>
        type == ContentType.Save
            ? Zip(($"{prefix}meta.toml", $"name = \"{name}\"\n"), ($"{prefix}universe.xml", "<Universe />"), ($"{prefix}mods/ExampleMod/state.xml", "<State />"))
            : Zip([($"{prefix}meta.toml", $"name = \"{name}\"\nversion = \"2026.10.19.5638\"\n"), ($"{prefix}vehicle.xml", vehicle), ($"{prefix}parts/notes.txt", "notes"), .. prefix.Length == 0 ? Array.Empty<(string, string)>() : [("README.md", "outside the root")]]);

    internal static byte[] Zip(params (string Path, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private string VehicleFolder(string name) => Path.Combine(_paths.GetInstanceVehiclesFolder(_instanceId), name);

    private string BackupFolder() => Directory.CreateDirectory(Path.Combine(_paths.GetBackupsRoot(), _instanceId.ToString(), "Vehicles")).FullName;

    private void WriteVehicle(string folderName, string name)
    {
        var folder = VehicleFolder(folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "meta.toml"), $"name = \"{name}\"\n");
        File.WriteAllText(Path.Combine(folder, "vehicle.xml"), "<Vehicle mine=\"yes\" />");
    }

    private string Snapshot()
    {
        var root = _paths.GetInstanceRoot(_instanceId);
        return string.Join("\n", Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => File.Exists(path) ? $"{path}:{File.ReadAllText(path)}" : path));
    }

    private void AssertNoStaging() =>
        Assert.DoesNotContain(Directory.EnumerateDirectories(_paths.GetInstanceRoot(_instanceId)), path => Path.GetFileName(path).StartsWith(".borea-", StringComparison.Ordinal));

    private sealed class FailingReplace(IInstalledContentRecords inner) : IInstalledContentRecords
    {
        public Task<IReadOnlyList<InstalledContentRecord>> ListAsync(Guid instanceId, CancellationToken cancellationToken = default) => inner.ListAsync(instanceId, cancellationToken);

        public Task<InstalledContentRecord?> FindAsync(Guid instanceId, ContentType type, string folderName, CancellationToken cancellationToken = default) => inner.FindAsync(instanceId, type, folderName, cancellationToken);

        public Task AddAsync(Guid instanceId, InstalledContentRecord record, CancellationToken cancellationToken = default) => inner.AddAsync(instanceId, record, cancellationToken);

        public Task ReplaceAsync(Guid instanceId, string oldFolderName, InstalledContentRecord record, CancellationToken cancellationToken = default) => throw new IOException("The record file is locked.");

        public Task<bool> RemoveAsync(Guid instanceId, ContentType type, string folderName, CancellationToken cancellationToken = default) => inner.RemoveAsync(instanceId, type, folderName, cancellationToken);

        public Task<bool> RenameFolderAsync(Guid instanceId, ContentType type, string oldFolderName, string newFolderName, CancellationToken cancellationToken = default) => inner.RenameFolderAsync(instanceId, type, oldFolderName, newFolderName, cancellationToken);

        public Task<InstalledContentChanges> CompareAsync(Guid instanceId, InstalledContentRecord record, CancellationToken cancellationToken = default) => inner.CompareAsync(instanceId, record, cancellationToken);

        public Task<IReadOnlyList<InstalledContentRecord>> DropMissingAsync(Guid instanceId, CancellationToken cancellationToken = default) => inner.DropMissingAsync(instanceId, cancellationToken);
    }
}
