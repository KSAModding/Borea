using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Storage.Instances;
using Borea.Storage.Mods;
using Borea.Storage.State;
using Borea.Core.State;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Mods;

public sealed class FileForeignModHandoverTests : IAsyncLifetime
{
    private const string ModId = "test-mod";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly FakeModDownloader _downloader = new();
    private TestGamePathProvider _paths = null!;
    private FileInstanceRepository _instances = null!;
    private FileModStateRepository _state = null!;
    private FileForeignModHandover _handover = null!;
    private Guid _instanceId;

    public async Task InitializeAsync()
    {
        _paths = new TestGamePathProvider(_root);
        _instances = new FileInstanceRepository(_paths);
        _state = new FileModStateRepository(_paths);
        _instanceId = (await _instances.CreateAsync("Test", InstanceSource.Custom.Value)).Instance.InstanceId;
        _handover = new FileForeignModHandover(_paths, _downloader, _instances, _state);
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TakeOwnershipAsync_ForeignMod_InstallsTheRecordedReleaseAndKeepsItsState(bool enabled)
    {
        var foreign = await RecordForeignAsync(enabled);
        _downloader.Bytes = Archive("release");

        var owned = (await _handover.TakeOwnershipAsync(_instanceId, ModId)).Installed;

        Assert.Equal(ModInstallOwnership.Borea, owned.Ownership);
        Assert.True(owned.CanDeleteFiles);
        Assert.Equal(foreign.Reason, owned.Reason);
        Assert.Equal(foreign.Version, owned.Version);
        Assert.Equal("release", File.ReadAllText(Path.Combine(ModFolder, "value.txt")));
        Assert.Equal(owned.OwnershipToken, File.ReadAllText(Path.Combine(ModFolder, ".borea-owner")));
        Assert.False(File.Exists(Path.Combine(ModFolder, "settings.json")));
        Assert.Equal(enabled, await _state.IsActiveAsync(_instanceId, ModId));
        var saved = Assert.Single((await _instances.GetByIdAsync(_instanceId))!.Mods);
        Assert.Equal(ModInstallOwnership.Borea, saved.Ownership);
        Assert.Empty(Directory.EnumerateDirectories(_paths.GetInstanceRoot(_instanceId), ".borea-*"));
    }

    [Fact]
    public async Task TakeOwnershipAsync_DownloadFails_LeavesTheModAsItWas()
    {
        await RecordForeignAsync(enabled: true);
        _downloader.Failure = new IOException("no answer");

        await Assert.ThrowsAsync<IOException>(() => _handover.TakeOwnershipAsync(_instanceId, ModId));

        Assert.Equal("local", File.ReadAllText(Path.Combine(ModFolder, "value.txt")));
        Assert.True(File.Exists(Path.Combine(ModFolder, "settings.json")));
        Assert.False(File.Exists(Path.Combine(ModFolder, ".borea-owner")));
        var saved = Assert.Single((await _instances.GetByIdAsync(_instanceId))!.Mods);
        Assert.Equal(ModInstallOwnership.Foreign, saved.Ownership);
        Assert.True(await _state.IsActiveAsync(_instanceId, ModId));
    }

    [Fact]
    public async Task TakeOwnershipAsync_ArchiveWithoutADefinition_LeavesTheModAsItWas()
    {
        await RecordForeignAsync(enabled: true);
        _downloader.Bytes = TestArchives.Build((ModId + "/value.txt", "release"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handover.TakeOwnershipAsync(_instanceId, ModId));

        Assert.Equal("local", File.ReadAllText(Path.Combine(ModFolder, "value.txt")));
        Assert.Equal(ModInstallOwnership.Foreign, Assert.Single((await _instances.GetByIdAsync(_instanceId))!.Mods).Ownership);
    }

    [Fact]
    public async Task TakeOwnershipAsync_FolderIsGone_LeavesTheRecordAsItWas()
    {
        await RecordForeignAsync(enabled: true);
        Directory.Delete(ModFolder, recursive: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handover.TakeOwnershipAsync(_instanceId, ModId));

        Assert.Empty(_downloader.ArchivePaths);
        Assert.Equal(ModInstallOwnership.Foreign, Assert.Single((await _instances.GetByIdAsync(_instanceId))!.Mods).Ownership);
    }

    [Fact]
    public async Task TakeOwnershipAsync_ModBoreaOwns_Throws()
    {
        _downloader.Bytes = Archive("release");
        var installer = new FileModInstaller(_paths, _downloader, _instances, _state);
        await installer.InstallAsync(_instanceId, Release(), InstallReason.Manual, enable: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handover.TakeOwnershipAsync(_instanceId, ModId));
    }

    [Fact]
    public async Task TakeOwnershipAsync_ModThatIsNotInstalled_Throws()
        => await Assert.ThrowsAsync<InvalidOperationException>(() => _handover.TakeOwnershipAsync(_instanceId, ModId));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TakeOwnershipAsync_FailsAfterTheSwap_PutsTheFolderAndTheRecordBack(bool enabled)
    {
        await RecordForeignAsync(enabled);
        _downloader.Bytes = Archive("release");
        var handover = new FileForeignModHandover(_paths, _downloader, _instances, new FailingManifest(_state));

        await Assert.ThrowsAsync<IOException>(() => handover.TakeOwnershipAsync(_instanceId, ModId));

        Assert.Equal("local", File.ReadAllText(Path.Combine(ModFolder, "value.txt")));
        Assert.True(File.Exists(Path.Combine(ModFolder, "settings.json")));
        Assert.False(File.Exists(Path.Combine(ModFolder, ".borea-owner")));
        var saved = Assert.Single((await _instances.GetByIdAsync(_instanceId))!.Mods);
        Assert.Equal(ModInstallOwnership.Foreign, saved.Ownership);
        Assert.Equal(enabled, await _state.IsActiveAsync(_instanceId, ModId));
        Assert.Empty(Directory.EnumerateDirectories(_paths.GetInstanceRoot(_instanceId), ".borea-*"));
    }

    [Fact]
    public async Task TakeOwnershipAsync_RecoveryFolderStays_NamesItInTheResult()
    {
        await RecordForeignAsync(enabled: true);
        _downloader.Bytes = Archive("release");
        var handover = new FileForeignModHandover(_paths, _downloader, _instances, _state, timeProvider: null, deleteRecoveryDirectory: _ => false);

        var result = await handover.TakeOwnershipAsync(_instanceId, ModId);

        Assert.Equal(ModInstallOwnership.Borea, result.Installed.Ownership);
        Assert.Equal("release", File.ReadAllText(Path.Combine(ModFolder, "value.txt")));
        var kept = Assert.IsType<string>(result.RetainedRecoveryDirectory);
        Assert.True(Directory.Exists(kept));
        Assert.Equal("local", File.ReadAllText(Path.Combine(kept, "value.txt")));
    }

    private string ModFolder => Path.Combine(_paths.GetInstanceModsFolder(_instanceId), ModId);

    /// <summary>
    /// A mod the adopter matched against a release, with a file of its own that
    /// the release does not hold.
    /// </summary>
    private async Task<InstalledMod> RecordForeignAsync(bool enabled)
    {
        Directory.CreateDirectory(ModFolder);
        await File.WriteAllTextAsync(Path.Combine(ModFolder, "mod.toml"), "name = \"test\"");
        await File.WriteAllTextAsync(Path.Combine(ModFolder, "value.txt"), "local");
        await File.WriteAllTextAsync(Path.Combine(ModFolder, "settings.json"), "{}");

        var release = Release();
        var foreign = new InstalledMod(ModId, release.Version, InstallReason.Dependency, DateTimeOffset.UtcNow, release, "ABCD", ModInstallOwnership.Foreign);
        await _instances.UpdateAsync(
            _instanceId,
            instance =>
            {
                instance.AddMod(foreign);
                return true;
            });
        await _state.AddEntryAsync(_instanceId, ModId, enabled);
        return foreign;
    }

    private static byte[] Archive(string value) => TestArchives.Build((ModId + "/mod.toml", "name = \"test\""), (ModId + "/value.txt", value));

    /// <summary>
    /// Fails the first manifest write of the handover, which lands after the
    /// folder and the record were already swapped.
    /// </summary>
    private sealed class FailingManifest(IModStateRepository inner) : IModStateRepository
    {
        public Task<IReadOnlyList<ModManifestEntry>> GetEntriesAsync(Guid instanceId, CancellationToken cancellationToken = default)
            => inner.GetEntriesAsync(instanceId, cancellationToken);

        public Task<bool> IsActiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default)
            => inner.IsActiveAsync(instanceId, modId, cancellationToken);

        public Task<IReadOnlyList<string>> GetAllActiveModIdsAsync(Guid instanceId, CancellationToken cancellationToken = default)
            => inner.GetAllActiveModIdsAsync(instanceId, cancellationToken);

        public Task<ModEntryAddResult> AddEntryAsync(Guid instanceId, string modId, bool enabled, CancellationToken cancellationToken = default)
            => throw new IOException("the manifest is locked");

        public Task<bool> SetActiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default)
            => inner.SetActiveAsync(instanceId, modId, cancellationToken);

        public Task<bool> SetInactiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default)
            => inner.SetInactiveAsync(instanceId, modId, cancellationToken);

        public Task<bool> ReorderAsync(Guid instanceId, IReadOnlyList<string> modIds, CancellationToken cancellationToken = default)
            => inner.ReorderAsync(instanceId, modIds, cancellationToken);

        public Task<bool> PutGameContentFirstAsync(Guid instanceId, string gameDirectory, CancellationToken cancellationToken = default)
            => inner.PutGameContentFirstAsync(instanceId, gameDirectory, cancellationToken);
    }

    private static ModVersionMetadata Release() => new(
        1,
        ModId,
        ModVersion.Parse("1.0.0"),
        ReleaseStatus.Stable,
        DateTimeOffset.UtcNow,
        "2026.9.7.5402",
        5402,
        new DownloadInfo("https://example.com/mod.zip", null, null, "application/zip"),
        null,
        Array.Empty<ModDependency>(),
        type: ContentType.Mod,
        install: new InstallInfo(ModId, derived: true));
}
