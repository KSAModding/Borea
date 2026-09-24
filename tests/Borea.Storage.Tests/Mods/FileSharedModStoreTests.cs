using System.Security.Cryptography;
using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Mods;
using Borea.Core.Settings;
using Borea.Storage.Files;
using Borea.Storage.Instances;
using Borea.Storage.Mods;
using Borea.Storage.Settings;
using Borea.Storage.State;
using Borea.Storage.Tests.Launch;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Mods;

/// <summary>The break-out of a stored release that changed, and the setting that turns the store off.</summary>
public sealed class FileSharedModStoreTests : IAsyncLifetime
{
    private const string ModId = "test-mod";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly FakeModDownloader _downloader = new() { Bytes = Archive("one") };
    private readonly DirectoryLinker _linker = new();
    private readonly FakeLauncher _launcher = new();
    private readonly TestGamePathProvider _paths;
    private readonly ModStore _store;
    private readonly FileInstanceRepository _instances;
    private readonly FileModStateRepository _state;
    private readonly FileBoreaSettingsRepository _settings;
    private bool _gameRunning;
    private bool _otherBoreaRunning;
    private Guid _first;
    private Guid _second;

    public FileSharedModStoreTests()
    {
        _paths = new TestGamePathProvider(_root);
        _store = new ModStore(_paths, _linker, linksReleases: true);
        _instances = new FileInstanceRepository(_paths, _store);
        _state = new FileModStateRepository(_paths);
        _settings = new FileBoreaSettingsRepository(_paths);
    }

    public async Task InitializeAsync()
    {
        _first = (await _instances.CreateAsync("First", InstanceSource.Custom.Value)).Instance.InstanceId;
        _second = (await _instances.CreateAsync("Second", InstanceSource.Custom.Value)).Instance.InstanceId;
    }

    public Task DisposeAsync()
    {
        DirectoryLinks.DeleteTreeWithoutFollowingLinks(_root);
        return Task.CompletedTask;
    }

    private FileSharedModStore SharedStore(IInstanceRepository? instances = null)
        => new(_paths, _store, instances ?? _instances, _settings, _launcher, () => _gameRunning, () => _otherBoreaRunning, TimeSpan.FromMilliseconds(10));

    private string StoredMod => Path.Combine(_paths.GetStaticModFilesRoot(), ModId);

    [Fact]
    public async Task CheckAsync_UnchangedRelease_KeepsTheLinks()
    {
        await InstallBothAsync();

        var brokenOut = await SharedStore().CheckAsync(_first);

        Assert.Empty(brokenOut);
        Assert.True(_linker.IsLink(ModFolder(_first)));
        Assert.True(_linker.IsLink(ModFolder(_second)));
    }

    [Fact]
    public async Task CheckAsync_ChangedRelease_GivesEachInstanceItsOwnCopyAndEmptiesTheStore()
    {
        await InstallBothAsync();
        File.WriteAllText(Path.Combine(ModFolder(_first), "settings.cfg"), "written by the mod");

        var brokenOut = await SharedStore().CheckAsync(_first);

        Assert.Equal(ModId, Assert.Single(brokenOut).ModId);
        foreach (var instanceId in new[] { _first, _second })
        {
            Assert.False(_linker.IsLink(ModFolder(instanceId)));
            Assert.Equal("written by the mod", File.ReadAllText(Path.Combine(ModFolder(instanceId), "settings.cfg")));
            var recorded = Assert.Single((await _instances.GetByIdAsync(instanceId))!.Mods);
            Assert.Equal(ModStorage.Private, recorded.Storage);
            Assert.Equal(recorded.OwnershipToken, File.ReadAllText(Path.Combine(ModFolder(instanceId), ModFolders.OwnershipFileName)));
            Assert.Empty(Directory.EnumerateDirectories(_paths.GetInstanceRoot(instanceId), ".borea-*"));
        }

        Assert.False(Directory.Exists(StoredMod));

        File.WriteAllText(Path.Combine(ModFolder(_first), "value.txt"), "first only");
        Assert.Equal("one", File.ReadAllText(Path.Combine(ModFolder(_second), "value.txt")));
    }

