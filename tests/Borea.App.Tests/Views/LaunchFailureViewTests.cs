using Avalonia.Controls;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;
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

    /// <summary>The texts that show, which includes the text of each shown button.</summary>
    private static List<string?> Texts(Control root)
        => root.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
}
