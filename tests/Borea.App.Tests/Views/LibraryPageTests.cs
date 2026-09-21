using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class LibraryPageTests
{
    [Fact]
    public async Task GameSetupBanner_StartsBelowTheHeading()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        Assert.True(harness.ViewModel.NeedsGameSetup);

        var (heading, banner) = await RenderAsync(harness);

        Assert.True(banner > heading, $"the banner starts at {banner} and the heading at {heading}");
    }

    private static async Task<(double Heading, double Banner)> RenderAsync(ViewModelHarness harness)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await HeadlessApp.Session.Dispatch(() =>
        {
            var page = new LibraryPage { DataContext = harness.ViewModel };
            var window = new Window { Width = 1280, Height = 832, Content = page, DataContext = harness.ViewModel };
            window.Show();
            window.UpdateLayout();
            var heading = page.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == harness.Localization.PageLibraryHeading);
            var banner = page.GetVisualDescendants().OfType<Banner>().Single(control => control.IsEffectivelyVisible);
            var tops = (Top(page, heading), Top(page, banner));
            window.Close();
            return tops;
        }, timeout.Token);
    }

    private static double Top(Visual page, Visual control) => control.TranslatePoint(new Point(0, 0), page)!.Value.Y;
}
