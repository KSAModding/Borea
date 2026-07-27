using Borea.Storage.State;

namespace Borea.Storage.Tests;

public sealed class FileModStateRepositoryTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly TestGamePathProvider _pathProvider;
    private readonly FileModStateRepository _repository;
    private readonly Guid _instanceId = Guid.NewGuid();

    public FileModStateRepositoryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + _instanceId);
        _pathProvider = new TestGamePathProvider(_tempRoot);
        _repository = new FileModStateRepository(_pathProvider);
    }

    [Fact]
    public async Task ReadsExistingManifest_MatchingStarMapFormat()
    {
        // Simulates a manifest KSA itself already wrote.
        var manifestPath = _pathProvider.GetInstanceManifestPath(_instanceId);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(manifestPath, """
            [[mods]]
            id="Core"
            enabled = true

            [[mods]]
            id="some-other-mod"
            enabled = false
            """);

        Assert.True(await _repository.IsActiveAsync(_instanceId, "Core"));
        Assert.False(await _repository.IsActiveAsync(_instanceId, "some-other-mod"));

        var active = await _repository.GetAllActiveModIdsAsync(_instanceId);
        Assert.Equal(new[] { "Core" }, active);
    }

    [Fact]
    public async Task MissingManifest_ReturnsInactiveDefaults()
    {
        Assert.False(await _repository.IsActiveAsync(_instanceId, "anything"));
        Assert.Empty(await _repository.GetAllActiveModIdsAsync(_instanceId));
    }

    [Fact]
    public async Task SetActiveAsync_CreatesEntryWhenMissing()
    {
        await _repository.SetActiveAsync(_instanceId, "new-mod");

        Assert.True(await _repository.IsActiveAsync(_instanceId, "new-mod"));
    }

    [Fact]
    public async Task SetActiveAsync_PreservesOtherEntries()
    {
        await _repository.SetActiveAsync(_instanceId, "mod-a");
        await _repository.SetActiveAsync(_instanceId, "mod-b");
        await _repository.SetInactiveAsync(_instanceId, "mod-a");

        Assert.False(await _repository.IsActiveAsync(_instanceId, "mod-a"));
        Assert.True(await _repository.IsActiveAsync(_instanceId, "mod-b"));
    }

    [Fact]
    public async Task SetInactiveAsync_OnUntrackedMod_IsNoOp()
    {
        await _repository.SetInactiveAsync(_instanceId, "never-existed");

        var manifestPath = _pathProvider.GetInstanceManifestPath(_instanceId);
        Assert.False(File.Exists(manifestPath)); // Should not have created a file for a no-op.
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}