using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;
using Borea.Core.Launch;
using Borea.Core.Mods;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class LaunchFailureViewTests
{
    [Fact]
    public async Task CrashedStart_ThePageKeepsOneStatusLine_AndTheModalHasTheWayOut()
    {
        using var harness = await LaunchFailureTests.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.PlayCommand.ExecuteAsync(null);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var shown = await HeadlessApp.Session.Dispatch(() =>
        {
            var page = new InstancePage { DataContext = viewModel };
            var modal = new LaunchFailureModal { DataContext = viewModel };
            var window = new Window { Width = 1280, Height = 832, Content = new Panel { Children = { page, modal } } };
            window.Show();
            window.UpdateLayout();
            return (Page: Texts(page), Modal: Texts(modal));
        }, timeout.Token);

        var localization = harness.Localization;
        Assert.Single(shown.Page, text => text == viewModel.LaunchMessage);
        Assert.Contains(localization.LaunchShowDetails, shown.Page);
        Assert.DoesNotContain(viewModel.DisableBlamedModText, shown.Page);
        Assert.DoesNotContain(localization.LaunchOpenLog, shown.Page);
        Assert.Contains(viewModel.LaunchMessage, shown.Modal);
        Assert.Contains(viewModel.DisableBlamedModText, shown.Modal);
        Assert.Contains(localization.LaunchShowDetails, shown.Modal);
        Assert.Contains(localization.LaunchOpenLog, shown.Modal);
    }

    [Fact]
    public async Task RuntimePrompt_ShowsTheVersionInTheTitle_AndTheThreeStepsInOrder()
    {
        using var harness = await LaunchFailureTests.CreateAsync();
        var viewModel = harness.ViewModel;
        var runtime = DotnetRuntimeNeed.Parse("""{ "runtimeOptions": { "framework": { "name": "Microsoft.NETCore.App", "version": "10.0.0" } } }""")!;
        viewModel.PromptedRuntime = new RuntimePrompt("StarMap", runtime, "/Applications/KSA.app");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var shown = await HeadlessApp.Session.Dispatch(() =>
        {
            var modal = new RuntimePromptModal { DataContext = viewModel };
            var window = new Window { Width = 1280, Height = 832, Content = modal };
            window.Show();
            window.UpdateLayout();
            // top to bottom, then left to right, as a reader sees them
            return modal.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible)
                .Select(text => (text.Text, Where: text.TranslatePoint(default, modal)!.Value))
                .OrderBy(text => Math.Round(text.Where.Y)).ThenBy(text => text.Where.X)
                .Select(text => text.Text).ToList();
        }, timeout.Token);

        var localization = harness.Localization;
        Assert.Equal(localization.FormatLaunchRuntimeTitle("10.0"), shown[0]);
        Assert.Contains(localization.FormatLaunchRuntimeMissing("StarMap", "10.0", "/Applications/KSA.app"), shown);
        var steps = new[] { "1.", localization.FormatLaunchRuntimeDownload("10.0"), "2.", localization.LaunchRuntimeInstallStep, "3.", localization.LaunchRuntimeCheckStep };
        Assert.Equal(steps, shown.Where(steps.Contains));
    }

    [Fact]
    public async Task NotGameFolder_TheStatusLineHasTheWayToTheGameTab()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.LaunchMessage = harness.Localization.FormatLaunchNotGameFolder("~/Documents/ahwoo");
        viewModel.HasLaunchFailed = true;
        viewModel.IsLaunchNotGameFolder = true;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var shown = await HeadlessApp.Session.Dispatch(() =>
        {
            var page = new InstancePage { DataContext = viewModel };
            var window = new Window { Width = 1280, Height = 832, Content = page };
            window.Show();
            window.UpdateLayout();
            return Texts(page);
        }, timeout.Token);

        Assert.Single(shown, text => text == viewModel.LaunchMessage);
        Assert.Single(shown, text => text == harness.Localization.SetupBannerAction);
        Assert.DoesNotContain(harness.Localization.LaunchShowDetails, shown);
    }

    [Fact]
    public async Task LoaderFoundNoKsaDll_TheStatusLineAndTheModalLeadToTheGameTab()
    {
        using var harness = await LaunchFailureTests.CreateGameNotFoundAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.PlayCommand.ExecuteAsync(null);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var localization = harness.Localization;

        var shown = await HeadlessApp.Session.Dispatch(() =>
        {
            var page = new InstancePage { DataContext = viewModel };
            var modal = new LaunchFailureModal { DataContext = viewModel };
            var window = new Window { Width = 1280, Height = 832, Content = new Panel { Children = { page, modal } } };
            window.Show();
            window.UpdateLayout();
            // the two links of the status line, each with the place where it shows
            var links = page.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible
                    && (text.Text == localization.LaunchShowDetails || text.Text == localization.SetupBannerAction))
                .Select(text => new Rect(text.TranslatePoint(default, page)!.Value, text.Bounds.Size)).ToList();
            return (Links: links, Modal: Texts(modal));
        }, timeout.Token);

        Assert.Equal(2, shown.Links.Count);
        Assert.False(shown.Links[0].Intersects(shown.Links[1]));
        Assert.Contains(localization.SetupBannerAction, shown.Modal);
        Assert.DoesNotContain(localization.ModalClose, shown.Modal);
    }

    /// <summary>The texts that show, which includes the text of each shown button.</summary>
    private static List<string?> Texts(Control root)
        => root.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
}
