using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class ContentDependenciesViewTests
{
    [Fact]
    public async Task DependenciesTab_SitsBetweenDescriptionAndVersionsAndShowsTheLoaderAndTheGroups()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);
        viewModel.ShowContentDependenciesCommand.Execute(null);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (tabs, texts) = await HeadlessApp.Session.Dispatch(() =>
        {
            var page = new Borea.App.Views.Pages.ContentPage();
            var window = new Window { Width = 1200, Height = 800, DataContext = viewModel, Content = page };
            window.Show();
            try
            {
                page.UpdateLayout();
                var tabs = page.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.Classes.Contains("tab") && button.IsEffectivelyVisible)
                    .OrderBy(button => button.TranslatePoint(default, page)!.Value.X)
                    .Select(button => button.Content as string)
                    .ToList();
                var texts = page.GetVisualDescendants().OfType<TextBlock>()
                    .Where(text => text.IsEffectivelyVisible && text.Text is not null)
                    .Select(text => text.Text!)
                    .ToList();
                return Task.FromResult((tabs, texts));
            }
            finally
            {
                window.Close();
            }
        }, timeout.Token);

        Assert.Equal([harness.Localization.ContentTabDescription, harness.Localization.ContentTabDependencies, harness.Localization.ContentTabVersions], tabs);
        Assert.Contains("Dependencies of 0.7.5", texts);
        Assert.Contains("Needs StarMap 0.4.5 or newer", texts);
        Assert.Contains(harness.Localization.ContentDependencyOptional, texts);
        Assert.Contains(harness.Localization.ContentDependencyOptionalHint, texts);
        Assert.Contains("KittenExtensions", texts);
        Assert.Contains(harness.Localization.ContentDependencyNotInIndex, texts);
        Assert.DoesNotContain(harness.Localization.ContentNoDependencies, texts);
    }
}