    [Fact]
    public async Task CheckAsync_WhileTheGameRuns_ChangesNothing()
    {
        await InstallBothAsync();
        File.WriteAllText(Path.Combine(ModFolder(_first), "settings.cfg"), "written by the mod");
        _gameRunning = true;

        var brokenOut = await SharedStore().CheckAsync(_first);

        Assert.Empty(brokenOut);
        Assert.True(_linker.IsLink(ModFolder(_first)));
        Assert.True(_linker.IsLink(ModFolder(_second)));
    }

    [Fact]
    public async Task CheckAsync_ReleaseWithoutSnapshot_CountsAsChanged()
    {
        await InstallAsync(_first, Release());
        File.Delete(Assert.Single(Directory.GetFiles(StoredMod, "*.snapshot.toml")));

        var brokenOut = await SharedStore().CheckAsync(_first);

        Assert.Single(brokenOut);
        Assert.False(_linker.IsLink(ModFolder(_first)));
    }

    [Fact]
    public async Task CheckAsync_ModBrokenOutBefore_IsNotReportedAgain()
    {
        await InstallAsync(_first, Release());
        _downloader.Bytes = Archive("two");
        await InstallAsync(_second, Release(version: "1.3.0"));
        foreach (var instanceId in new[] { _first, _second })
            File.WriteAllText(Path.Combine(ModFolder(instanceId), "settings.cfg"), "written by the mod");

        Assert.Single(await SharedStore().CheckAsync(_first));
        var second = await SharedStore().CheckAsync(_second);

        Assert.Empty(second);
        Assert.False(_linker.IsLink(ModFolder(_second)));
        Assert.Equal("two", File.ReadAllText(Path.Combine(ModFolder(_second), "value.txt")));
    }

    [Fact]
    public async Task ReplaceAsync_ModBrokenOutBefore_StaysPrivate()
    {
        await InstallBothAsync();
        File.WriteAllText(Path.Combine(ModFolder(_first), "settings.cfg"), "written by the mod");
        await SharedStore().CheckAsync(_first);
        var current = Assert.Single((await _instances.GetByIdAsync(_first))!.Mods);
        _downloader.Bytes = Archive("two");
        var replacer = new FileModReplacer(_paths, _downloader, _instances, _state, store: _store);

        var result = await replacer.ReplaceAsync(_first, current, Release(version: "1.3.0"));

        Assert.Equal(ModStorage.Private, result.Replacement.Storage);
        Assert.False(_linker.IsLink(ModFolder(_first)));
        Assert.Equal("two", File.ReadAllText(Path.Combine(ModFolder(_first), "value.txt")));
        Assert.False(Directory.Exists(StoredMod));
    }

    [Fact]
    public async Task CheckAsync_RecordCannotBeSaved_PutsTheLinkBack()
    {
        await InstallAsync(_first, Release());
        File.WriteAllText(Path.Combine(ModFolder(_first), "settings.cfg"), "written by the mod");

        await Assert.ThrowsAsync<IOException>(() => SharedStore(new FailingUpdateRepository(_instances)).CheckAsync(_first));

        Assert.True(_linker.IsLink(ModFolder(_first)));
        Assert.Equal(ModStorage.Linked, Assert.Single((await _instances.GetByIdAsync(_first))!.Mods).Storage);
        Assert.Empty(Directory.EnumerateDirectories(_paths.GetInstanceRoot(_first), ".borea-*"));
    }

    [WindowsFact("Only Windows refuses to delete a file that is open.")]
    public async Task CheckAsync_LinkCannotBePutBack_KeepsItAsideAndNamesIt()
    {
        await InstallAsync(_first, Release());
        File.WriteAllText(Path.Combine(ModFolder(_first), "settings.cfg"), "written by the mod");
        var instances = new CopyHeldOpenRepository(_instances, Path.Combine(ModFolder(_first), "value.txt"));

        try
        {
            var error = await Assert.ThrowsAsync<IOException>(() => SharedStore(instances).CheckAsync(_first));

            var recoveryLink = Assert.Single(Directory.EnumerateDirectories(_paths.GetInstanceRoot(_first), ".borea-recovery-*"));
            Assert.Contains(recoveryLink, error.Message);
            Assert.True(_linker.IsLink(recoveryLink));
        }
        finally
        {
            instances.Held?.Dispose();
        }
    }

