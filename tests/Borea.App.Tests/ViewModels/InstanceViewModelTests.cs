using Borea.App.ViewModels;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Preferences;

namespace Borea.App.Tests.ViewModels;

public sealed class InstanceViewModelTests
{
    [Fact]
    public async Task Open_EmptyInstance_ShowsThePageWithoutContent()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();

        await Assert.Single(viewModel.Instances).OpenCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowInstance);
        Assert.True(viewModel.IsLibrarySection);
        Assert.Equal("Main", viewModel.SelectedInstance?.Name);
        Assert.False(viewModel.HasContent);
        Assert.Empty(viewModel.ContentGroups);
    }

    [Fact]
    public async Task Open_GroupsContentByHowItWasAdded()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true);
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, InstallReason.Dependency);
        await viewModel.LoadAsync();

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasContent);
        Assert.Equal(
            [harness.Localization.InstanceGroupMods, harness.Localization.InstanceGroupDependencies],
            viewModel.ContentGroups.Select(group => group.Title));
        var afc = Assert.Single(viewModel.ContentGroups[0].Items);
        Assert.Equal("AdvancedFlightComputer", afc.ModId);
        Assert.Equal("Advanced Flight Computer", afc.Name);
        Assert.NotNull(afc.AuthorsText);
        Assert.False(afc.IsDependency);
        Assert.True(afc.IsEnabled);
        Assert.True(Assert.Single(viewModel.ContentGroups[1].Items).IsDependency);
    }

    [Fact]
    public async Task ToggleEnabled_WritesTheManifest()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var item = viewModel.ContentGroups.Single().Items.Single();

        item.IsEnabled = false;
        await item.ToggleEnabledCommand.ExecuteAsync(null);
        Assert.False(await harness.Services.ModState.IsActiveAsync(instance.InstanceId, "AdvancedFlightComputer"));

        item.IsEnabled = true;
        await item.ToggleEnabledCommand.ExecuteAsync(null);
        Assert.True(await harness.Services.ModState.IsActiveAsync(instance.InstanceId, "AdvancedFlightComputer"));
        Assert.Null(viewModel.ContentError);
    }

    [Fact]
    public async Task Remove_AfterConfirmation_TakesTheModOutOfTheInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var item = viewModel.ContentGroups.Single().Items.Single();

        item.BeginRemoveCommand.Execute(null);
        Assert.True(item.IsConfirmingRemove);
        item.CancelRemoveCommand.Execute(null);
        Assert.False(item.IsConfirmingRemove);
        item.BeginRemoveCommand.Execute(null);
        await item.ConfirmRemoveCommand.ExecuteAsync(null);

        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
        Assert.False(Directory.Exists(Path.Combine(harness.Services.Paths.GetInstanceModsFolder(instance.InstanceId), "AdvancedFlightComputer")));
        Assert.Null(viewModel.ContentError);
        Assert.False(viewModel.HasContent);
        Assert.Equal(0, viewModel.ActiveInstance?.ModCount);
        Assert.True(viewModel.CurrentWindowInstance);
    }

    [Fact]
    public async Task Remove_ModBoreaDidNotInstall_KeepsItAndExplains()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        await viewModel.ContentGroups.Single().Items.Single().ConfirmRemoveCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatContentRemoveNotOwned("AdvancedFlightComputer"), viewModel.ContentError);
        Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
        Assert.True(Directory.Exists(Path.Combine(harness.Services.Paths.GetInstanceModsFolder(instance.InstanceId), "AdvancedFlightComputer")));
    }

    [Fact]
    public async Task DeleteFromThePage_ReturnsToTheLibrary()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        await viewModel.Instances.Single().OpenCommand.ExecuteAsync(null);

        await viewModel.SelectedInstance!.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowLibrary);
        Assert.False(viewModel.CurrentWindowInstance);
    }

    [Fact]
    public async Task Play_WithoutALoader_ShowsTheLauncherMessage()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        await viewModel.Instances.Single().OpenCommand.ExecuteAsync(null);

        await viewModel.PlayCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrWhiteSpace(viewModel.LaunchMessage));
        Assert.False(viewModel.IsLaunching);
    }

    [Fact]
    public async Task LanguageChange_RetitlesTheGroups()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        harness.Localization.TrySetCulture("de");

        Assert.Equal(harness.Localization.InstanceGroupMods, viewModel.ContentGroups.Single().Title);
        Assert.Equal(harness.Localization.HomeInstanceSourceCustom, viewModel.ActiveInstance!.SourceText);
    }

    [Fact]
    public async Task PlayKSAWithoutModLoader_ShowsErrorMessageIfNoGameDirectorySet()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.PlayWithoutModLoaderCommand.ExecuteAsync(null);

        // InstalledVersionText is null when no Game Directory is set
        Assert.Null(viewModel.InstalledVersionText);
        Assert.NotNull(viewModel.LaunchMessage);
    }

    [Fact]
    public async Task HomeLaunch_ChosenFromTheMenu_BecomesTheButtonAndIsSaved()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        Assert.True(viewModel.IsHomeLaunchActiveInstance);
        Assert.Equal(harness.Localization.LaunchActiveInstance, viewModel.HomeLaunchText);
        Assert.False(viewModel.EnableHomeLaunch);

        await viewModel.PlayWithoutModLoaderCommand.ExecuteAsync(null);
        await viewModel.WhenPreferencesSavedAsync();

        Assert.False(viewModel.IsHomeLaunchActiveInstance);
        Assert.Equal(harness.Localization.LaunchWithoutModLoader, viewModel.HomeLaunchText);
        Assert.True(viewModel.EnableHomeLaunch);
        var saved = await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames);
        Assert.Equal(HomeLaunchOption.WithoutModLoader, saved.Preferences.HomeLaunch);

        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsHomeLaunchActiveInstance);
        Assert.Equal(harness.Localization.LaunchActiveInstance, viewModel.HomeLaunchText);
    }

    [Fact]
    public async Task HomeLaunch_Button_StartsTheOptionLastChosen()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.PlayWithoutModLoaderCommand.ExecuteAsync(null);
        var withoutLoaderMessage = viewModel.LaunchMessage;
        viewModel.LaunchMessage = null;

        await viewModel.PlayHomeCommand.ExecuteAsync(null);

        Assert.NotNull(withoutLoaderMessage);
        Assert.Equal(withoutLoaderMessage, viewModel.LaunchMessage);
    }
}
