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
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(999, true)]
    [InlineData(int.MaxValue, true)]
    public void IsAboveHighest_IsTrueOnlyAboveTheCeiling(int snapshotVersion, bool expected)
    {
        Assert.Equal(expected, SnapshotVersions.IsAboveHighest(snapshotVersion));
    }
}
