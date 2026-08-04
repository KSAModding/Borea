using Borea.Core.Dependencies;
using Borea.Core.Mods;

namespace Borea.Core.Tests.Dependencies;

public sealed class UninstallCheckTests
{
    [Fact]
    public void Constructor_ValidInput_SetsAllProperties()
    {
        var instanceId = Guid.NewGuid();

        var check = new UninstallCheck(instanceId, "test-mod", ModVersion.Parse("1.0.0"), new[] { "dependent-a" }, isActive: true);

        Assert.Equal(instanceId, check.InstanceId);
        Assert.Equal("test-mod", check.ModId);
        Assert.True(check.IsActive);
        Assert.Single(check.DependentModIds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidModId_ThrowsArgumentException(string? modId)
    {
        Assert.Throws<ArgumentException>(() =>
            new UninstallCheck(Guid.NewGuid(), modId!, ModVersion.Parse("1.0.0"), Array.Empty<string>(), isActive: false));
    }

    [Fact]
    public void Constructor_NullDependentModIds_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new UninstallCheck(Guid.NewGuid(), "test-mod", ModVersion.Parse("1.0.0"), null!, isActive: false));
    }

    [Fact]
    public void CanUninstall_NoDependents_IsTrue()
    {
        var check = new UninstallCheck(Guid.NewGuid(), "test-mod", ModVersion.Parse("1.0.0"), Array.Empty<string>(), isActive: false);

        Assert.True(check.CanUninstall);
    }

    [Fact]
    public void CanUninstall_HasDependents_IsFalse()
    {
        var check = new UninstallCheck(Guid.NewGuid(), "test-mod", ModVersion.Parse("1.0.0"), new[] { "dependent-a" }, isActive: false);

        Assert.False(check.CanUninstall);
    }

    [Fact]
    public void CanUninstall_IsIndependentOfIsActive()
    {
        // Confirms the deliberate separation from the doc comment: IsActive
        // is informational only and never gates CanUninstall.
        var activeButUninstallable = new UninstallCheck(Guid.NewGuid(), "test-mod", ModVersion.Parse("1.0.0"), new[] { "dependent-a" }, isActive: true);
        var inactiveButFree = new UninstallCheck(Guid.NewGuid(), "test-mod", ModVersion.Parse("1.0.0"), Array.Empty<string>(), isActive: false);

        Assert.False(activeButUninstallable.CanUninstall);
        Assert.True(inactiveButFree.CanUninstall);
    }

    [Fact]
    public void DependentModIds_IsDefensiveCopy_NotAffectedByMutatingSourceList()
    {
        var source = new List<string> { "dependent-a" };
        var check = new UninstallCheck(Guid.NewGuid(), "test-mod", ModVersion.Parse("1.0.0"), source, isActive: false);

        source.Add("dependent-b");

        Assert.Single(check.DependentModIds);
    }
}
