using Borea.Core.Mods;

namespace Borea.Core.Tests.Mods;

public sealed class SpecVersionsTests
{
    [Fact]
    public void Highest_IsTheOneVersionTheFormatDefines()
    {
        Assert.Equal(1, SpecVersions.Highest);
    }

    [Theory]
    [InlineData(int.MinValue, false)]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(SpecVersions.Highest, false)]
    [InlineData(SpecVersions.Highest + 1, true)]
    [InlineData(SpecVersions.Highest + 6, true)]
    [InlineData(int.MaxValue, true)]
    public void IsAboveHighest_IsTrueOnlyAboveHighest(int specVersion, bool expected)
    {
        Assert.Equal(expected, SpecVersions.IsAboveHighest(specVersion));
    }
}
