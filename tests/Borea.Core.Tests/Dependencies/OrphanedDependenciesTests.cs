using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.Core.Tests.Dependencies;

public sealed class OrphanedDependenciesTests
{
    [Fact]
    public void After_DependencyOfOnlyTheRemovedMod_IsUnusedWithTheDependencyItNamed()
    {
        var instance = InstanceWith(
            Mod("Stars", InstallReason.Manual, Required("Menu")),
            Mod("Menu", InstallReason.Dependency, Required("Library")),
            Mod("Library", InstallReason.Dependency));

        Assert.Equal(["Menu", "Library"], OrphanedDependencies.After(instance, "Stars").Select(mod => mod.ModId));
    }

    [Fact]
    public void After_DependencyThatAnotherModStillNames_Stays()
    {
        var instance = InstanceWith(
            Mod("Stars", InstallReason.Manual, Required("Menu")),
            Mod("Tools", InstallReason.Manual, new ModDependency("Menu", ModDependencyKind.Suggests)),
            Mod("Menu", InstallReason.Dependency));

        Assert.Empty(OrphanedDependencies.After(instance, "Stars"));
    }

    [Fact]
    public void After_ChoiceOfAnAnyOfEntry_Stays()
    {
        var instance = InstanceWith(
            Mod("Stars", InstallReason.Manual, Required("Menu")),
            Mod("Tools", InstallReason.Manual, ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("Other"), new ModDependencyAlternative("Menu")])),
            Mod("Menu", InstallReason.Dependency));

        Assert.Empty(OrphanedDependencies.After(instance, "Stars"));
    }

    [Theory]
    [InlineData(InstallReason.Manual, ModInstallOwnership.Borea)]
    [InlineData(InstallReason.Dependency, ModInstallOwnership.Foreign)]
    public void After_ModThePlayerChoseOrBoreaDoesNotOwn_Stays(InstallReason reason, ModInstallOwnership ownership)
    {
        var instance = InstanceWith(
            Mod("Stars", InstallReason.Manual, Required("Menu")),
            Mod("Menu", reason, ownership: ownership));

        Assert.Empty(OrphanedDependencies.After(instance, "Stars"));
    }

    [Fact]
    public void After_ModNamedOnlyByAConflict_IsUnused()
    {
        var instance = InstanceWith(
            Mod("Stars", InstallReason.Manual, Required("Menu")),
            Mod("Tools", InstallReason.Manual, new ModDependency("Menu", ModDependencyKind.Conflict)),
            Mod("Menu", InstallReason.Dependency));

        Assert.Equal(["Menu"], OrphanedDependencies.After(instance, "Stars").Select(mod => mod.ModId));
    }

    private static ModDependency Required(string modId) => new(modId, ModDependencyKind.Required);

    private static Instance InstanceWith(params InstalledMod[] mods) =>
        Instance.FromExisting(Guid.NewGuid(), "Main", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, mods, isFavorite: false);

    private static InstalledMod Mod(string modId, InstallReason reason, ModDependency? dependency = null, ModInstallOwnership ownership = ModInstallOwnership.Borea)
    {
        var release = new ModVersionMetadata(
            specVersion: 1,
            modId: modId,
            version: ModVersion.Parse("1.0.0"),
            releaseStatus: ReleaseStatus.Stable,
            releaseDate: DateTimeOffset.UtcNow,
            gameMin: "2026.7.4.2131",
            gameMinRevision: 2131,
            download: new DownloadInfo($"https://example.invalid/{modId}.zip", sha256: null, sizeBytes: null, contentType: "application/zip"),
            installSizeBytes: null,
            dependencies: dependency is null ? [] : [dependency]);
        return new InstalledMod(modId, release.Version, reason, DateTimeOffset.UtcNow, release, ownership: ownership);
    }
}
