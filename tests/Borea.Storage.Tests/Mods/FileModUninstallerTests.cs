using Borea.Storage.Mods;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Mods;

public sealed class FileModUninstallerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly TestGamePathProvider _pathProvider;
    private readonly FileModUninstaller _uninstaller;
    private readonly Guid _instanceId = Guid.NewGuid();

    public FileModUninstallerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + _instanceId);
        _pathProvider = new TestGamePathProvider(_tempRoot);
        _uninstaller = new FileModUninstaller(_pathProvider);
    }

    private string ModDirectory(string modId) =>
        Path.Combine(_pathProvider.GetInstanceModsFolder(_instanceId), modId);

    [Fact]
    public async Task UninstallAsync_ExistingModFolder_DeletesIt()
    {
        var modDir = ModDirectory("test-mod");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "some-file.dat"), "content");

        await _uninstaller.UninstallAsync(_instanceId, "test-mod");

        Assert.False(Directory.Exists(modDir));
    }

    [Fact]
    public async Task UninstallAsync_NonexistentModFolder_IsNoOpRatherThanThrowing()
    {
        await _uninstaller.UninstallAsync(_instanceId, "never-installed");

        Assert.False(Directory.Exists(ModDirectory("never-installed")));
    }

    [Fact]
    public async Task UninstallAsync_OnlyDeletesTargetMod_LeavesSiblingsIntact()
    {
        var targetDir = ModDirectory("mod-to-remove");
        var siblingDir = ModDirectory("mod-to-keep");
        Directory.CreateDirectory(targetDir);
        Directory.CreateDirectory(siblingDir);

        await _uninstaller.UninstallAsync(_instanceId, "mod-to-remove");

        Assert.False(Directory.Exists(targetDir));
        Assert.True(Directory.Exists(siblingDir));
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
    public void Constructor_NullPathProvider_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new FileModUninstaller(null!));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
