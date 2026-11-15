using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;
using Borea.Core.Mods;
using ContentPage = Borea.App.Views.Pages.ContentPage;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class ScrollPolishTests
{
    [Fact]
    public async Task InstanceHeader_TurnsCompactWhenTheBodyScrollsDown_AndFullAtTheTop()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => ViewModelHarness.WithCopies(json, "MeasureTools", 30));
        for (var number = 1; number <= 30; number++)
            await InstalledContent.AddAsync(harness, $"MeasureTools{number}", activate: true, ownership: ModInstallOwnership.Borea);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.Instances.Single().OpenCommand.ExecuteAsync(null);

        var seen = await HeadlessApp.RunAsync(harness, async () =>
        {
            var page = new InstancePage();
            var window = new Window { Width = 1000, Height = 760, DataContext = harness.ViewModel, Content = page };
            window.Show();
            try
            {
                page.UpdateLayout();
                var root = page.GetVisualDescendants().OfType<Grid>().First(grid => grid.Name == "InstanceRoot");
                var body = page.GetVisualDescendants().OfType<ScrollViewer>().First(scroller => scroller.Name == "BodyScroller");
                var play = page.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("cta"));
                var tabs = page.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("full-only") && border.CornerRadius == new CornerRadius(100));
                var fullHeight = root.Children[0].Bounds.Height;
                var atTop = (root.Classes.Contains("compact"), tabs.IsEffectivelyVisible);

                body.Offset = new Vector(0, 400);
                await HeadlessApp.FramesAsync(6);
                var down = (root.Classes.Contains("compact"), tabs.IsEffectivelyVisible, play.IsEffectivelyVisible, Height: root.Children[0].Bounds.Height);

                body.Offset = new Vector(0, 0);
                await HeadlessApp.FramesAsync(6);
                var back = (root.Classes.Contains("compact"), tabs.IsEffectivelyVisible, root.Children[0].Bounds.Height);
                return (fullHeight, atTop, down, back);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal((false, true), seen.atTop);
        Assert.True(seen.down.Item1);
        Assert.False(seen.down.Item2);
        Assert.True(seen.down.Item3);
        Assert.InRange(seen.down.Height, 40, 80);
        Assert.Equal((false, true), (seen.back.Item1, seen.back.Item2));
        Assert.Equal(seen.fullHeight, seen.back.Item3, 0.5);
    }

    [Theory]
    [InlineData(typeof(InstancePage))]
    [InlineData(typeof(LibraryPage))]
    [InlineData(typeof(ContentPage))]
    [InlineData(typeof(PackPage))]
    [InlineData(typeof(ListingPage))]
    public async Task EveryScrollingPage_HasABackToTopButtonOnItsBody(Type pageType)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var seen = await HeadlessApp.RunAsync(harness, () =>
        {
            var page = (Control)Activator.CreateInstance(pageType)!;
            var window = new Window { Width = 1000, Height = 760, DataContext = harness.ViewModel, Content = page };
            window.Show();
            try
            {
                page.UpdateLayout();
                var button = page.GetVisualDescendants().OfType<ScrollToTopButton>().Single();
                return Task.FromResult((button.Target is not null, button.Target?.Name, button.GetValue(Avalonia.Automation.AutomationProperties.NameProperty)));
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(seen.Item1);
        Assert.NotNull(seen.Name);
        Assert.Equal(harness.Localization.DiscoverBackToTop, seen.Item3);
    }
}
