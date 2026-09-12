using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Storage.Instances;
using Borea.Storage.Mods;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Mods;

public sealed class FileModUninstallerTests : IAsyncLifetime
{
    private readonly string _tempRoot;
    private readonly TestGamePathProvider _pathProvider;
    private readonly FileInstanceRepository _instances;
    private readonly FileModUninstaller _uninstaller;
    private Guid _instanceId;

    public FileModUninstallerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
        _pathProvider = new TestGamePathProvider(_tempRoot);
        _instances = new FileInstanceRepository(_pathProvider);
        _uninstaller = new FileModUninstaller(_pathProvider, _instances);
    }

    public async Task InitializeAsync()
    {
        _instanceId = (await _instances.CreateAsync("Test", InstanceSource.Custom.Value)).InstanceId;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task UninstallAsync_BoreaOwnedFolder_DeletesIt()
    {
        await AddInstalledModAsync(ModInstallOwnership.Borea);
        var modDir = ModDirectory("test-mod");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "some-file.dat"), "content");

        await _uninstaller.UninstallAsync(_instanceId, "test-mod");

        Assert.False(Directory.Exists(modDir));
    }

    [Fact]
    public async Task UninstallAsync_UntrackedFolder_LeavesItIntact()
    {
        var modDir = ModDirectory("never-installed");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "local-file.dat"), "content");

        await _uninstaller.UninstallAsync(_instanceId, "never-installed");

        Assert.True(File.Exists(Path.Combine(modDir, "local-file.dat")));
    }

    [Fact]
    public async Task UninstallAsync_ForeignOwnedRecord_LeavesFolderIntact()
    {
        await AddInstalledModAsync(ModInstallOwnership.Foreign);
        var modDir = ModDirectory("test-mod");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "local-file.dat"), "content");

        await _uninstaller.UninstallAsync(_instanceId, "test-mod");

        Assert.True(File.Exists(Path.Combine(modDir, "local-file.dat")));
    }

    [Fact]
    public async Task UninstallAsync_ReplacedOwnedFolder_LeavesItIntact()
    {
        await AddInstalledModAsync(ModInstallOwnership.Borea);
        var modDir = ModDirectory("test-mod");
        Directory.Delete(modDir, recursive: true);
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "local-file.dat"), "content");

        await _uninstaller.UninstallAsync(_instanceId, "test-mod");

        Assert.True(File.Exists(Path.Combine(modDir, "local-file.dat")));
    }

    [Fact]
    public async Task UninstallAsync_LegacyBoreaRecordWithoutToken_ReportsActionableError()
    {
        await AddInstalledModAsync(ModInstallOwnership.Borea, createOwnershipMarker: false);
        var modDir = ModDirectory("test-mod");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "local-file.dat"), "content");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _uninstaller.UninstallAsync(_instanceId, "test-mod"));

        Assert.Contains("cannot verify ownership", exception.Message);
        Assert.True(File.Exists(Path.Combine(modDir, "local-file.dat")));
    }

    [Fact]
    public async Task UninstallAsync_OnlyDeletesTargetMod_LeavesSiblingsIntact()
    {
        await AddInstalledModAsync(ModInstallOwnership.Borea, "mod-to-remove");
        var targetDir = ModDirectory("mod-to-remove");
        var siblingDir = ModDirectory("mod-to-keep");
        Directory.CreateDirectory(targetDir);
        Directory.CreateDirectory(siblingDir);

        await _uninstaller.UninstallAsync(_instanceId, "mod-to-remove");

        Assert.False(Directory.Exists(targetDir));
        Assert.True(Directory.Exists(siblingDir));
    }

    [Fact]
    public async Task UninstallAsync_CanceledBeforeRead_LeavesOwnedFolderIntact()
    {
        await AddInstalledModAsync(ModInstallOwnership.Borea);
        var modDir = ModDirectory("test-mod");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _uninstaller.UninstallAsync(_instanceId, "test-mod", cancellation.Token));

        Assert.True(Directory.Exists(modDir));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UninstallAsync_InvalidModId_ThrowsArgumentException(string? modId)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _uninstaller.UninstallAsync(_instanceId, modId!));
    }

    [Fact]
    public void Constructor_NullDependency_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new FileModUninstaller(null!, _instances));
        Assert.Throws<ArgumentNullException>(() => new FileModUninstaller(_pathProvider, null!));
    }

    private string ModDirectory(string modId) => Path.Combine(_pathProvider.GetInstanceModsFolder(_instanceId), modId);

    private async Task AddInstalledModAsync(
        ModInstallOwnership ownership,
        string modId = "test-mod",
        bool createOwnershipMarker = true)
    {
        var instance = await _instances.GetByIdAsync(_instanceId);
        var release = MetadataFixtures.MinimalRelease(modId);
        var ownershipToken = ownership == ModInstallOwnership.Borea && createOwnershipMarker
            ? Guid.NewGuid().ToString("N")
            : null;
        instance!.AddMod(new InstalledMod(
            modId,
            release.Version,
            InstallReason.Manual,
            DateTimeOffset.UtcNow,
            release,
            ownership: ownership,
            ownershipToken: ownershipToken));
        await _instances.SaveAsync(instance);

        if (ownershipToken is not null)
        {
            var modDirectory = ModDirectory(modId);
            Directory.CreateDirectory(modDirectory);
            File.WriteAllText(Path.Combine(modDirectory, ".borea-owner"), ownershipToken);
        }
    }
}
