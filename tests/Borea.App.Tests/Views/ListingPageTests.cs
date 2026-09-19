using Avalonia.Controls;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views.Pages;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class ListingPageTests
{
    [Fact]
    public async Task StartStep_ShowsBothWaysIn()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await harness.ViewModel.OpenListingAsync();

        var texts = await RenderAsync(harness);

        Assert.Contains(harness.Localization.ListingNewTitle, texts);
        Assert.Contains(harness.Localization.ListingChangeTitle, texts);
        Assert.DoesNotContain(harness.Localization.ListingSteps, texts);
    }

    [Fact]
    public async Task FormStep_ShowsTheFieldsTheRowsTheChecksAndTheFile()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);
        editor.AddIconCommand.Execute(null);
        editor.AddDescriptionImageCommand.Execute(null);
        editor.AddDependencyCommand.Execute(null);

        var texts = await RenderAsync(harness);

        var localization = harness.Localization;
        Assert.Contains(localization.ListingAbout, texts);
        Assert.Contains(localization.ListingImageUrlHint, texts);
        Assert.Contains(localization.ListingImageNotMeasured, texts);
        Assert.Contains(localization.ListingPreview, texts);
        Assert.Contains(localization.ListingSteps, texts);
        Assert.Contains(localization.ListingDependencies, texts);
        Assert.Contains(editor.MissingText, texts);
        Assert.DoesNotContain(localization.ListingErrorsHeading, texts);
        Assert.Contains(localization.ListingOpenPullRequest, texts);
        Assert.Contains(localization.ListingNewPullRequestText, texts);
        Assert.DoesNotContain(localization.ListingEditPullRequestText, texts);
    }

    private static async Task<List<string?>> RenderAsync(ViewModelHarness harness)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await HeadlessApp.Session.Dispatch(() =>
        {
            var page = new ListingPage { DataContext = harness.ViewModel };
            var window = new Window { Width = 1280, Height = 832, Content = page, DataContext = harness.ViewModel };
            window.Show();
            window.UpdateLayout();
            var texts = page.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
            window.Close();
            return texts;
        }, timeout.Token);
    }
}
