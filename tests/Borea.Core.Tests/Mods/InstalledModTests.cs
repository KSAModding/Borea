using Borea.Core.Dependencies;
using Borea.Core.Mods;

namespace Borea.Core.Tests.Mods;

public sealed class InstalledModTests
{
    [Fact]
    public void Constructor_ValidInput_SetsAllProperties()
    {
        var metadata = TestFixtures.SampleVersionMetadata(
            "test-mod",
            dependencies: new[] { new ModDependency("cool-lib", ModDependencyKind.Required) });

        var installedMod = new InstalledMod(
            "test-mod", ModVersion.Parse("1.0.0"), InstallReason.Manual, DateTimeOffset.UtcNow, metadata);

        Assert.Equal("test-mod", installedMod.ModId);
        Assert.Equal(ModVersion.Parse("1.0.0"), installedMod.Version);
        Assert.Equal(InstallReason.Manual, installedMod.Reason);
        Assert.Same(metadata, installedMod.Metadata);
        Assert.Single(installedMod.Metadata.Dependencies);
        Assert.Null(installedMod.Checksum);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidModId_ThrowsArgumentException(string? modId)
    {
        var metadata = TestFixtures.SampleVersionMetadata("test-mod");

        Assert.Throws<ArgumentException>(() =>
            new InstalledMod(modId!, ModVersion.Parse("1.0.0"), InstallReason.Manual, DateTimeOffset.UtcNow, metadata));
    }

    [Fact]
    public void Constructor_NullMetadata_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new InstalledMod("test-mod", ModVersion.Parse("1.0.0"), InstallReason.Manual, DateTimeOffset.UtcNow, null!));
    }

    [Fact]
    public void Constructor_MetadataModIdMismatch_ThrowsArgumentException()
    {
        var metadata = TestFixtures.SampleVersionMetadata("other-mod");

        Assert.Throws<ArgumentException>(() =>
            new InstalledMod("test-mod", ModVersion.Parse("1.0.0"), InstallReason.Manual, DateTimeOffset.UtcNow, metadata));
    }

    [Fact]
    public void Constructor_MetadataModIdDifferingOnlyInCase_IsAccepted()
    {
        var metadata = TestFixtures.SampleVersionMetadata("Test-Mod");

        var installedMod = new InstalledMod(
            "test-mod", ModVersion.Parse("1.0.0"), InstallReason.Manual, DateTimeOffset.UtcNow, metadata);

        Assert.Same(metadata, installedMod.Metadata);
    }

    [Fact]
    public void Constructor_Checksum_IsOptionalAndDefaultsToNull()
    {
        var metadata = TestFixtures.SampleVersionMetadata("test-mod");

        var withChecksum = new InstalledMod(
            "test-mod", ModVersion.Parse("1.0.0"), InstallReason.Manual, DateTimeOffset.UtcNow, metadata, "abc123");

        Assert.Equal("abc123", withChecksum.Checksum);
    }

    [Fact]
    public void MarkAsManuallyInstalled_SetsReasonToManual()
    {
        var installedMod = TestFixtures.SampleInstalledMod("test-mod", reason: InstallReason.Dependency);

        installedMod.MarkAsManuallyInstalled();

        Assert.Equal(InstallReason.Manual, installedMod.Reason);
    }
}
