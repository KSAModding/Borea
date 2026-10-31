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
    public async Task Discover_SnapshotWithoutVehicles_TheTabsSayThatNothingIsSharedAndTheModsStayUnchanged()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        await viewModel.EnsureDiscoverLoadedAsync();

        Assert.False(viewModel.ShowsNothingSharedYet);
        Assert.Equal(["AdvancedFlightComputer", "KSArmory", "MeasureTools"], viewModel.DiscoverItems.Select(item => item.ModId));
        Assert.Equal(harness.Localization.FormatDiscoverModCount(3), viewModel.DiscoverCountText);

        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);
        Assert.Contains(nameof(MainViewModel.ShowsNothingSharedYet), changed);
        Assert.True(viewModel.ShowsNothingSharedYet);
        Assert.False(viewModel.ShowsClearSearchAndFilters);
        Assert.Equal(harness.Localization.DiscoverNoVehicles, viewModel.DiscoverNothingSharedText);
        Assert.Equal(harness.Localization.DiscoverNoVehiclesHint, viewModel.DiscoverNothingSharedHint);
        Assert.Equal(harness.Localization.DiscoverSearchVehicles, viewModel.DiscoverSearchPlaceholderText);

        viewModel.ShowDiscoverSavesCommand.Execute(null);
        Assert.True(viewModel.ShowsNothingSharedYet);
        Assert.Equal(harness.Localization.DiscoverNoSaves, viewModel.DiscoverNothingSharedText);
        Assert.Equal(harness.Localization.DiscoverNoSavesHint, viewModel.DiscoverNothingSharedHint);
        Assert.Equal(harness.Localization.DiscoverSearchSaves, viewModel.DiscoverSearchPlaceholderText);
    }

    [Fact]
    public async Task Discover_WhileTheListingsAreNotKnown_TheTabDoesNotSayThatNothingIsShared()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        void AssertHiddenWithANotice(Action change)
        {
            changed.Clear();
            change();
            Assert.Contains(nameof(MainViewModel.ShowsNothingSharedYet), changed);
            Assert.False(viewModel.ShowsNothingSharedYet);
        }

        AssertHiddenWithANotice(() => viewModel.IsDiscoverLoading = true);
        viewModel.IsDiscoverLoading = false;

        AssertHiddenWithANotice(() => viewModel.DiscoverError = "offline");
        viewModel.DiscoverError = null;

        AssertHiddenWithANotice(() => viewModel.IndexRefreshStatus = new ContentIndexRefreshStatus(ContentIndexRefreshOutcome.Failed, CachedAt: DateTimeOffset.UnixEpoch, "offline"));

        AssertHiddenWithANotice(() => viewModel.IndexRefreshStatus = new ContentIndexRefreshStatus(ContentIndexRefreshOutcome.Failed, CachedAt: null, "offline"));
    }

    [Theory]
    [InlineData(GameSaveKind.Vehicle, ContentType.Vehicle)]
    [InlineData(GameSaveKind.Save, ContentType.Save)]
    public async Task EmptyTable_FindOpensTheDiscoverTabOfItsType(GameSaveKind kind, ContentType tab)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var section = kind == GameSaveKind.Vehicle ? viewModel.VehiclesSection : viewModel.SavesSection;

        section.FindOnDiscoverCommand.Execute(null);

        Assert.True(viewModel.CurrentWindowDiscover);
        Assert.Equal(tab, viewModel.DiscoverType);
    }

    [Fact]
    public async Task Discover_SnapshotWithAVehicle_ListsItOnItsTab()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        Assert.Equal(["AdvancedFlightComputer", "KSArmory", "MeasureTools"], viewModel.DiscoverItems.Select(item => item.ModId));
        Assert.Equal(harness.Localization.FormatDiscoverModCount(3), viewModel.DiscoverCountText);

        viewModel.ShowDiscoverVehiclesCommand.Execute(null);

        Assert.True(viewModel.IsVehiclesTab);
        Assert.False(viewModel.ShowsCategories);
        Assert.False(viewModel.ShowsNothingSharedYet);
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
        Assert.Equal(harness.Localization.ContentDependencyRequiredItemHint, group.Hint);
        Assert.Null(viewModel.ContentShareUrl);
    }

    [Fact]
    public async Task Page_VehicleThatNamesADisabledModAsAConflict_SaysTheModIsDisabled()
    {
        using var harness = await VehicleHarnessAsync(dependencies: """{ "id": "MeasureTools", "kind": "conflict", "source": "authored" }""");
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, ownership: ModInstallOwnership.Borea);
        await harness.Services.ModState.SetInactiveAsync(instance.InstanceId, "MeasureTools");
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);

        await Assert.Single(viewModel.DiscoverItems).OpenCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.ContentDependencyGroups);
        var member = Assert.Single(viewModel.ContentConflicts);
        Assert.Equal(harness.Localization.FormatContentDependencyDisabled(Assert.Single(instance.Mods).Version.ToString()), member.StateText);
    }

    [Fact]
    public async Task ModPage_OfAModThatAHeldVehicleNeeds_ListsTheVehicleInRequiredBy()
    {
        using var harness = await VehicleHarnessAsync(dependencies: Required("MeasureTools"));
        var viewModel = harness.ViewModel;
        await ActiveInstanceWithRoverAsync(harness);

        await viewModel.DiscoverItems.Single(item => item.ModId == "MeasureTools").OpenCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasContentRequiredBy);
        var member = Assert.Single(viewModel.ContentRequiredBy);
        Assert.Equal("ExampleRover", member.ModId);
        Assert.Equal(harness.Localization.FormatContentDependencyInstalled("1.0.0"), member.StateText);
        Assert.Equal(harness.Localization.ContentTabDependencies, viewModel.ContentDependenciesTabText);
    }

    [Fact]
    public async Task Page_VehicleNeedingADisabledMod_SaysTheModIsDisabled()
    {
        using var harness = await VehicleHarnessAsync(dependencies: Required("MeasureTools"));
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, ownership: ModInstallOwnership.Borea);
        await harness.Services.ModState.SetInactiveAsync(instance.InstanceId, "MeasureTools");
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);

        await Assert.Single(viewModel.DiscoverItems).OpenCommand.ExecuteAsync(null);

        var member = Assert.Single(Assert.Single(Assert.Single(viewModel.ContentDependencyGroups).Entries).Members);
        Assert.Equal(harness.Localization.FormatContentDependencyDisabled(Assert.Single(instance.Mods).Version.ToString()), member.StateText);
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
        Assert.Equal(harness.Localization.FormatContentNameTaken("Example Rover", harness.Localization.InstanceGroupVehicles), viewModel.NameModalExplanation);
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

    [Fact]
    public async Task InstancePage_RecordedAndChangedVehicle_ShowsTheMark_AndRemoveAsksFirst()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        var instance = await ActiveInstanceWithRoverAsync(harness);
        var folder = Path.Combine(harness.Services.Paths.GetInstanceVehiclesFolder(instance.InstanceId), "Example Rover");
        File.WriteAllText(Path.Combine(folder, "vehicle.xml"), "<Vehicle edited=\"yes\" />");

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.WhenRecordedContentComparedAsync();

        var row = Assert.Single(viewModel.VehiclesSection.Items);
        Assert.True(row.HasRecord);
        Assert.Equal("Example Rover", row.ListingName);
        Assert.Equal(harness.Localization.FormatContentDependencyInstalled("1.0.0"), row.InstalledVersionText);
        Assert.Equal(harness.Localization.FormatPackMemberNewer("1.1.0"), row.NewerText);
        Assert.True(row.IsChanged);
        Assert.Equal("vehicle.xml", row.ChangedFilesText);

        row.BeginDeleteCommand.Execute(null);

        Assert.Equal(harness.Localization.FormatGameSaveDeleteChanged("Example Rover", "vehicle.xml"), row.DeleteConfirmText);
        Assert.True(Directory.Exists(folder));

        await row.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.False(Directory.Exists(folder));
        Assert.Empty(await harness.Services.InstalledContent.ListAsync(instance.InstanceId));
        Assert.Empty(viewModel.VehiclesSection.Items);
    }

    [Fact]
    public async Task InstancePage_FileChangedAfterTheRowsShowed_RemoveAsksAgainWithTheFiles()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        var instance = await ActiveInstanceWithRoverAsync(harness);
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.WhenRecordedContentComparedAsync();
        var row = Assert.Single(viewModel.VehiclesSection.Items);
        Assert.False(row.IsChanged);
        var folder = Path.Combine(harness.Services.Paths.GetInstanceVehiclesFolder(instance.InstanceId), "Example Rover");
        File.WriteAllText(Path.Combine(folder, "vehicle.xml"), "<Vehicle edited=\"yes\" />");

        row.BeginDeleteCommand.Execute(null);
        await row.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.True(row.IsConfirmingDelete);
        Assert.Equal(harness.Localization.FormatGameSaveDeleteChanged("Example Rover", "vehicle.xml"), row.DeleteConfirmText);
        Assert.True(Directory.Exists(folder));

        await row.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task InstancePage_OwnFolder_HasNoRecordAndKeepsTheDeleteText()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        var instance = await ActiveInstanceAsync(harness);
        var folder = Directory.CreateDirectory(Path.Combine(harness.Services.Paths.GetInstanceVehiclesFolder(instance.InstanceId), "Mine")).FullName;
        File.WriteAllText(Path.Combine(folder, "meta.toml"), "name = \"Mine\"\n");

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.VehiclesSection.Items);
        Assert.False(row.HasRecord);
        Assert.False(row.CanUpdate);
        Assert.Equal(harness.Localization.GameSaveDeleteConfirm, row.DeleteConfirmText);
    }

    [Fact]
    public async Task InstancePage_UpdateOfARecordedVehicle_WaitsForTheGo_AndInstallsTheNewerRelease()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        var instance = await ActiveInstanceWithRoverAsync(harness);
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = Assert.Single(viewModel.VehiclesSection.Items);
        Assert.True(row.CanUpdate);
        Assert.Equal(harness.Localization.FormatContentUpdateTo("1.1.0"), row.UpdateText);

        await row.UpdateCommand.ExecuteAsync(null);

        Assert.True(row.IsConfirmingInstall);
        Assert.False(row.IsIdle);
        Assert.Equal(ModVersion.Parse("1.0.0"), Assert.Single(await harness.Services.InstalledContent.ListAsync(instance.InstanceId)).Version);

        await row.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Equal(ModVersion.Parse("1.1.0"), Assert.Single(await harness.Services.InstalledContent.ListAsync(instance.InstanceId)).Version);
        var updated = Assert.Single(viewModel.VehiclesSection.Items);
        Assert.Equal(harness.Localization.FormatContentDependencyInstalled("1.1.0"), updated.InstalledVersionText);
        Assert.False(updated.CanUpdate);
        Assert.Contains(viewModel.Tasks.History, task => task.Kind == Borea.Core.History.TaskKind.Update && task.ContentId == "ExampleRover");
    }

    [Fact]
    public async Task InstancePage_UpdateOfAChangedVehicle_NamesTheFiles_AndTheGoReplacesIt()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        var instance = await ActiveInstanceWithRoverAsync(harness);
        var file = Path.Combine(harness.Services.Paths.GetInstanceVehiclesFolder(instance.InstanceId), "Example Rover", "vehicle.xml");
        File.WriteAllText(file, "<Vehicle edited=\"yes\" />");
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = Assert.Single(viewModel.VehiclesSection.Items);

        await row.UpdateCommand.ExecuteAsync(null);

        Assert.Contains(harness.Localization.FormatPackContentChanged("Example Rover", "vehicle.xml"), row.InstallWarning, StringComparison.Ordinal);
        Assert.Equal("<Vehicle edited=\"yes\" />", File.ReadAllText(file));

        await row.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Equal("<Vehicle />", File.ReadAllText(file));
        Assert.Equal(ModVersion.Parse("1.1.0"), Assert.Single(await harness.Services.InstalledContent.ListAsync(instance.InstanceId)).Version);
    }

    [Fact]
    public async Task InstancePage_RecordedRow_OpensThePageOfItsListing()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        await ActiveInstanceWithRoverAsync(harness);
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        await Assert.Single(viewModel.VehiclesSection.Items).OpenPageCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowContent);
        Assert.True(viewModel.IsContentFromInstance);
        Assert.Equal("ExampleRover", viewModel.SelectedContent!.ModId);
    }

    [Fact]
    public async Task RemoveMod_ARecordedVehicleRequires_NamesTheVehicle_AndStillRemovesOnConfirm()
    {
        using var harness = await VehicleHarnessAsync(dependencies: Required("AdvancedFlightComputer"));
        var viewModel = harness.ViewModel;
        var instance = await InstanceWithAfcAndRoverAsync(harness);
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.SelectMany(group => group.Items).Single(item => item.ModId == "AdvancedFlightComputer");
        var needed = harness.Localization.FormatContentNeededByVehicle("Example Rover");

        Assert.True(row.CanRemove);
        Assert.Equal($"{needed} {harness.Localization.ContentRemoveConfirm}", row.RemoveConfirmText);
        viewModel.ShowDiscoverModsCommand.Execute(null);
        Assert.Contains(needed, viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").RemoveConfirmText, StringComparison.Ordinal);
        Assert.Null(viewModel.DiscoverItems.Single(item => item.ModId == "MeasureTools").NeededByText);

        row.BeginRemoveCommand.Execute(null);
        await row.ConfirmRemoveCommand.ExecuteAsync(null);

        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
        Assert.Single(await harness.Services.InstalledContent.ListAsync(instance.InstanceId));
    }

    [Fact]
    public async Task DisableMod_ARecordedVehicleRequires_AsksFirst_AndDisablesOnConfirm()
    {
        using var harness = await VehicleHarnessAsync(dependencies: Required("AdvancedFlightComputer"));
        var viewModel = harness.ViewModel;
        var instance = await InstanceWithAfcAndRoverAsync(harness);
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.SelectMany(group => group.Items).Single(item => item.ModId == "AdvancedFlightComputer");

        row.IsEnabled = false;
        await row.ToggleEnabledCommand.ExecuteAsync(null);

        Assert.True(row.IsConfirmingDisable);
        Assert.True(row.IsEnabled);
        Assert.Equal($"{harness.Localization.FormatContentNeededByVehicle("Example Rover")} {harness.Localization.ContentDisableConfirm}", row.DisableConfirmText);
        Assert.True(await harness.Services.ModState.IsActiveAsync(instance.InstanceId, "AdvancedFlightComputer"));

        await row.ConfirmDisableCommand.ExecuteAsync(null);

        Assert.False(row.IsConfirmingDisable);
        Assert.False(await harness.Services.ModState.IsActiveAsync(instance.InstanceId, "AdvancedFlightComputer"));
    }

    [Fact]
    public async Task DisableMod_NoRecordedItemNeeds_DisablesAtOnce()
    {
        using var harness = await VehicleHarnessAsync(dependencies: Required("AdvancedFlightComputer"));
        var viewModel = harness.ViewModel;
        var instance = await InstanceWithAfcAndRoverAsync(harness);
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, ownership: ModInstallOwnership.Borea, into: (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.SelectMany(group => group.Items).Single(item => item.ModId == "MeasureTools");

        row.IsEnabled = false;
        await row.ToggleEnabledCommand.ExecuteAsync(null);

        Assert.False(row.IsConfirmingDisable);
        Assert.Null(row.NeededByText);
        Assert.False(await harness.Services.ModState.IsActiveAsync(instance.InstanceId, "MeasureTools"));
    }

    /// <summary>An active instance with AdvancedFlightComputer 0.7.5 from Borea and the recorded Example Rover 1.0.0.</summary>
    private static async Task<Instance> InstanceWithAfcAndRoverAsync(ViewModelHarness harness)
    {
        var instance = await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea);
        var release = (await harness.Services.UserContent.GetReleaseAsync("ExampleRover", ModVersion.Parse("1.0.0")))!;
        Assert.True((await harness.Services.UserContentInstaller.InstallAsync(instance.InstanceId, release, name: null)).IsDone);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        return instance;
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
    internal static Task<ViewModelHarness> VehicleHarnessAsync(
        Func<string, string>? editSnapshot = null,
        string dependencies = "",
        Func<Borea.Composition.BoreaServices, Task>? seed = null,
        Func<Borea.Core.Planning.IInstallPlanner, Borea.Core.Planning.IInstallPlanner>? wrapInstallPlanner = null)
    {
        var rover = RoverArchive();
        var entry = EntryNeeding("ExampleRover", "vehicle", "Example Rover", RoverUrl, rover, dependencies, ["1.0.0", "1.1.0"])
            .Replace("\"id\": \"ExampleRover\",", "\"id\": \"ExampleRover\", \"downloads\": { \"total\": 42, \"hosts\": { \"github\": 42 } },", StringComparison.Ordinal);
        return ViewModelHarness.CreateAsync(
            seed: seed,
            respond: request => request.RequestUri?.AbsoluteUri == RoverUrl ? ArchiveResponse(rover) : null,
            editSnapshot: editSnapshot,
            wrapInstallPlanner: wrapInstallPlanner,
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
