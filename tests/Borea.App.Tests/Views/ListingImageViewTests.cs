using Avalonia;
using Borea.App.Views;

namespace Borea.App.Tests.Views;

public sealed class ListingImageViewTests
{
    [Theory]
    [InlineData(512, 512, 100, 100, 0, 0, 100, 100)]
    [InlineData(512, 512, 200, 100, 50, 0, 100, 100)]
    [InlineData(512, 512, 171, 200, 0, 14.5, 171, 171)]
    [InlineData(1600, 900, 160, 160, 0, 35, 160, 90)]
    public void Fit_ScalesWithoutStretchingOrCropping(double imageWidth, double imageHeight, double slotWidth, double slotHeight, double x, double y, double width, double height)
    {
        Assert.Equal(new Rect(x, y, width, height), ListingImageView.Fit(new Size(imageWidth, imageHeight), new Size(slotWidth, slotHeight)));
    }
}
