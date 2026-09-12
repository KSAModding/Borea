using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Storage.Instances;
using Borea.Storage.Mods;
using Borea.Storage.State;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Mods;

public sealed class FileModReplacerTests : IAsyncLifetime
{
    private const string ModId = "test-mod";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly FakeModDownloader _downloader = new();
    private TestGamePathProvider _paths = null!;
    private FileInstanceRepository _instances = null!;
    private FileModStateRepository _state = null!;
    private Guid _instanceId;

    public async Task InitializeAsync()
    {
        _paths = new TestGamePathProvider(_root);
        _instances = new FileInstanceRepository(_paths);
        _state = new FileModStateRepository(_paths);
        _instanceId = (await _instances.CreateAsync("Test", InstanceSource.Custom.Value)).InstanceId;
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
    public async Task ReplaceAsync_ReplacesOwnedFilesAndPreservesState(bool enabled)
    {
        var current = await InstallAsync(Release("1.0.0"), "old", enabled);
        _downloader.Bytes = Archive("new");
        var replacer = new FileModReplacer(_paths, _downloader, _instances, _state);

        var result = await replacer.ReplaceAsync(_instanceId, current, Release("1.1.0"));

        Assert.Equal(ModVersion.Parse("1.1.0"), result.Replacement.Version);
        Assert.Equal("new", File.ReadAllText(Path.Combine(ModFolder, "value.txt")));
        Assert.Equal(enabled, await _state.IsActiveAsync(_instanceId, ModId));
        var saved = Assert.Single((await _instances.GetByIdAsync(_instanceId))!.Mods);
        Assert.Equal(current.Reason, saved.Reason);
        Assert.Equal(ModVersion.Parse("1.1.0"), saved.Version);
    }

    [Fact]
    public async Task ReplaceAsync_InvalidArchiveLeavesTheCurrentInstallUntouched()
    {
        var current = await InstallAsync(Release("1.0.0"), "old", enabled: true);
        _downloader.Bytes = TestArchives.Build((ModId + "/value.txt", "new"));
        var replacer = new FileModReplacer(_paths, _downloader, _instances, _state);

        await Assert.ThrowsAsync<InvalidOperationException>(() => replacer.ReplaceAsync(_instanceId, current, Release("1.1.0")));

        Assert.Equal("old", File.ReadAllText(Path.Combine(ModFolder, "value.txt")));
        Assert.Equal(ModVersion.Parse("1.0.0"), Assert.Single((await _instances.GetByIdAsync(_instanceId))!.Mods).Version);
    }

    [Fact]
    public async Task ReplaceAsync_RejectsAStaleInstalledRecord()
    {
        var current = await InstallAsync(Release("1.0.0"), "old", enabled: true);
        var stale = new InstalledMod(ModId, current.Version, current.Reason, current.InstalledAt, current.Metadata, current.Checksum, ModInstallOwnership.Borea, "different-token");
        var replacer = new FileModReplacer(_paths, _downloader, _instances, _state);

        await Assert.ThrowsAsync<InvalidOperationException>(() => replacer.ReplaceAsync(_instanceId, stale, Release("1.1.0")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(ModFolder, "value.txt")));
    }

    [Fact]
    public async Task ReplaceAsync_RejectsForeignOwnership()
    {
        var metadata = Release("1.0.0");
        var foreign = new InstalledMod(ModId, metadata.Version, InstallReason.Manual, DateTimeOffset.UtcNow, metadata, ownership: ModInstallOwnership.Foreign);
        var replacer = new FileModReplacer(_paths, _downloader, _instances, _state);

        await Assert.ThrowsAsync<InvalidOperationException>(() => replacer.ReplaceAsync(_instanceId, foreign, Release("1.1.0")));
    }

    [Fact]
    public async Task ReplaceAsync_CancellationDuringPreparationLeavesTheCurrentInstallUntouched()
    {
        var current = await InstallAsync(Release("1.0.0"), "old", enabled: true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var replacer = new FileModReplacer(_paths, _downloader, _instances, _state);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => replacer.ReplaceAsync(_instanceId, current, Release("1.1.0"), cancellationToken: cancellation.Token));
        Assert.Equal("old", File.ReadAllText(Path.Combine(ModFolder, "value.txt")));
    }

    [Fact]
    public async Task ReplaceAsync_MetadataSaveFailureAfterSwapRestoresTheCurrentInstall()
    {
        var current = await InstallAsync(Release("1.0.0"), "old", enabled: true);
        _downloader.Bytes = Archive("new");
        var failingInstances = new FailBeforeSaveInstanceRepository(_instances);
        var replacer = new FileModReplacer(_paths, _downloader, failingInstances, _state);

        await Assert.ThrowsAsync<IOException>(() => replacer.ReplaceAsync(_instanceId, current, Release("1.1.0")));

        Assert.Equal("old", File.ReadAllText(Path.Combine(ModFolder, "value.txt")));
        Assert.Equal(ModVersion.Parse("1.0.0"), Assert.Single((await _instances.GetByIdAsync(_instanceId))!.Mods).Version);
    }

    [Fact]
    public async Task ReplaceAsync_BackupCleanupFailureKeepsTheReplacementAndReportsRecoveryData()
    {
        var current = await InstallAsync(Release("1.0.0"), "old", enabled: true);
        _downloader.Bytes = Archive("new");
        var replacer = new FileModReplacer(_paths, _downloader, _instances, _state, timeProvider: null, deleteRecoveryDirectory: _ => false);

        var result = await replacer.ReplaceAsync(_instanceId, current, Release("1.1.0"));

        Assert.NotNull(result.RetainedRecoveryDirectory);
        Assert.True(Directory.Exists(result.RetainedRecoveryDirectory));
        Assert.Equal("new", File.ReadAllText(Path.Combine(ModFolder, "value.txt")));
        Assert.Equal(ModVersion.Parse("1.1.0"), Assert.Single((await _instances.GetByIdAsync(_instanceId))!.Mods).Version);
    }

    [Fact]
    public async Task ReplaceAsync_CompetingUpdateWaitsForRecoveryFileAndRecordRestore()
    {
        var current = await InstallAsync(Release("1.0.0"), "old", enabled: true);
        _downloader.Bytes = Archive("new");
        var coordinatedInstances = new CompetingRecoveryInstanceRepository(_instances);
        var replacer = new FileModReplacer(_paths, _downloader, coordinatedInstances, _state);

        await Assert.ThrowsAsync<IOException>(() => replacer.ReplaceAsync(_instanceId, current, Release("1.1.0")));

        Assert.Equal("old", File.ReadAllText(Path.Combine(ModFolder, "value.txt")));
        var saved = Assert.Single((await _instances.GetByIdAsync(_instanceId))!.Mods);
        Assert.Equal(ModVersion.Parse("1.0.0"), saved.Version);
        Assert.Equal(InstallReason.Manual, saved.Reason);
    }

    private string ModFolder => Path.Combine(_paths.GetInstanceModsFolder(_instanceId), ModId);

    private async Task<InstalledMod> InstallAsync(ModVersionMetadata release, string value, bool enabled)
    {
        _downloader.Bytes = Archive(value);
        var installer = new FileModInstaller(_paths, _downloader, _instances, _state);
        return (await installer.InstallAsync(_instanceId, release, InstallReason.Dependency, enabled)).Mod;
    }

    private static byte[] Archive(string value) => TestArchives.Build((ModId + "/mod.toml", "name = \"test\""), (ModId + "/value.txt", value));

    private static ModVersionMetadata Release(string version) => new(
        1,
        ModId,
        ModVersion.Parse(version),
        ReleaseStatus.Stable,
        DateTimeOffset.UtcNow,
        "2026.9.7.5402",
        5402,
        new DownloadInfo("https://example.com/mod.zip", null, null, "application/zip"),
        null,
        Array.Empty<ModDependency>(),
        type: ContentType.Mod,
        install: new InstallInfo(ModId, derived: true));

    private sealed class FailBeforeSaveInstanceRepository(IInstanceRepository inner) : IInstanceRepository
    {
        private bool _fail = true;

        public Task<IReadOnlyList<Instance>> GetAllAsync() => inner.GetAllAsync();
        public Task<Instance?> GetByIdAsync(Guid instanceId) => inner.GetByIdAsync(instanceId);
        public Task<Guid?> GetActiveInstanceIdAsync() => inner.GetActiveInstanceIdAsync();
        public Task SetActiveInstanceAsync(Guid instanceId) => inner.SetActiveInstanceAsync(instanceId);
        public Task<bool> IsNameAvailableAsync(string name, Guid? excludingInstanceId = null) => inner.IsNameAvailableAsync(name, excludingInstanceId);
        public Task<Instance> CreateAsync(string name, InstanceSource source) => inner.CreateAsync(name, source);
        public Task RenameAsync(Guid instanceId, string newName) => inner.RenameAsync(instanceId, newName);
        public Task DeleteAsync(Guid instanceId) => inner.DeleteAsync(instanceId);
        public Task SaveAsync(Instance instance) => inner.SaveAsync(instance);

        public async Task<TResult> UpdateAsync<TResult>(Guid instanceId, Func<Instance, TResult> update, CancellationToken cancellationToken = default)
        {
            if (!_fail)
                return await inner.UpdateAsync(instanceId, update, cancellationToken);

            _fail = false;
            var saved = await inner.GetByIdAsync(instanceId) ?? throw new InvalidOperationException();
            var copy = Instance.FromExisting(saved.InstanceId, saved.Name, saved.Source, saved.CreatedAt, saved.Mods, saved.ForeignMods, saved.IsFavorite);
            update(copy);
            throw new IOException("Injected metadata save failure.");
        }
    }

    private sealed class CompetingRecoveryInstanceRepository(IInstanceRepository inner) : IInstanceRepository
    {
        private bool _fail = true;

        public Task<IReadOnlyList<Instance>> GetAllAsync() => inner.GetAllAsync();
        public Task<Instance?> GetByIdAsync(Guid instanceId) => inner.GetByIdAsync(instanceId);
        public Task<Guid?> GetActiveInstanceIdAsync() => inner.GetActiveInstanceIdAsync();
        public Task SetActiveInstanceAsync(Guid instanceId) => inner.SetActiveInstanceAsync(instanceId);
        public Task<bool> IsNameAvailableAsync(string name, Guid? excludingInstanceId = null) => inner.IsNameAvailableAsync(name, excludingInstanceId);
        public Task<Instance> CreateAsync(string name, InstanceSource source) => inner.CreateAsync(name, source);
        public Task RenameAsync(Guid instanceId, string newName) => inner.RenameAsync(instanceId, newName);
        public Task DeleteAsync(Guid instanceId) => inner.DeleteAsync(instanceId);
        public Task SaveAsync(Instance instance) => inner.SaveAsync(instance);

        public async Task<TResult> UpdateAsync<TResult>(Guid instanceId, Func<Instance, TResult> update, CancellationToken cancellationToken = default)
        {
            if (_fail)
            {
                _fail = false;
                var saved = await inner.GetByIdAsync(instanceId) ?? throw new InvalidOperationException();
                var copy = Instance.FromExisting(saved.InstanceId, saved.Name, saved.Source, saved.CreatedAt, saved.Mods, saved.ForeignMods, saved.IsFavorite);
                update(copy);
                throw new IOException("Injected metadata save failure.");
            }

            Task? competitor = null;
            var result = await inner.UpdateAsync(
                instanceId,
                current =>
                {
                    var recoveryResult = update(current);
                    using var started = new ManualResetEventSlim();
                    competitor = Task.Run(async () =>
                    {
                        started.Set();
                        await inner.UpdateAsync(
                            instanceId,
                            competing =>
                            {
                                Assert.Single(competing.Mods).MarkAsManuallyInstalled();
                                return true;
                            });
                    });
                    started.Wait();
                    return recoveryResult;
                },
                cancellationToken);
            await competitor!;
            return result;
        }
    }
}