    [Fact]
    public async Task CheckAsync_TwoInstancesAtOnce_BreaksOutTheReleaseOnce()
    {
        await InstallBothAsync();
        File.WriteAllText(Path.Combine(ModFolder(_first), "settings.cfg"), "written by the mod");
        var instances = new GatedUpdateRepository(_instances);
        var store = SharedStore(instances);

        var first = store.CheckAsync(_first);
        await instances.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = store.CheckAsync(_second);
        await Task.WhenAny(second, Task.Delay(200));
        instances.Gate.SetResult();

        Assert.Single(await first);
        Assert.Empty(await second);
        Assert.False(_linker.IsLink(ModFolder(_first)));
        Assert.False(_linker.IsLink(ModFolder(_second)));
    }

    [Fact]
    public async Task SetEnabledAsync_Off_LeavesEveryInstanceWithItsOwnFilesAndAnEmptyStore()
    {
        await InstallBothAsync();

        Assert.Equal(SharedModStoreChange.Saved, await SharedStore().SetEnabledAsync(enabled: false));

        Assert.False((await _settings.GetAsync())!.SharedModStore);
        foreach (var instanceId in new[] { _first, _second })
        {
            Assert.False(_linker.IsLink(ModFolder(instanceId)));
            Assert.Equal("one", File.ReadAllText(Path.Combine(ModFolder(instanceId), "value.txt")));
            Assert.Equal(ModStorage.Private, Assert.Single((await _instances.GetByIdAsync(instanceId))!.Mods).Storage);
        }

        Assert.Empty(Directory.EnumerateDirectories(_paths.GetStaticModFilesRoot()));
    }

    [Fact]
    public async Task SetEnabledAsync_OffWhileTheGameRuns_ChangesNothing()
    {
        await InstallAsync(_first, Release());
        _launcher.Running.Add(_second);

        Assert.Equal(SharedModStoreChange.GameRunning, await SharedStore().SetEnabledAsync(enabled: false));

        Assert.Null(await _settings.GetAsync());
        Assert.True(_linker.IsLink(ModFolder(_first)));
    }

    [Fact]
    public async Task SetEnabledAsync_OffWhileAnotherBoreaRuns_ChangesNothing()
    {
        await InstallAsync(_first, Release());
        _otherBoreaRunning = true;

        Assert.Equal(SharedModStoreChange.BoreaRunning, await SharedStore().SetEnabledAsync(enabled: false));

        Assert.Null(await _settings.GetAsync());
        Assert.True(_linker.IsLink(ModFolder(_first)));
    }

