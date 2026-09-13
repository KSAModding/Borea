namespace Borea.App.Tests.ViewModels;

public sealed class HomeViewModelTests
{
    [Fact]
    public async Task Load_ShowsTheIndexModsWithTheNewestReleaseFirst()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        Assert.True(viewModel.HasRecentItems);
        Assert.Equal(["MeasureTools", "AdvancedFlightComputer", "KSArmory"], viewModel.RecentItems.Select(item => item.ModId));
        Assert.DoesNotContain(viewModel.RecentItems, item => item.ModId == "StarMap");
        Assert.False(string.IsNullOrWhiteSpace(viewModel.RecentItems[0].UpdatedText));
    }

    [Fact]
    public async Task OpenCard_ShowsTheContentPageWithTheDiscoverRow()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        await viewModel.RecentItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowContent);
        Assert.Equal("AdvancedFlightComputer", viewModel.SelectedContent?.ModId);
        Assert.Contains(viewModel.DiscoverItems, item => ReferenceEquals(item, viewModel.SelectedContent));
    }
}
