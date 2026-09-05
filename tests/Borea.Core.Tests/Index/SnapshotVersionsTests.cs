using Borea.Core.Index;

namespace Borea.Core.Tests.Index;

public sealed class SnapshotVersionsTests
{
    [Fact]
    public void Highest_IsTheVersionTheSpecDefines()
    {
        Assert.Equal(1, SnapshotVersions.Highest);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void IsAboveHighest_IsFalseAtOrBelowTheCeiling(int snapshotVersion)
    {
        Assert.False(SnapshotVersions.IsAboveHighest(snapshotVersion));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(999)]
    [InlineData(int.MaxValue)]
    public void IsAboveHighest_IsTrueAboveTheCeiling(int snapshotVersion)
    {
        Assert.True(SnapshotVersions.IsAboveHighest(snapshotVersion));
    }
}
