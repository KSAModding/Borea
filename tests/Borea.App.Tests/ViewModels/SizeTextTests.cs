using System.Globalization;
using Borea.App.ViewModels;

namespace Borea.App.Tests.ViewModels;

/// <summary>The formatting follows the culture, so the expected texts are checked in the invariant one.</summary>
public sealed class SizeTextTests : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public SizeTextTests() => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1000, "1k")]
    [InlineData(1234, "1.2k")]
    [InlineData(1999, "1.9k")]
    [InlineData(15300, "15k")]
    [InlineData(999999, "999k")]
    [InlineData(1200000, "1.2M")]
    [InlineData(25000000, "25M")]
    public void CompactCount_DropsPrecisionOnLargeNumbers(long count, string expected)
    {
        Assert.Equal(expected, MainViewModel.CompactCount(count));
    }

    [Theory]
    [InlineData(999, "999 B")]
    [InlineData(1000, "1.0 KB")]
    [InlineData(38_000_000, "38.0 MB")]
    [InlineData(1_864_276_755, "1.9 GB")]
    public void SizeText_UsesDecimalUnits(long bytes, string expected)
    {
        Assert.Equal(expected, MainViewModel.SizeText(bytes));
    }
}
