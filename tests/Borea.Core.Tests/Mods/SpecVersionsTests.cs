using Borea.Core.Mods;

namespace Borea.Core.Tests.Mods;

public sealed class SpecVersionsTests
{
    [Fact]
    public void Highest_IsTheOneVersionTheFormatDefines()
    {
        Assert.Equal(1, SpecVersions.Highest);
    }

    [Fact]
    public void IsAboveHighest_AtHighest_ReturnsFalse()
    {
        Assert.False(SpecVersions.IsAboveHighest(SpecVersions.Highest));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void IsAboveHighest_NonPositive_ReturnsFalse(int specVersion)
    {
        Assert.False(SpecVersions.IsAboveHighest(specVersion));
    }

    [Theory]
    [InlineData(SpecVersions.Highest + 1)]
    [InlineData(SpecVersions.Highest + 6)]
    [InlineData(int.MaxValue)]
    public void IsAboveHighest_AboveHighest_ReturnsTrue(int specVersion)
    {
        Assert.True(SpecVersions.IsAboveHighest(specVersion));
    }
}
