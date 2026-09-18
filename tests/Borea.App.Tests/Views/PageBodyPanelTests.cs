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
    // the body stops at the large width, and the left margin and the gap to the panel stay equal
    [InlineData(1599, 127.5, 940)]
    [InlineData(1600, 128, 940)]
    [InlineData(2560, 608, 940)]
    public void Place_WithSidePanel_KeepsTheMarginBeforeThePanel(double window, double left, double width)
    {
        var (bodyLeft, bodyWidth) = PageBodyPanel.Place(window - PageBodyPanel.NavigationRailWidth, hasSidePanel: true);
        var panelLeft = window - PageBodyPanel.NavigationRailWidth - PageBodyPanel.SidePanelInset - PageBodyPanel.SidePanelWidth;

        Assert.Equal((left, width), (bodyLeft, bodyWidth));
        Assert.Equal(bodyLeft, panelLeft - bodyLeft - bodyWidth);
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
}
