using Borea.Core.Dependencies;
using Borea.Core.Mods;

namespace Borea.Core.Tests.Dependencies;

public sealed class ModDependencyTests
{
    [Fact]
    public void Constructor_ValidInput_SetsAllProperties()
    {
        var range = VersionRange.Parse(">=1.2.0");

        var dependency = new ModDependency("cool-lib", range);

        Assert.Equal("cool-lib", dependency.ModId);
        Assert.Equal(range, dependency.RequiredVersion);
        Assert.False(dependency.IsOptional);
    }

    [Fact]
    public void Constructor_IsOptional_DefaultsToFalse()
    {
        var dependency = new ModDependency("cool-lib", VersionRange.Parse("1.0.0"));

        Assert.False(dependency.IsOptional);
    }

    [Fact]
    public void Constructor_IsOptionalExplicitTrue_IsSet()
    {
        var dependency = new ModDependency("cool-lib", VersionRange.Parse("1.0.0"), isOptional: true);

        Assert.True(dependency.IsOptional);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidModId_ThrowsArgumentException(string? modId)
    {
        Assert.Throws<ArgumentException>(() => new ModDependency(modId!, VersionRange.Parse("1.0.0")));
    }

    [Fact]
    public void Constructor_NullRequiredVersion_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ModDependency("cool-lib", null!));
    }

    [Fact]
    public void ToString_RequiredDependency_OmitsOptionalSuffix()
    {
        var dependency = new ModDependency("cool-lib", VersionRange.Parse(">=1.2.0"));

        Assert.Equal("cool-lib >=1.2.0", dependency.ToString());
    }

    [Fact]
    public void ToString_OptionalDependency_IncludesOptionalSuffix()
    {
        var dependency = new ModDependency("cool-lib", VersionRange.Parse(">=1.2.0"), isOptional: true);

        Assert.Equal("cool-lib >=1.2.0 (optional)", dependency.ToString());
    }
}