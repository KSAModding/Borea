using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Borea.App.Views;

namespace Borea.App.Tests.Views;

public sealed class RoundedClipTests
{
    [Theory]
    // a table: the child fills the inside of the line
    [InlineData(400, 300, 1, 1, 1, 16, 0, 0, 398, 298, 15.5)]
    // the modal: a thicker line leaves a smaller radius
    [InlineData(561, 200, 2, 2, 2, 16, 0, 0, 557, 196, 15)]
    // a line thicker than twice the radius leaves a square corner
    [InlineData(100, 100, 10, 10, 10, 4, 0, 0, 80, 80, 0)]
    // a border with padding: the clip stays on the line, outside the child
    [InlineData(200, 200, 1, 21, 11, 24, -20, -10, 198, 198, 23.5)]
    public void InnerEdge_IsTheInsideOfTheLineInTheCoordinatesOfTheChild(double width, double height, double thickness, double childX, double childY, double radius, double x, double y, double clipWidth, double clipHeight, double clipRadius)
    {
        var child = new Rect(childX, childY, 10, 10);

        var edge = RoundedClip.InnerEdge(new Size(width, height), child, new Thickness(thickness), new CornerRadius(radius));

        Assert.Equal((new Rect(x, y, clipWidth, clipHeight), clipRadius, clipRadius), edge);
    }

    [Fact]
    public async Task IsEnabled_ClipsTheChildUntilItIsReplacedOrDisabled()
    {
        await using var session = HeadlessApp.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var clips = await session.Dispatch(() =>
        {
            var first = new Border();
            var second = new Border();
            var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Child = first };
            RoundedClip.SetIsEnabled(border, true);
            var window = new Window { Width = 100, Height = 100, Content = border };
            window.Show();
            border.UpdateLayout();
            var enabled = Clip(first);

            border.Child = second;
            border.UpdateLayout();
            var replaced = Clip(first);
            var replacement = Clip(second);

            border.Width = 60;
            border.UpdateLayout();
            var resized = Clip(second);

            RoundedClip.SetIsEnabled(border, false);
            return (Enabled: enabled, Replaced: replaced, Replacement: replacement, Resized: resized, Disabled: Clip(second));
        }, timeout.Token);

        Assert.Equal((new Rect(0, 0, 98, 98), 15.5, 15.5), clips.Enabled);
        Assert.Null(clips.Replaced);
        Assert.Equal((new Rect(0, 0, 98, 98), 15.5, 15.5), clips.Replacement);
        Assert.Equal((new Rect(0, 0, 58, 98), 15.5, 15.5), clips.Resized);
        Assert.Null(clips.Disabled);
    }

    private static (Rect Rect, double RadiusX, double RadiusY)? Clip(Control child) =>
        child.Clip is RectangleGeometry clip ? (clip.Rect, clip.RadiusX, clip.RadiusY) : null;
}
