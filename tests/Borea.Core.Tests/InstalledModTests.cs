using Borea.Core.Mods;

namespace Borea.Core.Tests;

public sealed class InstalledModTests
{
    [Fact]
    public void Constructor_ValidInput_SetsAllProperties()
    {
        var metadata = TestFixtures.SampleModMetadata("test-mod", "1.0.0");

        var installedMod = new InstalledMod(
            "test-mod", ModVersion.Parse("1.0.0"), InstallReason.Manual, DateTimeOffset.UtcNow, metadata);

        Assert.Equal("test-mod", installedMod.ModId);
        Assert.Equal(ModVersion.Parse("1.0.0"), installedMod.Version);
        Assert.Equal(InstallReason.Manual, installedMod.Reason);
        Assert.Same(metadata, installedMod.Metadata);
        Assert.Null(installedMod.Checksum);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidModId_ThrowsArgumentException(string? modId)
    {
        var metadata = TestFixtures.SampleModMetadata("test-mod", "1.0.0");

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
        var metadata = TestFixtures.SampleModMetadata("other-mod", "1.0.0");

        Assert.Throws<ArgumentException>(() =>
            new InstalledMod("test-mod", ModVersion.Parse("1.0.0"), InstallReason.Manual, DateTimeOffset.UtcNow, metadata));
    }

    [Fact]
    public void Constructor_MetadataVersionMismatch_ThrowsArgumentException()
    {
        // Assumes the Version/Metadata.Version cross-check discussed earlier
        // in the conversation was added alongside the ModId check. If it
        // wasn't, this is the one test in this file to remove.
        var metadata = TestFixtures.SampleModMetadata("test-mod", "1.0.0");

        Assert.Throws<ArgumentException>(() =>
            new InstalledMod("test-mod", ModVersion.Parse("2.0.0"), InstallReason.Manual, DateTimeOffset.UtcNow, metadata));
    }

    [Fact]
    public void Constructor_Checksum_IsOptionalAndDefaultsToNull()
    {
        var metadata = TestFixtures.SampleModMetadata("test-mod", "1.0.0");

        var withChecksum = new InstalledMod(
            "test-mod", ModVersion.Parse("1.0.0"), InstallReason.Manual, DateTimeOffset.UtcNow, metadata, "abc123");

        Assert.Equal("abc123", withChecksum.Checksum);
    }

    [Fact]
    public void MarkAsManuallyInstalled_SetsReasonToManual()
    {
        var metadata = TestFixtures.SampleModMetadata("test-mod", "1.0.0");
        var installedMod = new InstalledMod(
            "test-mod", ModVersion.Parse("1.0.0"), InstallReason.Dependency, DateTimeOffset.UtcNow, metadata);

        installedMod.MarkAsManuallyInstalled();

        Assert.Equal(InstallReason.Manual, installedMod.Reason);
    }
}
