using System.Globalization;
using Borea.Core.Files;

namespace Borea.Core.Tests.Files;

public sealed class ByteSizeTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(999, "999 B")]
    [InlineData(1_500, "1.5 KB")]
    [InlineData(999_960, "1.0 MB")]
    [InlineData(999_960_000, "1.0 GB")]
    [InlineData(2_500_000_000_000, "2.5 TB")]
    [InlineData(2_500_000_000_000_000, "2500.0 TB")]
    public void Format_UsesTheLargestUnitBelowOneThousand(long bytes, string expected)
        => Assert.Equal(expected, ByteSize.Format(bytes, CultureInfo.InvariantCulture));

    [Fact]
    public void Format_UsesTheCultureDecimalSeparator()
        => Assert.Equal("1,5 KB", ByteSize.Format(1_500, CultureInfo.GetCultureInfo("de-DE")));
}
