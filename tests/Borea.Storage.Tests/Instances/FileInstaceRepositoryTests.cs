using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Storage.Instances;
using Borea.Storage.Tests.Mods;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Instances;

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

        // Build a mod whose release carries a stamped dependency, attach it, and persist the change.
        var stampedDependency = new ModDependency("some-other-mod", ModDependencyKind.Required, ModVersion.Parse("1.0.0"));
        var installedMod = new InstalledMod(
            modId: "test-mod",
            version: ModVersion.Parse("1.2.3"),
            reason: InstallReason.Manual,
            installedAt: DateTimeOffset.UtcNow,
            metadata: MetadataFixtures.MinimalRelease("test-mod", "1.2.3", new[] { stampedDependency }));

        instance.AddMod(installedMod);
        instance.SetFavorite(true);
        await _repository.SaveAsync(instance);

        // Reload via a FRESH repository, proving this came from disk, not memory.
        var freshRepository = new FileInstanceRepository(_pathProvider);
        var reloaded = await freshRepository.GetByIdAsync(instance.InstanceId);

        Assert.NotNull(reloaded);
        Assert.Equal("My Test Pack", reloaded!.Name);
        Assert.True(reloaded.IsFavorite);
        Assert.Single(reloaded.Mods);

        var reloadedMod = reloaded.Mods.Single();
        Assert.Equal("test-mod", reloadedMod.ModId);
        Assert.Equal(ModVersion.Parse("1.2.3"), reloadedMod.Version);
        Assert.Equal(ModVersion.Parse("1.2.3"), reloadedMod.Metadata.Version);
        Assert.Equal(InstallReason.Manual, reloadedMod.Reason);
        Assert.Equal("2026.7", reloadedMod.Metadata.GameMin);

        var reloadedDependency = reloadedMod.Metadata.Dependencies.Single();
        Assert.Equal("some-other-mod", reloadedDependency.ModId);
        Assert.Equal(ModDependencyKind.Required, reloadedDependency.Kind);
        Assert.Equal(ModVersion.Parse("1.0.0"), reloadedDependency.MinVersion);

        Assert.Equal(installedMod.InstalledAt, reloadedMod.InstalledAt);
    }

    [Fact]
    public async Task RoundTrip_FullReleaseAndTwoMods_SurvivesTheNesting()
    {
        var instance = await _repository.CreateAsync("Deep Nesting", InstanceSource.Custom.Value);

        var anyOfDependency = ModDependency.OfAlternatives(ModDependencyKind.Required, new[]
        {
            new ModDependencyAlternative("audio-a", ModVersion.Parse("2.0.0")),
            new ModDependencyAlternative("audio-b"),
        });

        instance.AddMod(new InstalledMod(
            "test-mod", ModVersion.Parse("1.2.0-beta.1"), InstallReason.Manual, DateTimeOffset.UtcNow,
            MetadataFixtures.FullRelease("test-mod")));
        instance.AddMod(new InstalledMod(
            "second-mod", ModVersion.Parse("2.0.0"), InstallReason.Dependency, DateTimeOffset.UtcNow,
            MetadataFixtures.MinimalRelease("second-mod", "2.0.0", new[] { anyOfDependency })));
        await _repository.SaveAsync(instance);

        var reloaded = await new FileInstanceRepository(_pathProvider).GetByIdAsync(instance.InstanceId);

        Assert.Equal(2, reloaded!.Mods.Count);
        var fullMod = reloaded.Mods.Single(m => m.ModId == "test-mod");
        Assert.Equal(2, fullMod.Metadata.Dependencies.Count);
        Assert.Equal(MetadataSource.Derived, fullMod.Metadata.Dependencies[1].Source);
        Assert.Equal("https://forums.example/thread/1", fullMod.Metadata.Listing!.Links["forums"]);

        var secondMod = reloaded.Mods.Single(m => m.ModId == "second-mod");
        var anyOf = secondMod.Metadata.Dependencies.Single(d => d.IsAnyOf);
        Assert.Equal(2, anyOf.AnyOf!.Count);
        Assert.Equal("audio-b", anyOf.AnyOf[1].ModId);
    }

    [Fact]
    public async Task GetAllAsync_SkipsAnUnreadableInstanceInsteadOfFailing()
    {
        var healthy = await _repository.CreateAsync("Healthy", InstanceSource.Custom.Value);

        var brokenId = Guid.NewGuid();
        var brokenPath = _pathProvider.GetInstanceMetadataPath(brokenId);
        Directory.CreateDirectory(Path.GetDirectoryName(brokenPath)!);
        File.WriteAllText(brokenPath, "InstanceId = \"not-even-a-guid\"");

        var all = await _repository.GetAllAsync();

        Assert.Single(all);
        Assert.Equal(healthy.InstanceId, all[0].InstanceId);
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

    [Fact]
    public async Task GetActiveInstanceIdAsync_NoPointerSet_ReturnsNull()
    {
        var result = await _repository.GetActiveInstanceIdAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task SetActiveInstanceAsync_ThenGet_RoundTrips()
    {
        var instance = await _repository.CreateAsync("Active Test", InstanceSource.Custom.Value);

        await _repository.SetActiveInstanceAsync(instance.InstanceId);
        var activeId = await _repository.GetActiveInstanceIdAsync();

        Assert.Equal(instance.InstanceId, activeId);
    }

    [Fact]
    public async Task SetActiveInstanceAsync_NonexistentInstance_ThrowsInvalidOperationException()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.SetActiveInstanceAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetActiveInstanceIdAsync_PersistsAcrossFreshRepository()
    {
        var instance = await _repository.CreateAsync("Persisted Active", InstanceSource.Custom.Value);
        await _repository.SetActiveInstanceAsync(instance.InstanceId);

        var freshRepository = new FileInstanceRepository(_pathProvider);
        var activeId = await freshRepository.GetActiveInstanceIdAsync();

        Assert.Equal(instance.InstanceId, activeId);
    }

    [Fact]
    public async Task GetActiveInstanceIdAsync_MalformedPointerFile_ReturnsNullRatherThanThrowing()
    {
        // Confirms the deliberate asymmetry: a corrupted/malformed active-instance
        // pointer degrades to "nothing selected" rather than surfacing an error,
        // since "no active instance" is a normal state (first run), unlike a
        // corrupted Instance itself which is meant to fail loudly.
        var pointerPath = _pathProvider.GetActiveInstancePointerPath();
        Directory.CreateDirectory(Path.GetDirectoryName(pointerPath)!);
        File.WriteAllText(pointerPath, "ActiveInstanceId = \"not-a-valid-guid\"");

        var result = await _repository.GetActiveInstanceIdAsync();

        Assert.Null(result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
