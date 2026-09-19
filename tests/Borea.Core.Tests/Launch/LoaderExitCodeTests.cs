using Borea.Core.Launch;

namespace Borea.Core.Tests.Launch;

public sealed class LoaderExitCodeTests
{
    [Theory]
    [InlineData(-1073741819, "-1073741819 (0xC0000005, access violation)")]
    [InlineData(-1073741571, "-1073741571 (0xC00000FD, stack overflow)")]
    [InlineData(-532462766, "-532462766 (0xE0434352, .NET exception)")]
    [InlineData(-2146233082, "-2146233082 (0x80131506, CLR internal error)")]
    [InlineData(3, "3 (0x00000003)")]
    public void Describe_OnWindows_ShowsTheHexValueAndTheNameOfACommonCode(int exitCode, string expected)
    {
        Assert.Equal(expected, LoaderExitCode.Describe(exitCode, windows: true));
    }

    [Fact]
    public void Describe_ElsewhereThanWindows_IsTheNumber()
    {
        Assert.Equal("-1073741819", LoaderExitCode.Describe(-1073741819, windows: false));
    }
}
