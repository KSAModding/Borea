using Borea.Preview.Rendering;

namespace Borea.Preview.Tests.Rendering;

public sealed class DragScaleControllerTests
{
    /// <summary>Each pair in <paramref name="frames"/> is the scale of one drag frame and its time in milliseconds.</summary>
    [Theory]
    [InlineData(0.5, new[] { 0.5, 61.0 })]
    [InlineData(0.25, new[] { 0.5, 61.0, 0.5, 61.0 })]
    [InlineData(0.5, new[] { 0.5, 61.0, 0.5, 20.0, 0.5, 61.0 })]
    [InlineData(0.5, new[] { 0.5, 70.0, 0.5, 70.0, 0.25, 16.0 })]
    [InlineData(0.25, new[] { 0.5, 70.0, 0.5, 70.0, 0.25, 40.0 })]
    [InlineData(0.5, new[] { 0.5, 70.0, 0.5, 70.0, 0.25, 63.0 })]
    [InlineData(0.5, new[] { 0.5, 70.0, 0.5, 70.0, 0.25, 63.0, 0.5, 70.0, 0.5, 70.0 })]
    [InlineData(0.25, new[] { 0.5, 61.0, 0.5, double.NaN, 0.5, 61.0 })]
    public void Record_SwitchesAfterTwoSlowFrames_AndGoesBackWhenTheSlowScaleHasRoomOrDoesNotHelp(double expected, double[] frames)
    {
        // At 0.25, 40 ms saves more than 30 percent of 70 ms and stays, and 63 ms saves less, so 0.5 stays for the drag.
        // A time that is not finite is not a frame, so it does not break a run of slow frames.
        var controller = new DragScaleController(new RenderOptions());
        for (var i = 0; i < frames.Length; i += 2)
        {
            controller.Record(frames[i], frames[i + 1]);
        }

        Assert.Equal(expected, controller.Scale);
    }

    [Fact]
    public void Reset_LetsANewDragUseTheSlowScaleAgain()
    {
        var controller = new DragScaleController(new RenderOptions());
        foreach (var (scale, ms) in new[] { (0.5, 70.0), (0.5, 70.0), (0.25, 63.0) })
        {
            controller.Record(scale, ms);
        }

        controller.Reset();
        controller.Record(0.5, 70.0);
        controller.Record(0.5, 70.0);

        Assert.Equal(0.25, controller.Scale);
    }
}
