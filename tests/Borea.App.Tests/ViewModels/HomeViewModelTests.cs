using Borea.App.ViewModels;
using Borea.Core.Instances;

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
        Assert.Equal(harness.Localization.FormatTimeAgoShort(DateTimeOffset.UtcNow - viewModel.RecentItems[0].UpdatedAt), viewModel.RecentItems[0].UpdatedText);
        Assert.Equal($"Updated on {MainViewModel.DateText(viewModel.RecentItems[0].UpdatedAt)}", viewModel.RecentItems[0].UpdatedDateText);
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

    [Fact]
    public async Task SwitchOffOnHome_KeepsTheInstanceUntilTheNextVisit()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        await viewModel.Instances.Single().ActivateCommand.ExecuteAsync(null);
        Assert.Same(viewModel.ActiveInstance, viewModel.HomeInstance);

        await viewModel.HomeInstance!.ToggleActiveCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ActiveInstance);
        Assert.Equal("Alpha", viewModel.HomeInstance?.Name);
        Assert.False(viewModel.HomeInstance!.IsActive);

        await viewModel.HomeInstance.ToggleActiveCommand.ExecuteAsync(null);
        Assert.Same(viewModel.ActiveInstance, viewModel.HomeInstance);

        await viewModel.HomeInstance!.ToggleActiveCommand.ExecuteAsync(null);
        viewModel.SetMainWindowLibrary();
        viewModel.SetMainWindowHome();

        Assert.Null(viewModel.HomeInstance);
        Assert.False(viewModel.HasHomeInstance);
    }

    [Fact]
    public async Task InstanceActivatedElsewhere_ShowsOnHome()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        viewModel.SetMainWindowLibrary();

        await viewModel.Instances.Single().ActivateCommand.ExecuteAsync(null);
        viewModel.SetMainWindowHome();

        Assert.Same(viewModel.ActiveInstance, viewModel.HomeInstance);
        Assert.Equal("Alpha", viewModel.HomeInstance?.Name);
    }
}
