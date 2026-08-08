using Borea.Core.Dependencies;
using Borea.Core.Mods;

namespace Borea.Core.Tests.Mods;

public sealed class InstalledModTests
{
    [Fact]
    public void Constructor_ValidInput_SetsAllProperties()
    {
        var metadata = TestFixtures.SampleModMetadata("test-mod");
        var dependencies = new[] { new ModDependency("cool-lib", ModDependencyKind.Required) };

        var installedMod = new InstalledMod(
            "test-mod", ModVersion.Parse("1.0.0"), InstallReason.Manual, DateTimeOffset.UtcNow, metadata, dependencies);

        Assert.Equal("test-mod", installedMod.ModId);
        Assert.Equal(ModVersion.Parse("1.0.0"), installedMod.Version);
        Assert.Equal(InstallReason.Manual, installedMod.Reason);
        Assert.Same(metadata, installedMod.Metadata);
        Assert.Single(installedMod.Dependencies);
        Assert.Null(installedMod.Checksum);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidModId_ThrowsArgumentException(string? modId)
    {
        var metadata = TestFixtures.SampleModMetadata("test-mod");

        Assert.Throws<ArgumentException>(() =>
            new InstalledMod(modId!, ModVersion.Parse("1.0.0"), InstallReason.Manual, DateTimeOffset.UtcNow, metadata, Array.Empty<ModDependency>()));
    }

    [Fact]
    public void Constructor_NullMetadata_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new InstalledMod("test-mod", ModVersion.Parse("1.0.0"), InstallReason.Manual, DateTimeOffset.UtcNow, null!, Array.Empty<ModDependency>()));
    }

    [Fact]
    public void Constructor_NullDependencies_ThrowsArgumentNullException()
    {
        var metadata = TestFixtures.SampleModMetadata("test-mod");

        Assert.Throws<ArgumentNullException>(() =>
            new InstalledMod("test-mod", ModVersion.Parse("1.0.0"), InstallReason.Manual, DateTimeOffset.UtcNow, metadata, null!));
    }

    [Fact]
    public void Constructor_MetadataModIdMismatch_ThrowsArgumentException()
    {
        var metadata = TestFixtures.SampleModMetadata("other-mod");

        Assert.Throws<ArgumentException>(() =>
            new InstalledMod("test-mod", ModVersion.Parse("1.0.0"), InstallReason.Manual, DateTimeOffset.UtcNow, metadata, Array.Empty<ModDependency>()));
    }

    [Fact]
    public void Constructor_MetadataModIdDifferingOnlyInCase_IsAccepted()
    {
        var metadata = TestFixtures.SampleModMetadata("Test-Mod");

        var installedMod = new InstalledMod(
            "test-mod", ModVersion.Parse("1.0.0"), InstallReason.Manual, DateTimeOffset.UtcNow, metadata, Array.Empty<ModDependency>());

        Assert.Same(metadata, installedMod.Metadata);
    }

    [Fact]
    public void Constructor_Checksum_IsOptionalAndDefaultsToNull()
    {
        var metadata = TestFixtures.SampleModMetadata("test-mod");

        var withChecksum = new InstalledMod(
            "test-mod", ModVersion.Parse("1.0.0"), InstallReason.Manual, DateTimeOffset.UtcNow, metadata, Array.Empty<ModDependency>(), "abc123");

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
