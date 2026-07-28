using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Storage.Instances;

namespace Borea.Storage.Tests;

public sealed class FileInstanceRepositoryTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly TestGamePathProvider _pathProvider;
    private readonly FileInstanceRepository _repository;

    public FileInstanceRepositoryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
        _pathProvider = new TestGamePathProvider(_tempRoot);
        _repository = new FileInstanceRepository(_pathProvider);
    }

    [Fact]
    public async Task RoundTrip_PersistsInstanceWithModAndDependency()
    {
        // Create a custom instance.
        var instance = await _repository.CreateAsync("My Test Pack", InstanceSource.Custom.Value);

        // Build a mod with a dependency, attach it, and persist the change.
        var dependency = new ModDependency("some-other-mod", VersionRange.Parse(">=1.0.0"), isOptional: false);
        var metadata = new ModMetadata(
            modId: "test-mod",
            name: "Test Mod",
            author: "MrJeranimo",
            version: ModVersion.Parse("1.2.3"),
            builtForGameVersion: GameVersion.Parse("2026.7.4.2131"),
            description: "A mod used purely for round-trip testing.",
            releasedAt: DateTimeOffset.UtcNow,
            fileSizeBytes: 1024,
            dependencies: new[] { dependency },
            tags: new[] { "test", "utility" });

        var installedMod = new InstalledMod(
            modId: "test-mod",
            version: ModVersion.Parse("1.2.3"),
            reason: InstallReason.Manual,
            installedAt: DateTimeOffset.UtcNow,
            metadata: metadata);

        instance.AddMod(installedMod);
        instance.SetFavorite(true);
        await _repository.SaveAsync(instance);

        // Reload via a FRESH repository — proves this came from disk, not memory.
        var freshRepository = new FileInstanceRepository(_pathProvider);
        var reloaded = await freshRepository.GetByIdAsync(instance.InstanceId);

        Assert.NotNull(reloaded);
        Assert.Equal("My Test Pack", reloaded!.Name);
        Assert.True(reloaded.IsFavorite);
        Assert.Single(reloaded.Mods);

        var reloadedMod = reloaded.Mods.Single();
        Assert.Equal("test-mod", reloadedMod.ModId);
        Assert.Equal(ModVersion.Parse("1.2.3"), reloadedMod.Version);
        Assert.Equal(GameVersion.Parse("2026.7.4.2131"), reloadedMod.Metadata.BuiltForGameVersion);
        Assert.Equal(InstallReason.Manual, reloadedMod.Reason);
        Assert.Equal("Test Mod", reloadedMod.Metadata.Name);
        Assert.Single(reloadedMod.Metadata.Dependencies);

        var reloadedDependency = reloadedMod.Metadata.Dependencies.Single();
        Assert.Equal("some-other-mod", reloadedDependency.ModId);
        Assert.False(reloadedDependency.IsOptional);
    }

    [Fact]
    public async Task RenameAsync_PersistsNewName()
    {
        var instance = await _repository.CreateAsync("Original Name", InstanceSource.Custom.Value);

        await _repository.RenameAsync(instance.InstanceId, "Renamed Pack");

        var reloaded = await _repository.GetByIdAsync(instance.InstanceId);
        Assert.Equal("Renamed Pack", reloaded?.Name);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenNameAlreadyTaken()
    {
        await _repository.CreateAsync("Duplicate Name", InstanceSource.Custom.Value);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.CreateAsync("Duplicate Name", InstanceSource.Custom.Value));
    }

    [Fact]
    public async Task DeleteAsync_IsIdempotent()
    {
        // Deleting a nonexistent instance should not throw.
        await _repository.DeleteAsync(Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}