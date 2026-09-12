using System.Security.Cryptography;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Storage.Instances;
using Borea.Storage.Mods;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Mods;

public sealed class FileForeignModAdopterTests : IAsyncLifetime
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly FakeArchiveReleaseLookup _lookup = new();
    private TestGamePathProvider _paths = null!;
    private FileInstanceRepository _instances = null!;
    private FileForeignModAdopter _adopter = null!;
    private Guid _instanceId;

    public async Task InitializeAsync()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _instances = new FileInstanceRepository(_paths);
        _instanceId = (await _instances.CreateAsync("Test", InstanceSource.Custom.Value)).InstanceId;
        _adopter = new FileForeignModAdopter(_paths, _instances, _lookup);
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task ScanAsync_ListsOnlyUntrackedFoldersWithModToml()
    {
        WriteMod("UnknownMod", "name = \"Unknown\"");
        Directory.CreateDirectory(Path.Combine(ModsFolder, "NotAMod"));

        var foreign = Assert.Single(await _adopter.ScanAsync(_instanceId));

        Assert.Equal("UnknownMod", foreign.FolderName);
        Assert.Null(foreign.Version);
        Assert.Equal(GameCompatibility.Unknown, foreign.Compatibility);
        var saved = await _instances.GetByIdAsync(_instanceId);
        Assert.Equal("UnknownMod", Assert.Single(saved!.ForeignMods).FolderName);
    }

    [Fact]
    public async Task ScanAsync_ReadsStarMapDependenciesWithoutVersionBounds()
    {
        WriteMod("LocalMod", """
            [StarMap]
            EntryAssembly = "LocalMod"

            [[StarMap.ModDependencies]]
            ModId = "RequiredMod"

            [[StarMap.ModDependencies]]
            ModId = "OptionalMod"
            Optional = true
            """);

        var foreign = Assert.Single(await _adopter.ScanAsync(_instanceId));

        Assert.Collection(
            foreign.Dependencies,
            dependency =>
            {
                Assert.Equal("RequiredMod", dependency.ModId);
                Assert.False(dependency.Optional);
            },
            dependency =>
            {
                Assert.Equal("OptionalMod", dependency.ModId);
                Assert.True(dependency.Optional);
            });
    }

    [Fact]
    public async Task AdoptArchiveAsync_MatchingDigest_RecordsExactReleaseAsForeignOwned()
    {
        WriteMod("LocalMod", "name = \"Local\"");
        await _adopter.ScanAsync(_instanceId);
        var archive = WriteArchive(new byte[] { 1, 2, 3, 4 });
        var digest = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(archive)));
        _lookup.Release = Release("LocalMod", digest);

        var result = await _adopter.AdoptArchiveAsync(_instanceId, "localmod", archive);

        Assert.True(result.Matched);
        Assert.Equal(digest, result.Sha256);
        Assert.Equal(ModVersion.Parse("1.2.0"), result.InstalledMod!.Version);
        Assert.Equal(ModInstallOwnership.Foreign, result.InstalledMod.Ownership);
        Assert.False(result.InstalledMod.CanDeleteFiles);
        Assert.Null(result.ForeignMod);
        Assert.Equal("LocalMod", _lookup.LastModId);
        Assert.Equal(digest, _lookup.LastSha256);

        var saved = await _instances.GetByIdAsync(_instanceId);
        Assert.Empty(saved!.ForeignMods);
        var savedMod = Assert.Single(saved.Mods);
        Assert.Equal("LocalMod", savedMod.ModId);
        Assert.Equal(ModInstallOwnership.Foreign, savedMod.Ownership);
    }

    [Fact]
    public async Task AdoptArchiveAsync_UnmatchedDigest_KeepsUnknownForeignMod()
    {
        WriteMod("LocalMod", "name = \"Local\"");
        var archive = WriteArchive(new byte[] { 5, 6, 7 });

        var result = await _adopter.AdoptArchiveAsync(_instanceId, "LocalMod", archive);

        Assert.False(result.Matched);
        Assert.NotNull(result.ForeignMod);
        Assert.Null(result.ForeignMod.Version);
        Assert.Null(result.InstalledMod);
        var saved = await _instances.GetByIdAsync(_instanceId);
        Assert.Empty(saved!.Mods);
        Assert.Equal("LocalMod", Assert.Single(saved.ForeignMods).FolderName);
    }

    [Fact]
    public async Task AdoptArchiveAsync_ConcurrentInstanceUpdate_PreservesBothMods()
    {
        WriteMod("LocalMod", "name = \"Local\"");
        var archive = WriteArchive(new byte[] { 8, 9, 10 });
        var digest = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(archive)));
        _lookup.Release = Release("LocalMod", digest);
        _lookup.BlockNextLookup();

        var adoption = _adopter.AdoptArchiveAsync(_instanceId, "LocalMod", archive);
        await _lookup.LookupStarted.WaitAsync(TimeSpan.FromSeconds(5));

        var otherRelease = Release("OtherMod", new string('A', 64));
        await _instances.UpdateAsync(
            _instanceId,
            instance =>
            {
                instance.AddMod(new InstalledMod(
                    "OtherMod",
                    otherRelease.Version,
                    InstallReason.Manual,
                    DateTimeOffset.UtcNow,
                    otherRelease,
                    ownership: ModInstallOwnership.Foreign));
                return true;
            });
        _lookup.ContinueLookup();

        await adoption;

        var saved = await _instances.GetByIdAsync(_instanceId);
        Assert.Collection(
            saved!.Mods.OrderBy(mod => mod.ModId, ModIds.Comparer),
            mod => Assert.Equal("LocalMod", mod.ModId),
            mod => Assert.Equal("OtherMod", mod.ModId));
    }

    [Fact]
    public async Task AdoptArchiveAsync_FolderRemovedDuringLookup_DoesNotRecordMod()
    {
        WriteMod("LocalMod", "name = \"Local\"");
        var archive = WriteArchive(new byte[] { 11, 12, 13 });
        var digest = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(archive)));
        _lookup.Release = Release("LocalMod", digest);
        _lookup.BlockNextLookup();

        var adoption = _adopter.AdoptArchiveAsync(_instanceId, "LocalMod", archive);
        await _lookup.LookupStarted.WaitAsync(TimeSpan.FromSeconds(5));
        Directory.Delete(Path.Combine(ModsFolder, "LocalMod"), recursive: true);
        _lookup.ContinueLookup();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => adoption);

        Assert.Contains("no longer has", exception.Message);
        var saved = await _instances.GetByIdAsync(_instanceId);
        Assert.Empty(saved!.Mods);
        Assert.Empty(saved.ForeignMods);
    }

    private string ModsFolder => _paths.GetInstanceModsFolder(_instanceId);

    private void WriteMod(string folderName, string manifest)
    {
        var folder = Path.Combine(ModsFolder, folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "mod.toml"), manifest);
    }

    private string WriteArchive(byte[] bytes)
    {
        var path = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N") + ".zip");
        Directory.CreateDirectory(_tempRoot);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static ModVersionMetadata Release(string modId, string sha256) => new(
        specVersion: 1,
        modId: modId,
        version: ModVersion.Parse("1.2.0"),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: DateTimeOffset.Parse("2026-09-12T00:00:00Z"),
        gameMin: "2026.9.7.5402",
        gameMinRevision: 5402,
        download: new DownloadInfo("https://example.invalid/mod.zip", sha256, null, "application/zip"),
        installSizeBytes: null,
        dependencies: Array.Empty<Borea.Core.Dependencies.ModDependency>());

    private sealed class FakeArchiveReleaseLookup : IModArchiveReleaseLookup
    {
        private TaskCompletionSource? _continueLookup;

        public ModVersionMetadata? Release { get; set; }
        public string? LastModId { get; private set; }
        public string? LastSha256 { get; private set; }
        public Task LookupStarted { get; private set; } = Task.CompletedTask;

        public void BlockNextLookup()
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            LookupStarted = started.Task;
            _continueLookup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _lookupStarted = started;
        }

        public void ContinueLookup() => _continueLookup?.SetResult();

        private TaskCompletionSource? _lookupStarted;

        public async Task<ModVersionMetadata?> FindBySha256Async(
            string modId,
            string sha256,
            CancellationToken cancellationToken = default)
        {
            LastModId = modId;
            LastSha256 = sha256;
            _lookupStarted?.SetResult();
            if (_continueLookup is not null)
                await _continueLookup.Task.WaitAsync(cancellationToken);

            return Release;
        }
    }
}
