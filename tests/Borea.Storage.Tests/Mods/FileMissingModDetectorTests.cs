using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Storage.Instances;
using Borea.Storage.Mods;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Mods;

public sealed class FileMissingModDetectorTests : IAsyncLifetime
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private TestGamePathProvider _paths = null!;
    private FileInstanceRepository _instances = null!;
    private FileMissingModDetector _detector = null!;
    private Guid _instanceId;

    public async Task InitializeAsync()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _instances = new FileInstanceRepository(_paths);
        _instanceId = (await _instances.CreateAsync("Test", InstanceSource.Custom.Value)).Instance.InstanceId;
        _detector = new FileMissingModDetector(_paths, _instances);
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task ScanAsync_EveryFolderIsThere_FindsNothing()
    {
        await RecordAsync("FlightTools");
        WriteMod("FlightTools");

        Assert.Empty(await _detector.ScanAsync(_instanceId));
    }

    [Fact]
    public async Task ScanAsync_FolderIsGone_NamesTheMod()
    {
        await RecordAsync("FlightTools");
        await RecordAsync("HudCore");
        WriteMod("HudCore");

        Assert.Equal(new[] { "FlightTools" }, await _detector.ScanAsync(_instanceId));
    }

    [Fact]
    public async Task ScanAsync_FolderWithoutAModToml_NamesTheMod()
    {
        await RecordAsync("FlightTools");
        Directory.CreateDirectory(Path.Combine(ModsFolder, "FlightTools"));

        Assert.Equal(new[] { "FlightTools" }, await _detector.ScanAsync(_instanceId));
    }

    [Fact]
    public async Task ScanAsync_FolderSpelledDifferently_FindsNothing()
    {
        await RecordAsync("FlightTools");
        WriteMod("flighttools");

        Assert.Empty(await _detector.ScanAsync(_instanceId));
    }

    [Fact]
    public async Task ScanAsync_UnknownInstance_Throws()
        => await Assert.ThrowsAsync<InvalidOperationException>(() => _detector.ScanAsync(Guid.NewGuid()));

    [Fact]
    public async Task DropAsync_FolderIsGone_RemovesOnlyThatRecord()
    {
        await RecordAsync("FlightTools");
        await RecordAsync("HudCore");
        WriteMod("HudCore");

        Assert.True(await _detector.DropAsync(_instanceId, "flighttools"));

        var saved = await _instances.GetByIdAsync(_instanceId);
        Assert.Equal(new[] { "HudCore" }, saved!.Mods.Select(mod => mod.ModId));
    }

    [Fact]
    public async Task DropAsync_FolderIsThere_KeepsTheRecord()
    {
        await RecordAsync("FlightTools");
        WriteMod("FlightTools");

        Assert.False(await _detector.DropAsync(_instanceId, "FlightTools"));

        var saved = await _instances.GetByIdAsync(_instanceId);
        Assert.Single(saved!.Mods);
    }

    [Fact]
    public async Task DropAsync_ModIsNotRecorded_ChangesNothing()
        => Assert.False(await _detector.DropAsync(_instanceId, "FlightTools"));

    [Fact]
    public async Task FindLeftoverFolderAsync_FolderWithoutAModToml_NamesTheFolder()
    {
        await RecordAsync("FlightTools");
        Directory.CreateDirectory(Path.Combine(ModsFolder, "flighttools"));

        Assert.Equal("flighttools", await _detector.FindLeftoverFolderAsync(_instanceId, "FlightTools"));
    }

    [Fact]
    public async Task FindLeftoverFolderAsync_FolderIsGone_FindsNothing()
    {
        await RecordAsync("FlightTools");

        Assert.Null(await _detector.FindLeftoverFolderAsync(_instanceId, "FlightTools"));
    }

    [Fact]
    public async Task FindLeftoverFolderAsync_FolderIsThere_FindsNothing()
    {
        await RecordAsync("FlightTools");
        WriteMod("FlightTools");

        Assert.Null(await _detector.FindLeftoverFolderAsync(_instanceId, "FlightTools"));
    }

    private string ModsFolder => _paths.GetInstanceModsFolder(_instanceId);

    private void WriteMod(string folderName)
    {
        var folder = Path.Combine(ModsFolder, folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "mod.toml"), $"name = \"{folderName}\"");
    }

    private Task RecordAsync(string modId)
        => _instances.UpdateAsync(
            _instanceId,
            instance =>
            {
                instance.AddMod(new InstalledMod(modId, ModVersion.Parse("1.0.0"), InstallReason.Manual, DateTimeOffset.UnixEpoch, Release(modId)));
                return true;
            });

    private static ModVersionMetadata Release(string modId) => new(
        specVersion: 1,
        modId: modId,
        version: ModVersion.Parse("1.0.0"),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: DateTimeOffset.UnixEpoch,
        gameMin: "2026.9.7.5402",
        gameMinRevision: 5402,
        download: new DownloadInfo("https://example.invalid/mod.zip", new string('A', 64), null, "application/zip"),
        installSizeBytes: null,
        dependencies: Array.Empty<Borea.Core.Dependencies.ModDependency>());
}
