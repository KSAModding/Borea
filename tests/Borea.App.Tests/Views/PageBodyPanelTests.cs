using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Borea.App.Views;

namespace Borea.App.Tests.Views;

public sealed class PageBodyPanelTests
{
    [Theory]
    [InlineData(860, 24)]
    [InlineData(1000, 24)]
    [InlineData(1279, 24)]
    [InlineData(1280, 64)]
    [InlineData(1599, 64)]
    [InlineData(1600, 128)]
    [InlineData(2560, 128)]
    public void SideMargin_StepsWithTheWindowWidth(double window, double margin)
    {
        Assert.Equal(margin, PageBodyPanel.SideMargin(window - PageBodyPanel.NavigationRailWidth));
    }

    [Theory]
    [InlineData(860, 24, 732)]
    [InlineData(1000, 24, 872)]
    [InlineData(1279, 24, 1151)]
    [InlineData(1280, 64, 1072)]
    // the body stops at the large width, and the rest goes to both sides
    [InlineData(1599, 127.5, 1264)]
    [InlineData(1600, 128, 1264)]
    [InlineData(2560, 608, 1264)]
    public void Place_WithoutSidePanel_KeepsTheMarginOnBothSides(double window, double left, double width)
    {
        Assert.Equal((left, width), PageBodyPanel.Place(window - PageBodyPanel.NavigationRailWidth, hasSidePanel: false));
    }

    [Theory]
    [InlineData(860, 24, 408)]
    [InlineData(1000, 24, 548)]
    [InlineData(1279, 24, 827)]
    [InlineData(1280, 64, 748)]
    [InlineData(1599, 127.5, 1003.5)]
    [InlineData(1600, 128, 1004)]
    [InlineData(1920, 288, 1164)]
    // from here the whole body of a page without the panel fits next to it
    [InlineData(2200, 428, 1264)]
    [InlineData(2560, 608, 1264)]
    public void Place_WithSidePanel_KeepsTheBodyOfAPageWithoutIt_UntilThePanelNeedsTheRoom(double window, double left, double width)
    {
        var available = window - PageBodyPanel.NavigationRailWidth;
        var (bodyLeft, bodyWidth) = PageBodyPanel.Place(available, hasSidePanel: true);
        var (fullLeft, fullWidth) = PageBodyPanel.Place(available, hasSidePanel: false);
        var gap = available - PageBodyPanel.SidePanelInset - PageBodyPanel.SidePanelWidth - bodyLeft - bodyWidth;

        Assert.Equal((left, width), (bodyLeft, bodyWidth));
        Assert.Equal(fullLeft, bodyLeft);
        Assert.True(gap >= PageBodyPanel.SidePanelGap(available));
        Assert.True(bodyWidth == fullWidth || gap == PageBodyPanel.SidePanelGap(available));
    }

    [Theory]
    [InlineData(1000, 24)]
    [InlineData(1280, 64)]
    [InlineData(1600, 64)]
    [InlineData(2560, 64)]
    public void SidePanelGap_IsTheSideMarginUpToTheRegularOne(double window, double gap)
    {
        Assert.Equal(gap, PageBodyPanel.SidePanelGap(window - PageBodyPanel.NavigationRailWidth));
    }

    [Fact]
    public void Layout_StacksTheVisibleChildrenInTheBody()
    {
        var first = new Border { Height = 10 };
        var hidden = new Border { Height = 10, IsVisible = false };
        var last = new Border { Height = 10 };
        var panel = new PageBodyPanel { Children = { first, hidden, last } };

        panel.Measure(new Size(1200, double.PositiveInfinity));
        panel.Arrange(new Rect(panel.DesiredSize));

        Assert.Equal(PageBodyPanel.PageTopMargin + 20 + PageBodyPanel.PageBottomMargin, panel.DesiredSize.Height);
        Assert.Equal(new Rect(64, PageBodyPanel.PageTopMargin, 1072, 10), first.Bounds);
        Assert.Equal(new Rect(64, PageBodyPanel.PageTopMargin + 10, 1072, 10), last.Bounds);
    }

    [Theory]
    [InlineData(true, false, PageBodyPanel.PageTopMargin, 0)]
    [InlineData(false, true, 0, PageBodyPanel.PageBottomMargin)]
    public void Layout_FixedHeaderAndScrollingPart_KeepOnlyTheirOuterMargin(bool hasTopMargin, bool hasBottomMargin, double top, double bottom)
    {
        var child = new Border { Height = 10 };
        var panel = new PageBodyPanel { HasTopMargin = hasTopMargin, HasBottomMargin = hasBottomMargin, Children = { child } };

        panel.Measure(new Size(1200, double.PositiveInfinity));
        panel.Arrange(new Rect(panel.DesiredSize));

        Assert.Equal(top + 10 + bottom, panel.DesiredSize.Height);
        Assert.Equal(new Rect(64, top, 1072, 10), child.Bounds);
    }

    [Theory]
    [InlineData(480, 120)]
    [InlineData(800, 200)]
    [InlineData(1440, 200)]
    public void HeaderMessagesMaxHeight_LeavesALowPageRoomToScroll(double page, double maxHeight)
    {
        Assert.Equal(maxHeight, (double)PageBodyPanel.HeaderMessagesMaxHeight.Convert(page, typeof(double), null, CultureInfo.InvariantCulture)!);
    }
}
