using System.Security.Cryptography;
using Borea.Core.Dependencies;
using Borea.Core.Files;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Storage.Files;
using Borea.Storage.Instances;
using Borea.Storage.Mods;
using Borea.Storage.State;
using Borea.Storage.Tests.Launch;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Mods;

/// <summary>The installers, the uninstaller and instance deletion with the store on.</summary>
public sealed class ModStoreTests : IAsyncLifetime
{
    private const string ModId = "test-mod";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly FakeModDownloader _downloader = new() { Bytes = Archive("one") };
    private readonly SwitchableLinker _linker = new();
    private readonly TestGamePathProvider _paths;
    private readonly ModStore _store;
    private readonly FileInstanceRepository _instances;
    private readonly FileModStateRepository _state;
    private Guid _first;
    private Guid _second;

    public ModStoreTests()
    {
        _paths = new TestGamePathProvider(_root);
        _store = new ModStore(_paths, _linker, linksReleases: true);
        _instances = new FileInstanceRepository(_paths, _store);
        _state = new FileModStateRepository(_paths);
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

    private FileModInstaller Installer => new(_paths, _downloader, _instances, _state, store: _store);

    private FileModUninstaller Uninstaller => new(_paths, _instances, _store);

    private string StoredMod => Path.Combine(_paths.GetStaticModFilesRoot(), ModId);

    [Fact]
    public async Task InstallAsync_SameReleaseInASecondInstance_DownloadsNothingAndLinksTheStoredRelease()
    {
        var release = Release(Sha256Of(_downloader.Bytes));
        await InstallAsync(_first, release);

        var second = await Installer.InstallAsync(_second, release, InstallReason.Manual, enable: true);

        Assert.Single(_downloader.ArchivePaths);
        Assert.Equal(0, second.Download.BytesDownloaded);
        var entry = Assert.Single(StoredReleases());
        Assert.Equal($"1.2.0-{Sha256Of(_downloader.Bytes)[..12].ToLowerInvariant()}", Path.GetFileName(entry));
        Assert.Equal(entry, _linker.GetTarget(ModFolder(_first)));
        Assert.Equal(entry, _linker.GetTarget(ModFolder(_second)));
        Assert.Equal("one", ReadValue(_second));
        Assert.False(File.Exists(Path.Combine(entry, ModFolders.OwnershipFileName)));

        var recorded = Assert.Single((await _instances.GetByIdAsync(_second))!.Mods);
        Assert.Equal(ModStorage.Linked, recorded.Storage);
        Assert.Null(recorded.OwnershipToken);
        Assert.True(recorded.CanDeleteFiles);
    }

    [Fact]
    public async Task InstallAsync_SameVersionWithOtherBytes_NeverSharesAStorePath()
    {
        await InstallAsync(_first, Release(sha256: null));
        _downloader.Bytes = Archive("two");

        await InstallAsync(_second, Release(sha256: null));

        Assert.Equal(2, StoredReleases().Length);
        Assert.Equal("one", ReadValue(_first));
        Assert.Equal("two", ReadValue(_second));
    }

    [Fact]
    public async Task InstallAsync_ModThatManagesPaths_IsCopiedAndNotLinked()
    {
        var installed = await InstallAsync(_first, Release(Sha256Of(_downloader.Bytes), manages: ["settings.json"]));

        Assert.Equal(ModStorage.Private, installed.Storage);
        Assert.False(_linker.IsLink(ModFolder(_first)));
        Assert.Equal(installed.OwnershipToken, File.ReadAllText(Path.Combine(ModFolder(_first), ModFolders.OwnershipFileName)));
        Assert.False(Directory.Exists(_paths.GetStaticModFilesRoot()));
    }

    [Fact]
    public async Task InstallAsync_EmptyManagesClaim_IsLinked()
    {
        var installed = await InstallAsync(_first, Release(Sha256Of(_downloader.Bytes), manages: []));

        Assert.Equal(ModStorage.Linked, installed.Storage);
        Assert.True(_linker.IsLink(ModFolder(_first)));
    }

    [Fact]
    public async Task InstallAsync_LinkRefused_CopiesAndRecordsTheCopyAsPrivate()
    {
        _linker.Refuse = true;

        var installed = await InstallAsync(_first, Release(Sha256Of(_downloader.Bytes)));

        Assert.Equal(ModStorage.Private, installed.Storage);
        Assert.Equal(ModStorage.Private, Assert.Single((await _instances.GetByIdAsync(_first))!.Mods).Storage);
        Assert.False(_linker.IsLink(ModFolder(_first)));
        Assert.Equal("one", ReadValue(_first));
        Assert.Equal(installed.OwnershipToken, File.ReadAllText(Path.Combine(ModFolder(_first), ModFolders.OwnershipFileName)));
        Assert.False(Directory.Exists(StoredMod));
    }

    [Fact]
    public async Task InstallAsync_FailsAfterTheLinkWasStaged_RemovesTheLinkAndTheStoredRelease()
    {
        var otherInstance = InstallPlanningState.Capture((await _instances.GetByIdAsync(_second))!);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Installer.InstallGuardedAsync(_first, Release(Sha256Of(_downloader.Bytes)), InstallReason.Manual, enable: true, otherInstance));

        Assert.False(Directory.Exists(ModFolder(_first)));
        Assert.Empty(Directory.EnumerateDirectories(_paths.GetInstanceRoot(_first), ".borea-*"));
        Assert.False(Directory.Exists(StoredMod));
    }

