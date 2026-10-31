using System.Text.Json.Nodes;
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

        var (tabs, texts) = await HeadlessApp.RunAsync(harness, () =>
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
        });

        Assert.Equal([harness.Localization.ContentTabDescription, harness.Localization.ContentTabDependencies, harness.Localization.ContentTabVersions], tabs);
        Assert.Contains("Dependencies of 0.7.5", texts);
        Assert.Contains("Needs StarMap 0.4.5 or newer", texts);
        Assert.Contains(harness.Localization.ContentDependencyOptional, texts);
        Assert.Contains(harness.Localization.ContentDependencyOptionalHint, texts);
        Assert.Contains("KittenExtensions", texts);
        Assert.Contains(harness.Localization.ContentDependencyNotInIndex, texts);
        Assert.DoesNotContain(harness.Localization.ContentNoDependencies, texts);
        Assert.DoesNotContain(harness.Localization.ContentDependencyRequiredBy, texts);
        Assert.DoesNotContain(harness.Localization.ContentDependencyConflicts, texts);
    }

    [Fact]
    public async Task DependenciesTab_ShowsTheConflictsOnceAndTheLineAndTheLinkOfRequiredBy()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json =>
        {
            var root = JsonNode.Parse(json)!;
            var listings = root["listings"]!.AsArray();
            listings.Single(node => (string?)node!["id"] == "KSArmory")!["releases"]![0]!["dependencies"] = JsonNode.Parse("""[{ "id": "MeasureTools", "kind": "conflict", "source": "authored" }]""");
            listings.Single(node => (string?)node!["id"] == "AdvancedFlightComputer")!["releases"]![0]!["dependencies"] = JsonNode.Parse("""[{ "id": "KSArmory", "kind": "required", "source": "authored" }]""");
            return root.ToJsonString();
        });
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == "KSArmory").OpenCommand.ExecuteAsync(null);
        viewModel.ShowContentDependenciesCommand.Execute(null);

        var texts = await HeadlessApp.RunAsync(harness, () =>
        {
            var page = new Borea.App.Views.Pages.ContentPage();
            var window = new Window { Width = 1200, Height = 800, DataContext = viewModel, Content = page };
            window.Show();
            try
            {
                page.UpdateLayout();
                return Task.FromResult(page.GetVisualDescendants().OfType<TextBlock>()
                    .Where(text => text.IsEffectivelyVisible && text.Text is not null)
                    .Select(text => text.Text!)
                    .ToList());
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Single(texts, text => text == harness.Localization.ContentDependencyConflicts);
        Assert.Contains(harness.Localization.ContentDependencyConflictsHint, texts);
        Assert.Contains("MeasureTools", texts);
        Assert.Contains(harness.Localization.ContentNoDependencies, texts);
        Assert.Contains(harness.Localization.ContentDependencyRequiredBy, texts);
        Assert.Contains(harness.Localization.ContentDependencyRequiredByNoneInstalled, texts);
        Assert.Contains("Show all 1", texts);
        Assert.DoesNotContain("Advanced Flight Computer", texts);
    }
}
