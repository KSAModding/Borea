using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Borea.App.Views;

namespace Borea.App.Tests.Views;

/// <summary>
/// The width properties that <see cref="PageBodyPanel"/> gives the controls in it, which the styles of a page read
/// instead of a container query.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class PageBodyWidthTests
{
    [Theory]
    [InlineData(811, false, false, false, false)]
    [InlineData(810, true, false, false, false)]
    [InlineData(721, true, false, false, false)]
    [InlineData(720, true, true, false, false)]
    [InlineData(541, true, true, false, false)]
    [InlineData(540, true, true, true, false)]
    [InlineData(521, true, true, true, false)]
    [InlineData(520, true, true, true, true)]
    public async Task ControlInThePanel_KnowsHowWideTheBodyIs(double body, bool medium, bool narrow, bool compact, bool small)
    {
        var widths = await MeasureAsync(body);

        Assert.Equal((medium, narrow, compact, small), widths);
    }

    [Theory]
    [InlineData(1101, false, false)]
    [InlineData(1100, true, false)]
    [InlineData(801, true, false)]
    [InlineData(800, true, true)]
    public async Task ControlInThePanel_KnowsWhenTheStatisticsTablesStack(double body, bool limited, bool tight)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var widths = await HeadlessApp.Session.Dispatch(() =>
        {
            var inner = new TextBlock();
            var panel = new PageBodyPanel { Children = { new StackPanel { Children = { inner } } } };
            panel.Measure(new Size(body + 2 * PageBodyPanel.SmallMargin, 600));
            return (PageBodyPanel.GetIsLimited(inner), PageBodyPanel.GetIsTight(inner));
        }, timeout.Token);

        Assert.Equal((limited, tight), widths);
    }

    [Fact]
    public async Task StewardPage_HoldsNoContainerQuery()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var found = await HeadlessApp.Session.Dispatch(() =>
        {
            var page = new Borea.App.Views.Pages.StewardPage();
            return page.Styles.Any(style => style.GetType().Name == "ContainerQuery");
        }, timeout.Token);

        Assert.False(found);
    }

    [Fact]
    public async Task ControlInThePanel_FollowsABodyThatGrowsAgain()
    {
        var widths = await MeasureAsync(400, 1000);

        Assert.Equal((false, false, false, false), widths);
    }

    [Fact]
    public async Task ControlOutsideAPanel_IsOnAWidePage()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var widths = await HeadlessApp.Session.Dispatch(() => Read(new TextBlock()), timeout.Token);

        Assert.Equal((false, false, false, false), widths);
    }

    /// <summary>Measures a panel without a side panel at each body width in turn, and reads the properties of a control two levels below it.</summary>
    private static async Task<(bool Medium, bool Narrow, bool Compact, bool Small)> MeasureAsync(params double[] bodies)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await HeadlessApp.Session.Dispatch(() =>
        {
            var inner = new TextBlock();
            var panel = new PageBodyPanel { Children = { new StackPanel { Children = { inner } } } };

            // a page this narrow has the small margin on both sides of its body
            foreach (var body in bodies)
                panel.Measure(new Size(body + 2 * PageBodyPanel.SmallMargin, 600));

            return Read(inner);
        }, timeout.Token);
    }

    private static (bool Medium, bool Narrow, bool Compact, bool Small) Read(Control control) => (
        PageBodyPanel.GetIsMedium(control),
        PageBodyPanel.GetIsNarrow(control),
        PageBodyPanel.GetIsCompact(control),
        PageBodyPanel.GetIsSmall(control));
}
