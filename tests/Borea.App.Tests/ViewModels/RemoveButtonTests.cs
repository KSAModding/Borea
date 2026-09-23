using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

public sealed class RemoveButtonTests
{
    [Fact]
    public async Task ModPage_InstalledMod_RemovesWithoutAMenuAndAsksFirst()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        var afc = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");
        await afc.OpenCommand.ExecuteAsync(null);

        Assert.True(afc.CanRemove);
        Assert.Null(afc.RemoveBlockedText);
        Assert.Equal(harness.Localization.ContentRemove, afc.RemoveToolTip);

        afc.BeginRemoveCommand.Execute(null);
        Assert.True(afc.IsConfirmingRemove);
        Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);

        await afc.ConfirmRemoveCommand.ExecuteAsync(null);

        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
        Assert.False(afc.IsInstalled);
    }

    [Fact]
    public async Task ModNotInstalledByBorea_DisablesTheButtonAndSaysWhy()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();

        var afc = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");
        var expected = harness.Localization.FormatContentRemoveNotOwned("AdvancedFlightComputer");
        Assert.False(afc.CanRemove);
        Assert.False(afc.BeginRemoveCommand.CanExecute(null));
        Assert.Equal(expected, afc.RemoveBlockedText);
        Assert.Equal(expected, afc.RemoveToolTip);

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.SelectMany(group => group.Items).Single();
        Assert.False(row.CanRemove);
        Assert.Equal(expected, row.RemoveBlockedText);
    }

    [Fact]
    public async Task ModAnotherModNeeds_DisablesTheButtonAndNamesIt()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea);
        var needsAfc = new ForeignMod("FlightPlanner", [new LocalModDependency("AdvancedFlightComputer", optional: false)]);
        var withDependent = Instance.FromExisting(instance.InstanceId, instance.Name, instance.Source, instance.CreatedAt, instance.Mods.ToList(), [needsAfc], instance.IsFavorite);
        await harness.Services.Instances.SaveAsync(withDependent);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();

        var afc = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");
        var expected = harness.Localization.FormatContentRemoveRequired("AdvancedFlightComputer", "FlightPlanner");
        Assert.False(afc.CanRemove);
        Assert.False(afc.BeginRemoveCommand.CanExecute(null));
        Assert.Equal(expected, afc.RemoveBlockedText);
        Assert.Equal(expected, afc.RemoveToolTip);

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.SelectMany(group => group.Items).Single(item => item.ModId == "AdvancedFlightComputer");
        Assert.False(row.CanRemove);
        Assert.Equal(expected, row.RemoveBlockedText);
    }

    [Fact]
    public async Task LanguageChange_RetranslatesTheReason()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();

        harness.Localization.TrySetCulture("de");

        var afc = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");
        Assert.Equal(harness.Localization.FormatContentRemoveNotOwned("AdvancedFlightComputer"), afc.RemoveBlockedText);
    }
}