    [Fact]
    public async Task UninstallAsync_OneOfTwoInstances_LeavesTheOtherInstanceAndTheStoredRelease()
    {
        var release = Release(Sha256Of(_downloader.Bytes));
        await InstallAsync(_first, release);
        await InstallAsync(_second, release);

        await Uninstaller.UninstallAsync(_first, ModId);

        Assert.False(Directory.Exists(ModFolder(_first)));
        Assert.Empty((await _instances.GetByIdAsync(_first))!.Mods);
        Assert.Equal("one", ReadValue(_second));
        Assert.Single(StoredReleases());
    }

    [Fact]
    public async Task UninstallAsync_LastReference_RemovesTheStoredRelease()
    {
        var release = Release(Sha256Of(_downloader.Bytes));
        await InstallAsync(_first, release);
        await InstallAsync(_second, release);

        await Uninstaller.UninstallAsync(_first, ModId);
        await Uninstaller.UninstallAsync(_second, ModId);

        Assert.False(Directory.Exists(StoredMod));
    }

    [WindowsFact("Only Windows refuses to delete a file that is open.")]
    public async Task UninstallAsync_StoredReleaseCannotBeDeleted_LeavesNoPartialReleaseForTheNextInstall()
    {
        var release = Release(Sha256Of(_downloader.Bytes));
        await InstallAsync(_first, release);
        var entry = Assert.Single(StoredReleases());

        using (new FileStream(Path.Combine(entry, "value.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
            await Uninstaller.UninstallAsync(_first, ModId);
        await InstallAsync(_second, release);

        Assert.True(File.Exists(Path.Combine(ModFolder(_second), "mod.toml")));
        Assert.Equal("one", ReadValue(_second));
    }

    [Fact]
    public async Task UninstallAsync_LinkThatLeadsElsewhere_IsNotOwnedAndStays()
    {
        await InstallAsync(_first, Release(Sha256Of(_downloader.Bytes)));
        var own = Directory.CreateDirectory(Path.Combine(_root, "own")).FullName;
        File.WriteAllText(Path.Combine(own, "mod.toml"), "name = \"mine\"");
        _linker.Remove(ModFolder(_first));
        Assert.True(_linker.TryCreate(ModFolder(_first), own).Linked);

        await Uninstaller.UninstallAsync(_first, ModId);

        Assert.Equal(own, _linker.GetTarget(ModFolder(_first)));
        Assert.True(File.Exists(Path.Combine(own, "mod.toml")));
    }

    [Fact]
    public async Task DeleteAsync_InstanceWithTheLastLink_RemovesTheStoredRelease()
    {
        var release = Release(Sha256Of(_downloader.Bytes));
        await InstallAsync(_first, release);
        await InstallAsync(_second, release);

        await _instances.DeleteAsync(_first);
        Assert.Equal("one", ReadValue(_second));

        await _instances.DeleteAsync(_second);
        Assert.False(Directory.Exists(StoredMod));
    }

    [Fact]
    public async Task ReplaceAsync_LinkedMod_LinksTheNewReleaseAndRemovesTheOldOne()
    {
        var previous = await InstallAsync(_first, Release(Sha256Of(_downloader.Bytes)));
        var previousEntry = _linker.GetTarget(ModFolder(_first))!;
        _downloader.Bytes = Archive("two");
        var replacer = new FileModReplacer(_paths, _downloader, _instances, _state, store: _store);

        var result = await replacer.ReplaceAsync(_first, previous, Release(Sha256Of(_downloader.Bytes), version: "1.3.0"));

        Assert.Equal(ModStorage.Linked, result.Replacement.Storage);
        Assert.Equal("two", ReadValue(_first));
        Assert.False(Directory.Exists(previousEntry));
        Assert.Equal(_linker.GetTarget(ModFolder(_first)), Assert.Single(StoredReleases()));
        Assert.Empty(Directory.EnumerateDirectories(_paths.GetInstanceRoot(_first), ".borea-*"));
    }

    [Fact]
    public async Task ReplaceAsync_PreviousReleaseLinkedElsewhere_KeepsIt()
    {
        var release = Release(Sha256Of(_downloader.Bytes));
        var previous = await InstallAsync(_first, release);
        await InstallAsync(_second, release);
        _downloader.Bytes = Archive("two");
        var replacer = new FileModReplacer(_paths, _downloader, _instances, _state, store: _store);

        await replacer.ReplaceAsync(_first, previous, Release(Sha256Of(_downloader.Bytes), version: "1.3.0"));

        Assert.Equal("two", ReadValue(_first));
        Assert.Equal("one", ReadValue(_second));
        Assert.Equal(2, StoredReleases().Length);
    }

    [Fact]
    public async Task TakeOwnershipAsync_ForeignFolder_IsReplacedByALink()
    {
        var release = Release(Sha256Of(_downloader.Bytes));
        Directory.CreateDirectory(ModFolder(_first));
        File.WriteAllText(Path.Combine(ModFolder(_first), "mod.toml"), "name = \"test\"");
        await _instances.UpdateAsync(_first, instance =>
        {
            instance.AddMod(new InstalledMod(ModId, release.Version, InstallReason.Manual, DateTimeOffset.UtcNow, release, "ABCD", ModInstallOwnership.Foreign));
            return true;
        });
        await _state.AddEntryAsync(_first, ModId, enabled: true);
        var handover = new FileForeignModHandover(_paths, _downloader, _instances, _state, store: _store);

        var owned = (await handover.TakeOwnershipAsync(_first, ModId)).Installed;

        Assert.Equal(ModStorage.Linked, owned.Storage);
        Assert.Equal(Assert.Single(StoredReleases()), _linker.GetTarget(ModFolder(_first)));
        Assert.Equal("one", ReadValue(_first));
        Assert.Empty(Directory.EnumerateDirectories(_paths.GetInstanceRoot(_first), ".borea-*"));
    }

    private async Task<InstalledMod> InstallAsync(Guid instanceId, ModVersionMetadata release)
        => (await Installer.InstallAsync(instanceId, release, InstallReason.Manual, enable: true)).Mod;

    private string ModFolder(Guid instanceId) => Path.Combine(_paths.GetInstanceModsFolder(instanceId), ModId);

    private string ReadValue(Guid instanceId) => File.ReadAllText(Path.Combine(ModFolder(instanceId), "value.txt"));

    private string[] StoredReleases() => Directory.Exists(StoredMod) ? Directory.GetDirectories(StoredMod) : [];

    private static byte[] Archive(string value) => TestArchives.Build((ModId + "/mod.toml", "name = \"test\""), (ModId + "/value.txt", value));

    private static string Sha256Of(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static ModVersionMetadata Release(string? sha256, string version = "1.2.0", IReadOnlyList<string>? manages = null) => new(
        specVersion: 1,
        modId: ModId,
        version: ModVersion.Parse(version),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        gameMin: "2026.7.4.2131",
        gameMinRevision: 2131,
        download: new DownloadInfo("https://example.com/mod.zip", sha256, null, "application/zip"),
        installSizeBytes: null,
        dependencies: Array.Empty<ModDependency>(),
        manages: manages);

    /// <summary>The real linker, which can be told to refuse every link the way a filesystem without links does.</summary>
    private sealed class SwitchableLinker : IDirectoryLinker
    {
        private readonly DirectoryLinker _inner = new();

        public bool Refuse { get; set; }

        public DirectoryLinkResult TryCreate(string linkPath, string targetPath)
            => Refuse ? DirectoryLinkResult.NotLinked("This filesystem has no links.") : _inner.TryCreate(linkPath, targetPath);

        public bool IsLink(string path) => _inner.IsLink(path);

        public string? GetTarget(string path) => _inner.GetTarget(path);

        public void Remove(string linkPath) => _inner.Remove(linkPath);
    }
}
