using Avalonia;
using Avalonia.Controls;
using Borea.App.Views;

namespace Borea.App.Tests.Views;

public sealed class ShareBarPanelTests
{
    [Fact]
    public void Layout_MakesEachPartAsWideAsItsShare_WithAGapBetweenTwoParts()
    {
        var (first, second, panel) = Bar(0.75, 0.25);

        Arrange(panel, 202);

        Assert.Equal(new Rect(0, 0, 150, 6), first.Bounds);
        Assert.Equal(new Rect(152, 0, 50, 6), second.Bounds);
    }

    [Fact]
    public void Layout_GivesAPartWithNoShareNoRoomAndNoGap_AndKeepsATinyPartVisible()
    {
        var (first, second, panel) = Bar(0.001, 0);

        Arrange(panel, 200);

        Assert.Equal(new Rect(0, 0, 2, 6), first.Bounds);
        Assert.Equal(0, second.Bounds.Width);
    }

    private static (Border First, Border Second, ShareBarPanel Panel) Bar(double firstShare, double secondShare)
    {
        var first = new Border { Height = 6 };
        var second = new Border { Height = 6 };
        ShareBarPanel.SetShare(first, firstShare);
        ShareBarPanel.SetShare(second, secondShare);
        return (first, second, new ShareBarPanel { Children = { first, second } });
    }

    private static void Arrange(ShareBarPanel panel, double width)
    {
        panel.Measure(new Size(width, double.PositiveInfinity));
        panel.Arrange(new Rect(panel.DesiredSize));
    }
}
