using Borea.App.Views;

namespace Borea.App.Tests.Views;

public sealed class PageBodyPanelTests
{
    [Theory]
    // wide: the body sits on the center line, where Home and Library have theirs
    [InlineData(1840, 538, 764)]
    // the default window: the body moves left and keeps the gap of the design to the panel
    [InlineData(1200, 56, 764)]
    // narrower: the gap closes before the body gets narrower
    [InlineData(1100, 0, 764)]
    // the smallest window: the body takes what the panel leaves
    [InlineData(780, 0, 456)]
    [InlineData(200, 0, 0)]
    public void Place_KeepsTheBodyCenteredUntilThePanelNeedsTheRoom(double available, double left, double width)
    {
        Assert.Equal((left, width), PageBodyPanel.Place(available));
    }
}
