using Borea.App.ViewModels;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Mods;
using static Borea.App.Tests.ViewModels.UserContentFixtures;

namespace Borea.App.Tests.ViewModels;

/// <summary>Vehicles and saves in Discover, on their page and in an instance (RFC 0085).</summary>
public sealed class UserContentViewModelTests
{
    [Fact]
    public async Task Discover_SnapshotWithoutVehicles_KeepsTheTabsDisabledAndTheModsUnchanged()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        await viewModel.EnsureDiscoverLoadedAsync();

        Assert.False(viewModel.HasDiscoverVehicles);
        Assert.False(viewModel.HasDiscoverSaves);
        Assert.Equal(["AdvancedFlightComputer", "KSArmory", "MeasureTools"], viewModel.DiscoverItems.Select(item => item.ModId));
        Assert.Equal(harness.Localization.FormatDiscoverModCount(3), viewModel.DiscoverCountText);
    }

    [Fact]
    public async Task Discover_SnapshotWithAVehicle_EnablesItsTabAndListsIt()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        Assert.True(viewModel.HasDiscoverVehicles);
        Assert.False(viewModel.HasDiscoverSaves);
        Assert.Equal(["AdvancedFlightComputer", "KSArmory", "MeasureTools"], viewModel.DiscoverItems.Select(item => item.ModId));
        Assert.Equal(harness.Localization.FormatDiscoverModCount(3), viewModel.DiscoverCountText);

        viewModel.ShowDiscoverVehiclesCommand.Execute(null);

        Assert.True(viewModel.IsVehiclesTab);
        Assert.False(viewModel.ShowsCategories);
        var row = Assert.Single(viewModel.DiscoverItems);
        Assert.Equal(("ExampleRover", "Example Rover", "A vehicle.", "Kitten"), (row.ModId, row.Name, row.Abstract, Assert.Single(row.AuthorLinks).Name));
        Assert.Equal(harness.Localization.ContentTypeVehicle, row.TypeText);
        Assert.Equal(42L, row.Downloads);
        Assert.Equal(">= 2026.8.19.5261", row.GameVersionText);
        Assert.Equal(ModVersion.Parse("1.1.0"), row.LatestInChannel!.Version);
        Assert.Equal(harness.Localization.FormatDiscoverVehicleCount(1), viewModel.DiscoverCountText);
    }

    [Fact]
    public async Task Discover_SearchOnTheVehiclesTab_FindsOnlyVehicles_AndAModSearchFindsNoVehicle()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        viewModel.SearchText = "Rover";
        Assert.Empty(viewModel.DiscoverItems);

        viewModel.ShowDiscoverVehiclesCommand.Execute(null);
        Assert.Equal("ExampleRover", Assert.Single(viewModel.DiscoverItems).ModId);

        viewModel.SearchText = "Flight";
        Assert.Empty(viewModel.DiscoverItems);
    }

    [Fact]
    public async Task Discover_ChosenCategory_DoesNotFilterTheVehiclesTab()
    {
        using var harness = await VehicleHarnessAsync(editSnapshot: ViewModelHarness.CuratedTags(("control", "Control")));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var control = viewModel.CategoryOptions.First(category => category.Tag == "control");
        viewModel.ToggleCategoryCommand.Execute(control);
        Assert.True(viewModel.HasDiscoverFilters);

        viewModel.ShowDiscoverVehiclesCommand.Execute(null);

        Assert.Equal("ExampleRover", Assert.Single(viewModel.DiscoverItems).ModId);
        Assert.False(viewModel.HasDiscoverFilters);
    }

    [Fact]
    public async Task Discover_VehicleRecordedInTheActiveInstance_IsInstalledAndHiddenByHideInstalled()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        var instance = await ActiveInstanceWithRoverAsync(harness);
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);

        Assert.True(Assert.Single(viewModel.DiscoverItems).IsInstalled);
        viewModel.HideInstalled = true;
        Assert.Empty(viewModel.DiscoverItems);
        Assert.Equal(ModVersion.Parse("1.0.0"), viewModel.ActiveInstance!.InstalledVersionOf("ExampleRover"));
        Assert.Equal(instance.InstanceId, viewModel.ActiveInstance.InstanceId);
    }

    [Fact]
    public async Task Discover_VehicleRecordedInAnotherInstance_ShowsUnderInstalledInOtherInstances()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        await ActiveInstanceWithRoverAsync(harness);
        var other = (await harness.Services.Instances.CreateAsync("Other", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(other.InstanceId);
        await viewModel.LoadAsync();
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);

        viewModel.InstalledInOtherInstances = true;

        var row = Assert.Single(viewModel.DiscoverItems);
        Assert.False(row.IsInstalled);
    }

    [Fact]
    public async Task Discover_FavoriteVehicle_ShowsUnderFavoritesOnly()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);
        var row = Assert.Single(viewModel.DiscoverItems);

        await row.ToggleFavoriteCommand.ExecuteAsync(null);
        viewModel.FavoritesOnly = true;

        Assert.Same(row, Assert.Single(viewModel.DiscoverItems));
        Assert.True(row.IsFavorite);
    }

    [Fact]
    public async Task Page_VehicleWithTwoRequiredMods_ListsThemAndMarksTheInstalledOne()
    {
        using var harness = await VehicleHarnessAsync(dependencies: Required("AdvancedFlightComputer", "0.7.0") + ", " + Required("MeasureTools"));
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea, version: "0.7.4");
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);

        await Assert.Single(viewModel.DiscoverItems).OpenCommand.ExecuteAsync(null);
        viewModel.ShowContentDependenciesCommand.Execute(null);

        Assert.Equal(harness.Localization.ContentTabNeedsMods, viewModel.ContentDependenciesTabText);
        Assert.Equal(harness.Localization.FormatContentFileBuild("2026.8.19.5261"), viewModel.ContentFileBuildText);
        Assert.Equal("1.1.0", viewModel.LatestVersion!.Version);
        var group = Assert.Single(viewModel.ContentDependencyGroups);
        Assert.Equal(DependencyGroupKind.Required, group.Kind);
        var members = group.Entries.Select(entry => Assert.Single(entry.Members)).ToList();
        Assert.Equal(["AdvancedFlightComputer", "MeasureTools"], members.Select(member => member.ModId));
        Assert.True(members[0].IsInstalled);
        Assert.Equal(harness.Localization.FormatContentDependencyInstalled("0.7.4"), members[0].StateText);
        Assert.False(members[1].IsInstalled);
        Assert.Equal(harness.Localization.ContentDependencyNotInstalled, members[1].StateText);
        Assert.Null(viewModel.ContentShareUrl);
    }

    [Fact]
    public async Task Page_VersionsTab_ListsTheReleasesOfTheVehicle()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);
        await Assert.Single(viewModel.DiscoverItems).OpenCommand.ExecuteAsync(null);

        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);

        Assert.Equal(["1.1.0", "1.0.0"], viewModel.ContentVersions.Select(version => version.Version));
    }

    [Fact]
    public async Task Page_Mod_KeepsTheDependenciesTabNameAndNoFileBuild()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        await viewModel.DiscoverItems.Single(item => item.ModId == "MeasureTools").OpenCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.ContentTabDependencies, viewModel.ContentDependenciesTabText);
        Assert.Null(viewModel.ContentFileBuildText);
        Assert.NotNull(viewModel.ContentShareUrl);
    }

    [Fact]
    public async Task Install_TakenName_OpensTheNameModal_AndAValidNameInstalls()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        var instance = await ActiveInstanceAsync(harness);
        var vehicles = harness.Services.Paths.GetInstanceVehiclesFolder(instance.InstanceId);
        var mine = Directory.CreateDirectory(Path.Combine(vehicles, "Example Rover")).FullName;
        File.WriteAllText(Path.Combine(mine, "vehicle.xml"), "<Vehicle mine=\"yes\" />");
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);
        var row = Assert.Single(viewModel.DiscoverItems);

        await row.InstallCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsNameModalOpen);
        Assert.True(viewModel.ShowsGameSaveNameRules);
        Assert.Null(viewModel.NameModalSkipText);
        Assert.Equal(harness.Localization.FormatContentNameTaken("Example Rover", "Example Rover", harness.Localization.InstanceGroupVehicles), viewModel.NameModalExplanation);
        Assert.Equal("Example Rover 2", viewModel.ModalInstanceName);
        Assert.DoesNotContain(harness.Requests, uri => uri.AbsoluteUri == RoverUrl);

        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);

        // without a game the compatibility is unknown, so the install waits for a go, as a mod does
        Assert.False(viewModel.IsNameModalOpen);
        Assert.True(row.IsConfirmingInstall);
        await row.ConfirmInstallCommand.ExecuteAsync(null);
        Assert.Null(row.InstallError);
        var installed = Path.Combine(vehicles, "Example Rover 2");
        Assert.StartsWith("name = \"Example Rover 2\"", File.ReadAllText(Path.Combine(installed, "meta.toml")), StringComparison.Ordinal);
        Assert.Equal("<Vehicle mine=\"yes\" />", File.ReadAllText(Path.Combine(mine, "vehicle.xml")));
        var record = Assert.Single(await harness.Services.InstalledContent.ListAsync(instance.InstanceId));
        Assert.Equal(("Example Rover 2", ModVersion.Parse("1.1.0")), (record.FolderName, record.Version));
        Assert.True(viewModel.DiscoverItems.Single().IsInstalled);
    }

    [Fact]
    public async Task Install_DisabledRequiredMod_WaitsWithTheEnable_AndTheGoInstallsAndEnablesIt()
    {
        using var harness = await VehicleHarnessAsync(dependencies: Required("MeasureTools"));
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, ownership: ModInstallOwnership.Borea);
        await harness.Services.ModState.SetInactiveAsync(instance.InstanceId, "MeasureTools");
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);
        var row = Assert.Single(viewModel.DiscoverItems);

        await row.InstallCommand.ExecuteAsync(null);

        Assert.True(row.IsConfirmingInstall);
        Assert.Contains(harness.Localization.FormatPackContentEnables("MeasureTools", "Example Rover"), row.InstallWarning, StringComparison.Ordinal);
        Assert.Empty(await harness.Services.InstalledContent.ListAsync(instance.InstanceId));

        await row.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Null(row.InstallError);
        Assert.False(row.IsConfirmingInstall);
        Assert.Single(await harness.Services.InstalledContent.ListAsync(instance.InstanceId));
        Assert.True(await harness.Services.ModState.IsActiveAsync(instance.InstanceId, "MeasureTools"));
    }

    [Fact]
    public async Task Install_RequiredModThatIsNotListed_NamesWhatTheGameDoes_AndTheGoInstallsWithoutIt()
    {
        using var harness = await VehicleHarnessAsync(dependencies: Required("NotListedParts"));
        var viewModel = harness.ViewModel;
        var instance = await ActiveInstanceAsync(harness);
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);
        var row = Assert.Single(viewModel.DiscoverItems);

        await row.InstallCommand.ExecuteAsync(null);

        Assert.Contains("so the game refuses this vehicle", row.InstallWarning, StringComparison.Ordinal);
        await row.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Null(row.InstallError);
        Assert.Single(await harness.Services.InstalledContent.ListAsync(instance.InstanceId));
    }

    [Fact]
    public async Task Install_WhileTheGameRuns_SaysCloseTheGameFirstAndChangesNothing()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        var instance = await ActiveInstanceAsync(harness);
        harness.IsGameProcessRunning = () => true;
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);
        var row = Assert.Single(viewModel.DiscoverItems);

        await row.InstallCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.GameSaveCloseGame, row.InstallError);
        Assert.Empty(await harness.Services.InstalledContent.ListAsync(instance.InstanceId));
    }

    [Fact]
    public async Task VersionRow_OfARecordedVehicle_UpdatesItToThatRelease()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        var instance = await ActiveInstanceWithRoverAsync(harness);
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);
        await Assert.Single(viewModel.DiscoverItems).OpenCommand.ExecuteAsync(null);
        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);
        var newer = viewModel.ContentVersions.Single(version => version.Version == "1.1.0");
        Assert.True(viewModel.ContentVersions.Single(version => version.Version == "1.0.0").IsInstalled);
        Assert.Equal("1.0.0", newer.ReplacedVersion);

        await newer.InstallCommand.ExecuteAsync(null);
        Assert.True(newer.IsConfirmingInstall);
        await newer.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Null(newer.InstallError);
        Assert.Equal(ModVersion.Parse("1.1.0"), Assert.Single(await harness.Services.InstalledContent.ListAsync(instance.InstanceId)).Version);
    }

    [Fact]
    public async Task DiscoverRemove_ChangedVehicle_NamesTheFilesFirst_AndTheSecondGoRemovesIt()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        var instance = await ActiveInstanceWithRoverAsync(harness);
        var folder = Path.Combine(harness.Services.Paths.GetInstanceVehiclesFolder(instance.InstanceId), "Example Rover");
        File.WriteAllText(Path.Combine(folder, "vehicle.xml"), "<Vehicle edited=\"yes\" />");
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);
        var row = Assert.Single(viewModel.DiscoverItems);
        Assert.True(row.CanRemove);

        row.BeginRemoveCommand.Execute(null);
        await row.ConfirmRemoveCommand.ExecuteAsync(null);

        Assert.True(row.IsConfirmingRemove);
        Assert.Equal(harness.Localization.FormatContentRemoveChanged("Example Rover", "vehicle.xml"), row.RemoveConfirmText);
        Assert.True(Directory.Exists(folder));

        await row.ConfirmRemoveCommand.ExecuteAsync(null);

        Assert.False(Directory.Exists(folder));
        Assert.Empty(await harness.Services.InstalledContent.ListAsync(instance.InstanceId));
        Assert.False(viewModel.DiscoverItems.Single().IsInstalled);
    }

    private static async Task<Instance> ActiveInstanceAsync(ViewModelHarness harness)
    {
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        return instance;
    }

    /// <summary>The vehicle Example Rover with releases 1.0.0 and 1.1.0, served, with 42 downloads.</summary>
    internal static Task<ViewModelHarness> VehicleHarnessAsync(Func<string, string>? editSnapshot = null, string dependencies = "")
    {
        var rover = RoverArchive();
        var entry = EntryNeeding("ExampleRover", "vehicle", "Example Rover", RoverUrl, rover, dependencies, ["1.0.0", "1.1.0"])
            .Replace("\"id\": \"ExampleRover\",", "\"id\": \"ExampleRover\", \"downloads\": { \"total\": 42, \"hosts\": { \"github\": 42 } },", StringComparison.Ordinal);
        return ViewModelHarness.CreateAsync(
            respond: request => request.RequestUri?.AbsoluteUri == RoverUrl ? ArchiveResponse(rover) : null,
            editSnapshot: editSnapshot,
            typeFiles: TypeFiles(ContentIndexTypeFiles.Vehicles, entry));
    }

    /// <summary>An active instance that holds Example Rover 1.0.0 as Borea installs it.</summary>
    internal static async Task<Instance> ActiveInstanceWithRoverAsync(ViewModelHarness harness)
    {
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        var release = (await harness.Services.UserContent.GetReleaseAsync("ExampleRover", ModVersion.Parse("1.0.0")))!;
        Assert.True((await harness.Services.UserContentInstaller.InstallAsync(instance.InstanceId, release, name: null)).IsDone);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        return instance;
    }
}
