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
    public async Task ModNotInstalledByBorea_LinksToItsPageAndOffersTheHandover()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.SelectMany(group => group.Items).Single();
        Assert.False(row.IsOwned);
        Assert.True(row.CanManage);
        Assert.Null(row.NoPageText);

        await row.OpenCommand.ExecuteAsync(null);

        Assert.True(row.CanOpen);
        Assert.True(viewModel.CurrentWindowContent);
        Assert.Equal("AdvancedFlightComputer", viewModel.SelectedContent?.ModId);
    }

    [Fact]
    public async Task ModInstalledByBorea_OffersNoHandover()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        Assert.False(viewModel.ContentGroups.SelectMany(group => group.Items).Single().CanManage);
    }

    [Fact]
    public async Task Manage_AsksBeforeTheFilesAreReplaced()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.SelectMany(group => group.Items).Single();

        row.BeginManageCommand.Execute(null);

        Assert.True(row.IsConfirmingManage);
        Assert.Contains(row.Version, row.ManageConfirmText);
        Assert.Equal(ModInstallOwnership.Foreign, Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods).Ownership);

        row.CancelManageCommand.Execute(null);

        Assert.False(row.IsConfirmingManage);
    }

    [Fact]
    public async Task Manage_DownloadFails_SaysSoAndLeavesTheModForeign()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.SelectMany(group => group.Items).Single();
        row.BeginManageCommand.Execute(null);

        await row.ConfirmManageCommand.ExecuteAsync(null);

        var toast = Assert.Single(viewModel.Toasts.Items);
        Assert.Equal(harness.Localization.FormatToastManageFailed(row.Name), toast.Message);
        Assert.False(row.IsInstalling);
        var saved = Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
        Assert.Equal(ModInstallOwnership.Foreign, saved.Ownership);
        Assert.True(File.Exists(Path.Combine(harness.Services.Paths.GetInstanceModsFolder(instance.InstanceId), "AdvancedFlightComputer", "mod.toml")));
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
    public async Task LanguageChange_RetranslatesTheHandoverConfirmation()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        harness.Localization.TrySetCulture("de");

        var row = viewModel.ContentGroups.SelectMany(group => group.Items).Single();
        Assert.Equal(harness.Localization.FormatContentManageConfirm(row.Name, row.Version), row.ManageConfirmText);
        Assert.Contains("Borea", row.ManageConfirmText);
    }
}
