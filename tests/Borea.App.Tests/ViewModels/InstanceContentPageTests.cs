using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

public sealed class InstanceContentPageTests
{
    [Fact]
    public async Task OwnedIndexedMod_OpensItsPageAndLeadsBackToTheInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        var instance = viewModel.ActiveInstance!;
        await instance.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.SelectMany(group => group.Items).Single();
        Assert.True(row.CanOpen);
        Assert.Null(row.NoPageText);

        await row.OpenCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowContent);
        Assert.Equal("AdvancedFlightComputer", viewModel.SelectedContent?.ModId);
        Assert.True(viewModel.IsContentFromInstance);
        Assert.Equal(instance.InstanceId, viewModel.ContentReturnInstance?.InstanceId);
        Assert.True(viewModel.IsLibrarySection);
        Assert.False(viewModel.IsDiscoverSection);

        await viewModel.ReturnToInstanceCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowInstance);
        Assert.False(viewModel.CurrentWindowContent);
        Assert.Equal(instance.InstanceId, viewModel.SelectedInstance?.InstanceId);
    }

    [Fact]
    public async Task OpenedFromAnInactiveInstance_OffersNoAddOrRemove()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: false, ownership: ModInstallOwnership.Borea);
        var second = (await harness.Services.Instances.CreateAsync("Second", Borea.Core.Instances.InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(second.InstanceId);
        await viewModel.LoadAsync();
        var main = viewModel.Instances.Single(instance => instance.Name == "Main");
        Assert.False(main.IsActive);
        await main.OpenCommand.ExecuteAsync(null);

        await viewModel.ContentGroups.SelectMany(group => group.Items).Single().OpenCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsContentFromInstance);
        Assert.False(viewModel.CanActOnSelectedContent);
    }

    [Fact]
    public async Task OpenedFromTheActiveInstance_OffersAddAndRemove()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        await viewModel.ContentGroups.SelectMany(group => group.Items).Single().OpenCommand.ExecuteAsync(null);

        Assert.True(viewModel.CanActOnSelectedContent);
    }

    [Fact]
    public async Task ReturnAfterAChangeOnThePage_ShowsTheInstanceAsItIsNow()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.ContentGroups.SelectMany(group => group.Items).Single().OpenCommand.ExecuteAsync(null);
        var opened = viewModel.ContentReturnInstance;

        viewModel.SelectedContent!.BeginRemoveCommand.Execute(null);
        await viewModel.SelectedContent.ConfirmRemoveCommand.ExecuteAsync(null);
        await viewModel.ReturnToInstanceCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowInstance);
        Assert.NotSame(opened, viewModel.SelectedInstance);
        Assert.Equal(0, viewModel.SelectedInstance?.ModCount);
    }

    [Fact]
    public async Task ModNotInstalledByBorea_HasNoLinkAndSaysWhy()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.SelectMany(group => group.Items).Single();

        await row.OpenCommand.ExecuteAsync(null);

        Assert.False(row.CanOpen);
        Assert.Equal(harness.Localization.InstanceContentNotOwned, row.NoPageText);
        Assert.True(viewModel.CurrentWindowInstance);
        Assert.False(viewModel.CurrentWindowContent);
    }

    [Fact]
    public async Task OpenedFromDiscover_KeepsTheDiscoverBreadcrumb()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.ContentGroups.SelectMany(group => group.Items).Single().OpenCommand.ExecuteAsync(null);

        viewModel.SetMainWindowDiscover();
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsContentFromInstance);
        Assert.Null(viewModel.ContentReturnInstance);
        Assert.True(viewModel.IsDiscoverSection);
        Assert.False(viewModel.IsLibrarySection);
    }

    [Fact]
    public async Task LanguageChange_RetranslatesTheReason()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        harness.Localization.TrySetCulture("de");

        var row = viewModel.ContentGroups.SelectMany(group => group.Items).Single();
        Assert.Equal(harness.Localization.InstanceContentNotOwned, row.NoPageText);
        Assert.Contains("Borea", row.NoPageText);
    }
}
