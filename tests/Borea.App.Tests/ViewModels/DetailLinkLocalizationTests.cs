namespace Borea.App.Tests.ViewModels;

public sealed class DetailLinkLocalizationTests
{
    [Fact]
    public async Task SwitchingLanguage_RefreshesOpenDetailLinksWithoutChangingTheirTargets()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: PackViewModelTests.WithPacks(
            PackViewModelTests.Pack("starter-pack", "Starter Pack",
                PackViewModelTests.Version("1.0.0", PackViewModelTests.Pin("MeasureTools", "1.1.9")))));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        await viewModel.DiscoverPacks.Single().OpenCommand.ExecuteAsync(null);
        var contentTargets = viewModel.ContentLinks.Select(link => (link.Key, link.Url)).ToArray();
        var packTargets = viewModel.PackLinks.Select(link => (link.Key, link.Url)).ToArray();

        foreach (var culture in new[] { "zh-Hans", "en" })
        {
            Assert.True(harness.Localization.TrySetCulture(culture));

            Assert.Equal(culture == "zh-Hans" ? "论坛" : "Forum",
                viewModel.ContentLinks.Single(link => link.Key == "forums").Label);
            Assert.Equal(culture == "zh-Hans" ? "论坛" : "Forum",
                viewModel.PackLinks.Single(link => link.Key == "forums").Label);
            Assert.Equal(contentTargets, viewModel.ContentLinks.Select(link => (link.Key, link.Url)));
            Assert.Equal(packTargets, viewModel.PackLinks.Select(link => (link.Key, link.Url)));
            Assert.Contains(viewModel.ContentLinks, link => link.Label == "SpaceDock");
        }
    }
}
