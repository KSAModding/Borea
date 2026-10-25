using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Storage.Instances;
using Borea.Storage.Tests.Mods;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Instances;

public sealed class FileInstalledContentRecordsTests : IDisposable
{
    // A name with spaces and letters outside ASCII.
    private static readonly string NonAsciiFolder = "Kitten Fl\u00fcgel \u00c6ra";

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly FileInstalledContentRecords _records;
    private readonly Guid _instanceId = Guid.NewGuid();

    public FileInstalledContentRecordsTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _records = new FileInstalledContentRecords(_paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    internal static ModVersionMetadata Release(ContentType type = ContentType.Vehicle, string id = "ExampleRover", string folder = "Example Rover") => new(
        specVersion: 1,
        modId: id,
        version: ModVersion.Parse("1.2.0"),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: MetadataFixtures.SampleTimestamp(),
        gameMin: "2026.10.19.5638",
        gameMinRevision: 5638,
        download: MetadataFixtures.SampleDownload(),
        installSizeBytes: 4096,
        dependencies: [new ModDependency("SomePartsMod", ModDependencyKind.Required, ModVersion.Parse("1.0.0"))],
        type: type,
        install: new InstallInfo(folder, derived: true, folder: folder));

    private string VehicleFolder(string name) => Path.Combine(_paths.GetInstanceVehiclesFolder(_instanceId), name);

    private static void WriteFile(string folder, string relativePath, string content)
    {
        var path = Path.Combine(folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private async Task<InstalledContentRecord> InstallVehicleAsync(string folderName = "Example Rover")
    {
        var folder = VehicleFolder(folderName);
        WriteFile(folder, "meta.toml", $"name = \"{folderName}\"\n");
        WriteFile(folder, "vehicle.xml", "<Vehicle />");
        WriteFile(folder, Path.Combine("parts", "notes.txt"), "kept by a mod");
        var record = new InstalledContentRecord(Release(folder: folderName), folderName, MetadataFixtures.SampleTimestamp(), await InstalledContentHashes.ComputeAsync(folder));
        await _records.AddAsync(_instanceId, record);
        return record;
    }

    [Fact]
    public async Task Add_RoundTripsEveryField_WithSpacesAndLettersOutsideAscii()
    {
        var installed = await InstallVehicleAsync(NonAsciiFolder);

        var reloaded = Assert.Single(await new FileInstalledContentRecords(_paths).ListAsync(_instanceId));

        Assert.Equal(ContentType.Vehicle, reloaded.Type);
        Assert.Equal("ExampleRover", reloaded.ListingId);
        Assert.Equal(ModVersion.Parse("1.2.0"), reloaded.Version);
        Assert.Equal(NonAsciiFolder, reloaded.FolderName);
        Assert.Equal(installed.InstalledAt, reloaded.InstalledAt);
        Assert.Equal(NonAsciiFolder, reloaded.Metadata.Install!.Folder);
        Assert.Equal("SomePartsMod", Assert.Single(reloaded.Metadata.Dependencies).ModId);
        Assert.Equal(installed.Files, reloaded.Files);
        Assert.Equal(["meta.toml", "parts/notes.txt", "vehicle.xml"], reloaded.Files.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Find_IgnoresTheLetterCaseOfTheFolder_AndTheTypeMatters()
    {
        await InstallVehicleAsync();

        Assert.NotNull(await _records.FindAsync(_instanceId, ContentType.Vehicle, "EXAMPLE ROVER"));
        Assert.Null(await _records.FindAsync(_instanceId, ContentType.Save, "Example Rover"));
    }

    [Fact]
    public async Task Add_ASecondRecordOfTheSameFolder_Throws()
    {
        var record = await InstallVehicleAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _records.AddAsync(_instanceId, record.WithFolderName("example rover")));
    }

    [Fact]
    public async Task ReplaceAndRemove_ChangeOnlyThatRecord()
    {
        var record = await InstallVehicleAsync();
        await InstallVehicleAsync("Second Rover");
        var replacement = new InstalledContentRecord(record.Metadata, record.FolderName, record.InstalledAt.AddDays(1), record.Files);

        await _records.ReplaceAsync(_instanceId, replacement);
        Assert.Equal(record.InstalledAt.AddDays(1), (await _records.FindAsync(_instanceId, ContentType.Vehicle, "Example Rover"))!.InstalledAt);

        Assert.True(await _records.RemoveAsync(_instanceId, ContentType.Vehicle, "Example Rover"));
        Assert.False(await _records.RemoveAsync(_instanceId, ContentType.Vehicle, "Example Rover"));
        Assert.Equal("Second Rover", Assert.Single(await _records.ListAsync(_instanceId)).FolderName);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _records.ReplaceAsync(_instanceId, replacement));
    }

    [Fact]
    public async Task RemoveTheLastRecord_DeletesTheFile()
    {
        await InstallVehicleAsync();

        await _records.RemoveAsync(_instanceId, ContentType.Vehicle, "Example Rover");

        Assert.False(File.Exists(_paths.GetInstanceInstalledContentPath(_instanceId)));
    }

    [Fact]
    public async Task Compare_UntouchedFolder_IsUnchanged()
    {
        var record = await InstallVehicleAsync();

        Assert.True((await _records.CompareAsync(_instanceId, record)).IsUnchanged);
    }

    [Fact]
    public async Task Compare_ReportsTheChangedTheAddedAndTheRemovedFile()
    {
        var record = await InstallVehicleAsync();
        var folder = VehicleFolder("Example Rover");
        WriteFile(folder, "vehicle.xml", "<Vehicle edited=\"true\" />");
        WriteFile(folder, "thumbnail.png", "new");
        File.Delete(Path.Combine(folder, "parts", "notes.txt"));

        var changes = await _records.CompareAsync(_instanceId, record);

        Assert.False(changes.IsUnchanged);
        Assert.Equal(["thumbnail.png"], changes.Added);
        Assert.Equal(["parts/notes.txt"], changes.Removed);
        Assert.Equal(["vehicle.xml"], changes.Changed);
    }

    [Fact]
    public async Task Compare_MissingFolder_HasEveryFileRemoved()
    {
        var record = await InstallVehicleAsync();
        Directory.Delete(VehicleFolder("Example Rover"), recursive: true);

        var changes = await _records.CompareAsync(_instanceId, record);

        Assert.Equal(record.Files.Keys.Order(StringComparer.Ordinal), changes.Removed);
    }

    [Fact]
    public async Task DropMissing_DropsTheRecordWhoseFolderIsGone_AndKeepsTheOther()
    {
        await InstallVehicleAsync();
        await InstallVehicleAsync("Second Rover");
        Directory.Delete(VehicleFolder("Example Rover"), recursive: true);

        var dropped = await _records.DropMissingAsync(_instanceId);

        Assert.Equal("Example Rover", Assert.Single(dropped).FolderName);
        Assert.Equal("Second Rover", Assert.Single(await _records.ListAsync(_instanceId)).FolderName);
    }

    [Fact]
    public async Task DropMissing_FindsTheFolderInAnyLetterCase()
    {
        await InstallVehicleAsync();
        var vehicles = _paths.GetInstanceVehiclesFolder(_instanceId);
        MoveInTwoSteps(VehicleFolder("Example Rover"), Path.Combine(vehicles, "example rover"));
        MoveInTwoSteps(vehicles, Path.Combine(Path.GetDirectoryName(vehicles)!, "vehicles"));

        Assert.Empty(await _records.DropMissingAsync(_instanceId));
        Assert.Single(await _records.ListAsync(_instanceId));
    }

    // A move that changes only the letter case needs a step between, because Windows sees one name.
    private static void MoveInTwoSteps(string from, string to)
    {
        Directory.Move(from, from + "-moving");
        Directory.Move(from + "-moving", to);
    }

    [Fact]
    public async Task RenameFolder_MovesTheRecordAndKeepsTheHashes()
    {
        var record = await InstallVehicleAsync();

        Assert.True(await _records.RenameFolderAsync(_instanceId, ContentType.Vehicle, "example rover", "Rover Two"));

        var renamed = Assert.Single(await _records.ListAsync(_instanceId));
        Assert.Equal("Rover Two", renamed.FolderName);
        Assert.Equal(record.Files, renamed.Files);
        Assert.False(await _records.RenameFolderAsync(_instanceId, ContentType.Vehicle, "Example Rover", "Rover Three"));
    }

    [Fact]
    public async Task RenameFolder_OntoAnotherRecord_Throws()
    {
        await InstallVehicleAsync();
        await InstallVehicleAsync("Second Rover");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _records.RenameFolderAsync(_instanceId, ContentType.Vehicle, "Example Rover", "second rover"));
    }

    [Fact]
    public async Task InvalidToml_NamesItsPath_AndTheInstanceStaysReadable()
    {
        var instances = new FileInstanceRepository(_paths);
        var instance = (await instances.CreateAsync("Rovers", InstanceSource.Custom.Value)).Instance;
        var path = _paths.GetInstanceInstalledContentPath(instance.InstanceId);
        await File.WriteAllTextAsync(path, "[[Items]\nType = ");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _records.ListAsync(instance.InstanceId));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Equal("Rovers", (await new FileInstanceRepository(_paths).GetByIdAsync(instance.InstanceId))!.Name);
    }

    [Fact]
    public async Task RecordThatDisagreesWithItsRelease_NamesItsPath()
    {
        await InstallVehicleAsync();
        var path = _paths.GetInstanceInstalledContentPath(_instanceId);
        await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace("ListingId = \"ExampleRover\"", "ListingId = \"OtherRover\"", StringComparison.Ordinal));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _records.ListAsync(_instanceId));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownKeys_AtTheRootInAnItemAndInItsRelease_AreIgnored()
    {
        await InstallVehicleAsync();
        var path = _paths.GetInstanceInstalledContentPath(_instanceId);
        var text = "Written = \"by a newer Borea\"\n" + (await File.ReadAllTextAsync(path))
            .Replace("ListingId = \"ExampleRover\"", "ListingId = \"ExampleRover\"\nPlayerNote = \"mine\"", StringComparison.Ordinal)
            .Replace("ModId = \"ExampleRover\"", "ModId = \"ExampleRover\"\nFutureField = 3", StringComparison.Ordinal);
        Assert.Contains("PlayerNote", text, StringComparison.Ordinal);
        Assert.Contains("FutureField", text, StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, text);

        var record = Assert.Single(await _records.ListAsync(_instanceId));

        Assert.Equal("Example Rover", record.FolderName);
    }

    [Fact]
    public async Task Hashes_AreLowercaseSha256ByPathWithForwardSlashes()
    {
        var folder = VehicleFolder("Example Rover");
        WriteFile(folder, Path.Combine("a", "b", "c.txt"), "abc");

        var hashes = await InstalledContentHashes.ComputeAsync(folder);

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Assert.Single(hashes, hash => hash.Key == "a/b/c.txt").Value);
        Assert.Empty(await InstalledContentHashes.ComputeAsync(VehicleFolder("Nothing Here")));
    }
}
