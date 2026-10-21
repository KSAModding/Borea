using Borea.Preview.Rendering;

namespace Borea.Preview.Tests.Rendering;

public sealed class RenderOptionsTests
{
    private static readonly RenderOptions Defaults = new();

    [Theory]
    [InlineData(0.5, 61.0, 0.25)]
    [InlineData(0.5, 59.0, 0.5)]
    [InlineData(0.25, 61.0, 0.25)]
    [InlineData(0.25, 14.0, 0.5)]
    [InlineData(0.25, 20.0, 0.25)]
    [InlineData(0.25, double.NaN, 0.25)]
    public void NextDragScale_GoesToTheSlowScaleAfterASlowFrame_AndBackOnlyWithRoomToSpare(double lastScale, double lastFrameMs, double expected)
    {
        // At the slow scale, 20 ms would be 80 ms with the four times larger pixel count of 0.5, so it stays slow.
        Assert.Equal(expected, Defaults.NextDragScale(lastScale, lastFrameMs));
    }
}