    [Fact]
    public async Task WaitForGameExitAsync_ReturnsOnceTheGameIsClosed()
    {
        _gameRunning = true;
        var wait = SharedStore().WaitForGameExitAsync(_first);
        await Task.Delay(50);
        Assert.False(wait.IsCompleted);

        _gameRunning = false;

        await wait.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private async Task InstallBothAsync()
    {
        await InstallAsync(_first, Release());
        await InstallAsync(_second, Release());
    }

    private Task InstallAsync(Guid instanceId, ModVersionMetadata release)
        => new FileModInstaller(_paths, _downloader, _instances, _state, store: _store).InstallAsync(instanceId, release, InstallReason.Manual, enable: true);

    private string ModFolder(Guid instanceId) => Path.Combine(_paths.GetInstanceModsFolder(instanceId), ModId);

    private static byte[] Archive(string value) => TestArchives.Build((ModId + "/mod.toml", "name = \"test\""), (ModId + "/value.txt", value));

    private ModVersionMetadata Release(string version = "1.2.0") => new(
        specVersion: 1,
        modId: ModId,
        version: ModVersion.Parse(version),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        gameMin: "2026.7.4.2131",
        gameMinRevision: 2131,
        download: new DownloadInfo("https://example.com/mod.zip", Convert.ToHexString(SHA256.HashData(_downloader.Bytes)), null, "application/zip"),
        installSizeBytes: null,
        dependencies: Array.Empty<ModDependency>());

    private sealed class FakeLauncher : ILauncher
    {
        public HashSet<Guid> Running { get; } = [];

        public LaunchResult Launch(Instance instance, ModMetadata? loader, IReadOnlyList<string>? arguments = null) => throw new NotSupportedException();

        public Task<LaunchResult> WatchStartAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public bool IsRunning(Guid instanceId) => Running.Contains(instanceId);
    }

    /// <summary>Runs the change on the real record, then fails before it is saved.</summary>
    private sealed class FailingUpdateRepository(IInstanceRepository inner) : WrappingRepository(inner)
    {
        public override Task<TResult> UpdateAsync<TResult>(Guid instanceId, Func<Instance, TResult> update, CancellationToken cancellationToken = default)
            => Inner.UpdateAsync<TResult>(instanceId, current =>
            {
                update(current);
                throw new IOException("Injected metadata save failure.");
            }, cancellationToken);
    }

    /// <summary>Like <see cref="FailingUpdateRepository"/>, and holds a file of the new copy open so it cannot be removed.</summary>
    private sealed class CopyHeldOpenRepository(IInstanceRepository inner, string heldFile) : WrappingRepository(inner)
    {
        public FileStream? Held { get; private set; }

        public override Task<TResult> UpdateAsync<TResult>(Guid instanceId, Func<Instance, TResult> update, CancellationToken cancellationToken = default)
            => Inner.UpdateAsync<TResult>(instanceId, current =>
            {
                update(current);
                Held = new FileStream(heldFile, FileMode.Open, FileAccess.Read, FileShare.None);
                throw new IOException("Injected metadata save failure.");
            }, cancellationToken);
    }

    /// <summary>Holds the first change until <see cref="Gate"/> opens.</summary>
    private sealed class GatedUpdateRepository(IInstanceRepository inner) : WrappingRepository(inner)
    {
        private int _updates;

        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<TResult> UpdateAsync<TResult>(Guid instanceId, Func<Instance, TResult> update, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _updates) == 1)
            {
                Reached.SetResult();
                await Gate.Task;
            }

            return await Inner.UpdateAsync(instanceId, update, cancellationToken);
        }
    }

    private abstract class WrappingRepository(IInstanceRepository inner) : IInstanceRepository
    {
        protected IInstanceRepository Inner => inner;

        public Task<IReadOnlyList<Instance>> GetAllAsync() => inner.GetAllAsync();
        public Task<Instance?> GetByIdAsync(Guid instanceId) => inner.GetByIdAsync(instanceId);
        public Task<Guid?> GetActiveInstanceIdAsync() => inner.GetActiveInstanceIdAsync();
        public Task SetActiveInstanceAsync(Guid instanceId) => inner.SetActiveInstanceAsync(instanceId);
        public Task ClearActiveInstanceAsync() => inner.ClearActiveInstanceAsync();
        public Task<bool> IsNameAvailableAsync(string name, Guid? excludingInstanceId = null) => inner.IsNameAvailableAsync(name, excludingInstanceId);
        public Task<InstanceCreateResult> CreateAsync(string name, InstanceSource source) => inner.CreateAsync(name, source);
        public Task<InstanceCreateResult> CreateAsync(Instance instance) => inner.CreateAsync(instance);
        public Task<InstanceCreateResult> CreateAsync(Instance instance, InstanceOrigin origin) => inner.CreateAsync(instance, origin);
        public Task RenameAsync(Guid instanceId, string newName) => inner.RenameAsync(instanceId, newName);
        public Task DeleteAsync(Guid instanceId) => inner.DeleteAsync(instanceId);
        public Task SaveAsync(Instance instance) => inner.SaveAsync(instance);

        public virtual Task<TResult> UpdateAsync<TResult>(Guid instanceId, Func<Instance, TResult> update, CancellationToken cancellationToken = default)
            => inner.UpdateAsync(instanceId, update, cancellationToken);
    }
}
