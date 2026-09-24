using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

public sealed class PageBackTests
{
    [Fact]
    public async Task ModOpenedFromDiscover_GoesBackToDiscoverWithItsFilters()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.SetMainWindowDiscover();
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.SearchText = "Flight";
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);
        Assert.True(viewModel.CurrentWindowContent);
        Assert.False(viewModel.IsContentFromHome);
        Assert.True(viewModel.IsDiscoverSection);

        await viewModel.GoBackCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowDiscover);
        Assert.False(viewModel.CurrentWindowContent);
        Assert.Equal("Flight", viewModel.SearchText);
        Assert.Contains(viewModel.DiscoverItems, item => item.ModId == "AdvancedFlightComputer");
    }

    [Fact]
    public async Task ModOpenedFromHome_GoesBackToHome()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.RecentItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);
        Assert.True(viewModel.CurrentWindowContent);

        await viewModel.GoBackCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowHome);
        Assert.False(viewModel.CurrentWindowContent);
    }

    [Fact]
    public async Task ModOpenedFromHome_NamesHomeInTheBreadcrumbAndTheRail()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        await viewModel.RecentItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsContentFromHome);
        Assert.True(viewModel.IsHomeSection);
        Assert.False(viewModel.IsDiscoverSection);

        await viewModel.GoBackCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsContentFromHome);
        Assert.True(viewModel.IsHomeSection);
    }

    [Fact]
    public async Task ModOpenedFromALink_GoesBackToDiscover()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        await viewModel.OpenLinkAsync("borea://mod/MeasureTools");
        Assert.True(viewModel.CurrentWindowContent);

        await viewModel.GoBackCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowDiscover);
        Assert.False(viewModel.CurrentWindowContent);
    }

    [Fact]
    public async Task InstanceOpenedFromTheLibrary_GoesBackToTheLibrary()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        viewModel.SetMainWindowLibrary();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        Assert.True(viewModel.CurrentWindowInstance);

        await viewModel.GoBackCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowLibrary);
        Assert.False(viewModel.CurrentWindowInstance);
    }

    [Fact]
    public async Task ModOpenedFromAnInstance_GoesBackToThatInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        var instance = viewModel.ActiveInstance!;
        await instance.OpenCommand.ExecuteAsync(null);
        await viewModel.ContentGroups.SelectMany(group => group.Items).Single().OpenCommand.ExecuteAsync(null);
        Assert.True(viewModel.CurrentWindowContent);

        await viewModel.GoBackCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowInstance);
        Assert.Equal(instance.InstanceId, viewModel.SelectedInstance?.InstanceId);
    }

    [Fact]
    public async Task ListingPage_GoesBackToDiscoverAfterAModWasOpenedFromHome()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.RecentItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);
        await viewModel.GoBackCommand.ExecuteAsync(null);
        viewModel.SetMainWindowDiscover();
        await viewModel.OpenListingAsync();
        Assert.True(viewModel.CurrentWindowListing);

        await viewModel.GoBackCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowDiscover);
        Assert.False(viewModel.CurrentWindowListing);
    }

    [Fact]
    public async Task ModOpenedFromAnotherContentPage_GoesBackToDiscover()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.RecentItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);
        await viewModel.OpenContentCommand.ExecuteAsync(viewModel.DiscoverItems.Single(item => item.ModId == "MeasureTools"));
        Assert.False(viewModel.IsContentFromHome);
        Assert.True(viewModel.IsDiscoverSection);

        await viewModel.GoBackCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowDiscover);
    }

    [Fact]
    public async Task Forward_AfterBackFromAModOpenedFromAnInstance_KeepsItsWayBackToThatInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        var instance = viewModel.ActiveInstance!;
        await instance.OpenCommand.ExecuteAsync(null);
        await viewModel.ContentGroups.SelectMany(group => group.Items).Single().OpenCommand.ExecuteAsync(null);
        var mod = viewModel.SelectedContent;
        await viewModel.GoBackCommand.ExecuteAsync(null);

        await viewModel.GoForwardCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowContent);
        Assert.Same(mod, viewModel.SelectedContent);
        Assert.True(viewModel.IsContentFromInstance);

        await viewModel.GoBackCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowInstance);
        Assert.Equal(instance.InstanceId, viewModel.SelectedInstance?.InstanceId);
    }

    [Fact]
    public async Task Forward_AfterTheIndexChangedBehindTheBackStep_OpensTheNewRow()
    {
        var renamed = false;
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => renamed
            ? json.Replace("\"name\": \"Advanced Flight Computer\"", "\"name\": \"Flight Computer Deluxe\"", StringComparison.Ordinal)
            : json);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);
        await viewModel.GoBackCommand.ExecuteAsync(null);
        renamed = true;
        await viewModel.RefreshContentIndexCommand.ExecuteAsync(null);

        await viewModel.GoForwardCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowContent);
        Assert.Same(viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer"), viewModel.SelectedContent);
        Assert.Equal("Flight Computer Deluxe", viewModel.SelectedContent?.Name);
    }

    [Fact]
    public async Task Forward_AfterBackFromAnInstance_ReturnsToThatInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        viewModel.SetMainWindowLibrary();
        await viewModel.Instances.Single().OpenCommand.ExecuteAsync(null);
        await viewModel.GoBackCommand.ExecuteAsync(null);
        await viewModel.LoadAsync();

        await viewModel.GoForwardCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowInstance);
        Assert.Same(viewModel.Instances.Single(), viewModel.SelectedInstance);
    }

    [Fact]
    public async Task Forward_AfterAnotherPageWasOpened_StaysOnThatPage()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.RecentItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);
        await viewModel.GoBackCommand.ExecuteAsync(null);
        viewModel.SetMainWindowLibrary();
        viewModel.SetMainWindowHome();

        await viewModel.GoForwardCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowHome);
        Assert.False(viewModel.CurrentWindowContent);
    }

    [Fact]
    public async Task Forward_AfterBackFromTheListingPage_ReturnsToIt()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.SetMainWindowDiscover();
        await viewModel.OpenListingAsync();
        await viewModel.GoBackCommand.ExecuteAsync(null);

        await viewModel.GoForwardCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowListing);
        Assert.False(viewModel.CurrentWindowDiscover);
    }
}
