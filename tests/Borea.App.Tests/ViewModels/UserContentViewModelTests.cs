using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Borea.App.ViewModels;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Mods;
using Borea.Core.Sharing;
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
        Assert.Equal("Where to find Example Rover 2", Assert.Single(row.InstallStepsNotice).Heading);
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

    [Theory]
    [InlineData("ExampleRover", "Example Rover", "Press Esc and choose LAUNCH VEHICLE.", "Pick Example Rover under Vehicle Save.")]
    [InlineData("CampaignSave", "Campaign", "Press Esc and choose SAVE/LOAD.", "Double-click Campaign. The game asks once before it loads the save.")]
    public async Task Install_New_LeavesWhereItIsInTheGame_UntilDismiss(string id, string name, string menu, string pick)
    {
        var rover = RoverArchive();
        var campaign = SaveArchive("<Universe />");
        var types = new Dictionary<string, string>(TypeFiles(ContentIndexTypeFiles.Vehicles, Entry("ExampleRover", "vehicle", "Example Rover", RoverUrl, rover, "1.0.0")));
        foreach (var (file, content) in TypeFiles(ContentIndexTypeFiles.Saves, SaveEntry(("1.0.0", campaign))))
            types[file] = content;
        using var harness = await ViewModelHarness.CreateAsync(
            respond: request => request.RequestUri?.AbsoluteUri switch
            {
                RoverUrl => ArchiveResponse(rover),
                SaveUrl => ArchiveResponse(campaign),
                _ => null,
            },
            typeFiles: types);
        await ActiveInstanceAsync(harness);
        var row = harness.ViewModel.IndexListing(id)!;

        await row.InstallCommand.ExecuteAsync(null);
        Assert.False(row.HasInstallStepsNotice);
        await row.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Null(row.InstallError);
        var notice = Assert.Single(row.InstallStepsNotice);
        Assert.Equal($"Where to find {name}", notice.Heading);
        Assert.Equal(["1.", "2.", "3."], notice.Steps.Select(step => step.Number));
        Assert.Equal(["Start the game from Main.", menu, pick], notice.Steps.Select(step => step.Text));
        Assert.Equal("Play Main", row.InstalledPlayText);

        row.DismissInstallStepsNoticeCommand.Execute(null);

        Assert.False(row.HasInstallStepsNotice);
        Assert.Null(row.InstalledPlayText);
    }

    [Fact]
    public async Task Update_OfARecordedVehicle_LeavesNoSteps()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        await ActiveInstanceWithRoverAsync(harness);
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = Assert.Single(viewModel.VehiclesSection.Items);

        await row.UpdateCommand.ExecuteAsync(null);
        await row.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Equal(ModVersion.Parse("1.1.0"), Assert.Single(await harness.Services.InstalledContent.ListAsync(viewModel.ActiveInstance!.InstanceId)).Version);
        Assert.False(viewModel.IndexListing("ExampleRover")!.HasInstallStepsNotice);
    }

    [Fact]
    public async Task Remove_OfANewVehicle_TakesItsStepsAway()
    {
        using var harness = await VehicleHarnessAsync();
        await ActiveInstanceAsync(harness);
        var row = harness.ViewModel.IndexListing("ExampleRover")!;
        await row.InstallCommand.ExecuteAsync(null);
        await row.ConfirmInstallCommand.ExecuteAsync(null);
        Assert.True(row.HasInstallStepsNotice);
        var changed = new ConcurrentQueue<string?>();
        row.PropertyChanged += (_, e) => changed.Enqueue(e.PropertyName);

        row.BeginRemoveCommand.Execute(null);
        await row.ConfirmRemoveCommand.ExecuteAsync(null);

        Assert.Contains(nameof(DiscoverItem.HasInstallStepsNotice), changed);
        Assert.False(row.HasInstallStepsNotice);
    }

    [Fact]
    public async Task Rename_OfANewVehicle_TheStepsUseTheNewName()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        await ActiveInstanceAsync(harness);
        var row = viewModel.IndexListing("ExampleRover")!;
        await row.InstallCommand.ExecuteAsync(null);
        await row.ConfirmInstallCommand.ExecuteAsync(null);
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        Assert.Single(viewModel.VehiclesSection.Items).BeginRenameCommand.Execute(null);
        viewModel.ModalInstanceName = "Rover 2";
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);

        Assert.Null(viewModel.InstanceError);
        Assert.Equal("Where to find Rover 2", Assert.Single(row.InstallStepsNotice).Heading);
        Assert.Equal("Rover 2", Assert.Single(viewModel.VehiclesSection.Items).FolderName);
    }

    [Fact]
    public async Task Play_OfTheSteps_StartsTheInstanceThatGotTheItem_AndWaitsForTheGameToEnd()
    {
        var starter = new GameCrashToastTests.GameStarter();
        using var harness = await VehicleHarnessAsync(seed: GameSavesViewModelTests.WithStarMap, processStarter: starter);
        var viewModel = harness.ViewModel;
        var instance = await ActiveInstanceAsync(harness);
        starter.GameLog = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        var row = viewModel.IndexListing("ExampleRover")!;
        await row.InstallCommand.ExecuteAsync(null);
        await row.ConfirmInstallCommand.ExecuteAsync(null);
        var other = (await harness.Services.Instances.CreateAsync("Other", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(other.InstanceId);
        await viewModel.LoadAsync();
        Assert.True(row.CanPlayInstalled);
        var changed = new ConcurrentQueue<string?>();
        row.PropertyChanged += (_, e) => changed.Enqueue(e.PropertyName);

        await row.PlayInstalledCommand.ExecuteAsync(null);

        Assert.True(harness.Services.Launcher.IsRunning(instance.InstanceId));
        Assert.False(harness.Services.Launcher.IsRunning(other.InstanceId));
        Assert.Contains(nameof(DiscoverItem.CanPlayInstalled), changed);
        Assert.False(row.CanPlayInstalled);
        changed.Clear();

        starter.Exit(0);
        await viewModel.WhenGameExitCheckedAsync().WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Contains(nameof(DiscoverItem.CanPlayInstalled), changed);
        Assert.True(row.CanPlayInstalled);
    }

    [Fact]
    public async Task Play_OfTheSteps_RefusedLaunch_SaysWhyInAToast_WhereThePageHasNoLaunchLine()
    {
        using var harness = await VehicleHarnessAsync(seed: GameSavesViewModelTests.WithStarMap, loaderLauncher: new LaunchFailureTests.FixedLauncher(LaunchResult.Failed(LaunchOutcome.GameBuildMissing, "English text.")));
        var viewModel = harness.ViewModel;
        await ActiveInstanceAsync(harness);
        var row = viewModel.IndexListing("ExampleRover")!;
        await row.InstallCommand.ExecuteAsync(null);
        await row.ConfirmInstallCommand.ExecuteAsync(null);

        viewModel.SetMainWindowHome();
        await row.PlayInstalledCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasLaunchFailed);
        Assert.DoesNotContain(viewModel.Toasts.Items, toast => toast.IsFailed);

        viewModel.SetMainWindowDiscover();
        await row.PlayInstalledCommand.ExecuteAsync(null);

        Assert.Equal(viewModel.LaunchMessage, Assert.Single(viewModel.Toasts.Items, toast => toast.IsFailed).Message);
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
    public async Task InstancePage_RecordedRow_LinkReadsViewPageOnlyWhileTheNamesMatch()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        await ActiveInstanceWithRoverAsync(harness);
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        Assert.Equal(harness.Localization.GameSaveViewPage, Assert.Single(viewModel.VehiclesSection.Items).ListingLinkText);

        Assert.Single(viewModel.VehiclesSection.Items).BeginRenameCommand.Execute(null);
        viewModel.ModalInstanceName = "Rover 2";
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);

        Assert.Equal("Example Rover", Assert.Single(viewModel.VehiclesSection.Items).ListingLinkText);
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
    public async Task InstancePage_Open_ShowsTheNewestHostOfTheFolderOnlyOnARowOfItsType()
    {
        using var harness = await VehicleHarnessAsync();
        var instance = await ActiveInstanceAsync(harness);
        foreach (var name in new[] { "Shared", "Mine" })
        {
            var folder = Directory.CreateDirectory(Path.Combine(harness.Services.Paths.GetInstanceVehiclesFolder(instance.InstanceId), name)).FullName;
            File.WriteAllText(Path.Combine(folder, "meta.toml"), $"name = \"{name}\"\n");
        }

        var older = new SharedContentEntry(ContentType.Vehicle, "ksamods", "ksamods.gg", "Shared", "SHARED", "1.0.0", null);
        await harness.Services.SharedContent.RecordAsync(older, author: null);
        await harness.Services.SharedContent.RecordAsync(older with { HostKey = "ownhost", HostName = "own.example" }, author: null);
        await harness.Services.SharedContent.RecordAsync(older with { Type = ContentType.Save, FolderName = "Mine" }, author: null);

        await harness.ViewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        var rows = harness.ViewModel.VehiclesSection.Items;
        Assert.Equal(harness.Localization.FormatContentSharedOn("own.example"), rows.Single(row => row.FolderName == "Shared").SharedOnText);
        Assert.Null(rows.Single(row => row.FolderName == "Mine").SharedOnText);
    }

    [Theory]
    [InlineData(1, "a.xml")]
    [InlineData(3, "a.xml, b.xml, c.xml")]
    [InlineData(4, "a.xml, b.xml, c.xml and 1 more")]
    [InlineData(6, "a.xml, b.xml, c.xml and 3 more")]
    public async Task ShortChangedFilesText_NamesThreeFilesAndCountsTheRest(int count, string expected)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        string[] files = ["a.xml", "b.xml", "c.xml", "d.xml", "e.xml", "f.xml"];

        var text = harness.ViewModel.ShortChangedFilesText(new InstalledContentChanges(files[..1], [], files[1..count]));

        Assert.Equal(expected, text);
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
    public async Task InstancePage_NewerVehicle_CountsInTheHints_UntilItsUpdateEndsWithAToast()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        await ActiveInstanceWithRoverAsync(harness);
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();
        Assert.True(viewModel.HasUpdates);
        Assert.Equal(1, viewModel.ActiveInstanceUpdateCount);
        var row = Assert.Single(viewModel.VehiclesSection.Items);

        await row.UpdateCommand.ExecuteAsync(null);
        await row.ConfirmInstallCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();

        Assert.False(viewModel.HasUpdates);
        Assert.Equal(0, viewModel.ActiveInstanceUpdateCount);
        Assert.Equal(harness.Localization.FormatToastUpdated("Example Rover", "1.1.0", "Main"), Assert.Single(viewModel.Toasts.Items).Message);
    }

    [Fact]
    public async Task Home_RecordedVehicleWhoseFolderIsGone_DoesNotCount()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        var instance = await ActiveInstanceWithRoverAsync(harness);
        Directory.Delete(Path.Combine(harness.Services.Paths.GetInstanceVehiclesFolder(instance.InstanceId), "Example Rover"), recursive: true);

        await viewModel.LoadAsync();
        await viewModel.WhenContentUpdatesCheckedAsync();

        Assert.Equal(0, viewModel.ActiveInstanceUpdateCount);
    }

    [Fact]
    public async Task UpdateAll_OnlyVehicles_UpdatesEachWithItsToast()
    {
        using var harness = await TwoRoversOnAKnownGameAsync();
        var viewModel = harness.ViewModel;
        var instance = viewModel.ActiveInstance!;
        await instance.OpenCommand.ExecuteAsync(null);

        await viewModel.UpdateAll!.UpdateCommand.ExecuteAsync(null);

        Assert.Null(viewModel.UpdateAll!.ProgressStatus);
        Assert.Equal(["1.1.0", "1.1.0"], (await harness.Services.InstalledContent.ListAsync(instance.InstanceId)).Select(record => record.Version.ToString()));
        Assert.Equal(
            [harness.Localization.FormatToastUpdated("Alpha Rover", "1.1.0", instance.Name), harness.Localization.FormatToastUpdated("Example Rover", "1.1.0", instance.Name)],
            viewModel.Toasts.Items.Select(toast => toast.Message));
        Assert.False(viewModel.HasUpdates);
    }

    [Fact]
    public async Task UpdateAll_ChangedVehicle_WaitsOnItsRowWhileTheOtherUpdates()
    {
        using var harness = await TwoRoversOnAKnownGameAsync();
        var viewModel = harness.ViewModel;
        var instance = viewModel.ActiveInstance!;
        var file = Path.Combine(harness.Services.Paths.GetInstanceVehiclesFolder(instance.InstanceId), "Alpha Rover", "vehicle.xml");
        File.WriteAllText(file, "<Vehicle edited=\"yes\" />");
        await instance.OpenCommand.ExecuteAsync(null);

        await viewModel.UpdateAll!.UpdateCommand.ExecuteAsync(null);

        var waiting = viewModel.VehiclesSection.Items.Single(row => row.FolderName == "Alpha Rover");
        Assert.True(waiting.IsConfirmingInstall);
        Assert.Equal("<Vehicle edited=\"yes\" />", File.ReadAllText(file));
        Assert.Equal(ModVersion.Parse("1.1.0"), viewModel.VehiclesSection.Items.Single(row => row.FolderName == "Example Rover").Record!.Version);

        await waiting.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Equal("<Vehicle />", File.ReadAllText(file));
    }

    [Fact]
    public async Task UpdateAll_RefusedVehicle_KeepsItsErrorWhileTheOtherUpdates()
    {
        using var harness = await TwoRoversOnAKnownGameAsync(alphaDependencies: $$"""{ "id": "{{ViewModelHarness.FakeSpaceDock.OwnId}}", "kind": "conflict", "source": "authored" }""");
        var viewModel = harness.ViewModel;
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        await viewModel.UpdateAll!.UpdateCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.VehiclesSection.Items.Single(row => row.FolderName == "Alpha Rover").InstallError);
        Assert.Equal(ModVersion.Parse("1.1.0"), viewModel.VehiclesSection.Items.Single(row => row.FolderName == "Example Rover").Record!.Version);
    }

    [Fact]
    public async Task UpdateAll_ModsAndAVehicle_TheModalNamesBoth_AndTheGoUpdatesTheVehicleAfterTheMods()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: request => ModAndRoverDownload(request), typeFiles: ModAndRoverTypeFiles());
        var viewModel = harness.ViewModel;
        var instance = await ModAndRoverAsync(harness);
        var updateAll = viewModel.UpdateAll!;

        await updateAll.UpdateCommand.ExecuteAsync(null);

        Assert.True(updateAll.IsConfirmingUpdate);
        var change = Assert.Single(updateAll.RecordedChanges);
        Assert.Equal("Example Rover 1.0.0 1.1.0", $"{change.Name} {change.FromVersion} {change.ToVersion}");
        Assert.Equal(harness.Localization.FormatUpdateAllTitleWithRecorded(instance.Name), updateAll.TitleText);
        Assert.Equal(harness.Localization.UpdateAnyway, updateAll.ConfirmUpdateText);
        Assert.Equal(ModVersion.Parse("1.0.0"), Assert.Single(await harness.Services.InstalledContent.ListAsync(instance.InstanceId)).Version);

        await updateAll.ConfirmUpdateCommand.ExecuteAsync(null);

        Assert.Equal(ModVersion.Parse("1.1.0"), Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods).Version);
        await Assert.Single(viewModel.VehiclesSection.Items).ConfirmInstallCommand.ExecuteAsync(null);
        Assert.Equal(ModVersion.Parse("1.1.0"), Assert.Single(await harness.Services.InstalledContent.ListAsync(instance.InstanceId)).Version);
    }

    [Fact]
    public async Task UpdateAll_StopDuringTheMods_LeavesTheVehicleAlone()
    {
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = await ViewModelHarness.CreateAsync(
            respond: request => request.RequestUri?.Host == "archives.test" ? Stalled(reading) : ModAndRoverDownload(request),
            typeFiles: ModAndRoverTypeFiles());
        var viewModel = harness.ViewModel;
        var instance = await ModAndRoverAsync(harness);
        var updateAll = viewModel.UpdateAll!;
        await updateAll.UpdateCommand.ExecuteAsync(null);

        var update = updateAll.ConfirmUpdateCommand.ExecuteAsync(null);
        await reading.Task.WaitAsync(GateTimeout);
        updateAll.Run!.StopCommand.Execute(null);
        await update.WaitAsync(GateTimeout);

        Assert.NotNull(viewModel.UpdateAll!.ProgressStatus);
        Assert.False(Assert.Single(viewModel.VehiclesSection.Items).IsConfirmingInstall);
        Assert.Equal(ModVersion.Parse("1.0.0"), Assert.Single(await harness.Services.InstalledContent.ListAsync(instance.InstanceId)).Version);
    }

    [Fact]
    public async Task UpdateAll_RecordedSave_CountsOnHomeAndUpdates()
    {
        var first = SaveArchive("<Universe />");
        var second = SaveArchive("<Universe version=\"2\" />");
        var served = first;
        using var harness = await ViewModelHarness.CreateAsync(
            respond: request => request.RequestUri?.AbsoluteUri == SaveUrl ? ArchiveResponse(served) : null,
            typeFiles: TypeFiles(ContentIndexTypeFiles.Saves, SaveEntry(("1.0.0", first), ("2.0.0", second))));
        var viewModel = harness.ViewModel;
        var instance = await ActiveInstanceAsync(harness);
        Assert.True((await harness.Services.UserContentInstaller.InstallAsync(instance.InstanceId, (await harness.Services.UserContent.GetReleaseAsync("CampaignSave", ModVersion.Parse("1.0.0")))!, name: null)).IsDone);
        served = second;
        await viewModel.LoadAsync();
        await viewModel.WhenContentUpdatesCheckedAsync();
        Assert.Equal(1, viewModel.ActiveInstanceUpdateCount);

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.UpdateAll!.UpdateCommand.ExecuteAsync(null);
        await Assert.Single(viewModel.SavesSection.Items).ConfirmInstallCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();

        Assert.Equal(ModVersion.Parse("2.0.0"), Assert.Single(await harness.Services.InstalledContent.ListAsync(instance.InstanceId)).Version);
        Assert.Equal(0, viewModel.ActiveInstanceUpdateCount);
    }

    [Fact]
    public async Task Hints_LeaveOutAReleaseThatAPlanCannotPick_AndOfferTheNewestThatItCan()
    {
        const string BetaRelease = "\"id\": \"AlphaRover\", \"type\": \"vehicle\", \"version\": \"1.3.0\", \"version_scheme\": \"semver\", \"release_status\": \"stable\"";
        using var harness = await TwoRoversOnAKnownGameAsync(
            alphaVersions: ["1.0.0", "1.1.0", "1.2.0", "1.3.0", "1.4.0"],
            editEntries: entries => DownloadIsGone(NeedsANewerGame(NeedsANewerGame(entries, "AlphaRover", "1.4.0"), "ExampleRover", "1.1.0"), "AlphaRover", "1.2.0")
                .Replace(BetaRelease, BetaRelease.Replace("stable", "beta", StringComparison.Ordinal), StringComparison.Ordinal));
        var viewModel = harness.ViewModel;
        await viewModel.WhenContentUpdatesCheckedAsync();
        Assert.Equal(1, viewModel.ActiveInstanceUpdateCount);

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        Assert.Equal(ModVersion.Parse("1.1.0"), RoverRow(viewModel, "Alpha Rover").NewerRelease!.Version);
        Assert.Null(RoverRow(viewModel, "Example Rover").NewerRelease);
    }

    [Fact]
    public async Task UpdateOfARow_HoldsTheSlot_SoAReopenKeepsTheRowAndUpdateAllStartsNoSecondRun()
    {
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stall = false;
        using var harness = await TwoRoversOnAKnownGameAsync(holdDownload: url => stall && url == AlphaUrl ? Stalled(reading) : null);
        var viewModel = harness.ViewModel;
        var instance = viewModel.ActiveInstance!;
        await instance.OpenCommand.ExecuteAsync(null);
        var alpha = RoverRow(viewModel, "Alpha Rover");
        stall = true;

        var update = alpha.UpdateCommand.ExecuteAsync(null);
        await reading.Task.WaitAsync(GateTimeout);
        Assert.False(viewModel.CanChangeContent);
        await viewModel.UpdateAll!.UpdateCommand.ExecuteAsync(null).WaitAsync(GateTimeout);
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        Assert.Same(alpha, RoverRow(viewModel, "Alpha Rover"));
        Assert.False(alpha.CanUpdate);
        alpha.Run!.StopCommand.Execute(null);
        await update.WaitAsync(GateTimeout);

        Assert.True(viewModel.CanChangeContent);
        Assert.True(RoverRow(viewModel, "Alpha Rover").CanUpdate);
        Assert.Equal(["1.0.0", "1.0.0"], (await harness.Services.InstalledContent.ListAsync(instance.InstanceId)).Select(record => record.Version.ToString()));
    }

    [Fact]
    public async Task UpdateAll_TheGoOfAWaitingRowWaitsForTheRun_AndAStopAsksNothingMore()
    {
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stall = false;
        using var harness = await TwoRoversOnAKnownGameAsync(holdDownload: url => stall && url == RoverUrl ? Stalled(reading) : null);
        var viewModel = harness.ViewModel;
        var instance = viewModel.ActiveInstance!;
        var file = Path.Combine(harness.Services.Paths.GetInstanceVehiclesFolder(instance.InstanceId), "Alpha Rover", "vehicle.xml");
        File.WriteAllText(file, "<Vehicle edited=\"yes\" />");
        await instance.OpenCommand.ExecuteAsync(null);
        stall = true;

        var all = viewModel.UpdateAll!.UpdateCommand.ExecuteAsync(null);
        await reading.Task.WaitAsync(GateTimeout);
        var waiting = RoverRow(viewModel, "Alpha Rover");
        Assert.True(waiting.IsConfirmingInstall);
        await waiting.ConfirmInstallCommand.ExecuteAsync(null).WaitAsync(GateTimeout);
        RoverRow(viewModel, "Example Rover").Run!.StopCommand.Execute(null);
        await all.WaitAsync(GateTimeout);

        Assert.Equal("<Vehicle edited=\"yes\" />", File.ReadAllText(file));
        Assert.False(RoverRow(viewModel, "Alpha Rover").IsConfirmingInstall);
    }

    [Fact]
    public async Task UpdateAll_StopOnARow_LeavesTheNextRowAlone()
    {
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stall = false;
        using var harness = await TwoRoversOnAKnownGameAsync(holdDownload: url => stall && url == AlphaUrl ? Stalled(reading) : null);
        var viewModel = harness.ViewModel;
        var instance = viewModel.ActiveInstance!;
        await instance.OpenCommand.ExecuteAsync(null);
        stall = true;

        var all = viewModel.UpdateAll!.UpdateCommand.ExecuteAsync(null);
        await reading.Task.WaitAsync(GateTimeout);
        var running = RoverRow(viewModel, "Alpha Rover");
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        Assert.Same(running, RoverRow(viewModel, "Alpha Rover"));
        running.Run!.StopCommand.Execute(null);
        await all.WaitAsync(GateTimeout);

        Assert.Equal(["1.0.0", "1.0.0"], (await harness.Services.InstalledContent.ListAsync(instance.InstanceId)).Select(record => record.Version.ToString()));
    }

    [Fact]
    public async Task UpdateAll_WhileARowWaits_AnotherClickAsksAgain()
    {
        using var harness = await TwoRoversOnAKnownGameAsync();
        var viewModel = harness.ViewModel;
        var instance = viewModel.ActiveInstance!;
        var file = Path.Combine(harness.Services.Paths.GetInstanceVehiclesFolder(instance.InstanceId), "Alpha Rover", "vehicle.xml");
        File.WriteAllText(file, "<Vehicle edited=\"yes\" />");
        await instance.OpenCommand.ExecuteAsync(null);
        await viewModel.UpdateAll!.UpdateCommand.ExecuteAsync(null);
        Assert.True(RoverRow(viewModel, "Alpha Rover").IsConfirmingInstall);

        await viewModel.UpdateAll!.UpdateCommand.ExecuteAsync(null);

        Assert.Null(viewModel.UpdateAll!.ProgressStatus);
        Assert.True(RoverRow(viewModel, "Alpha Rover").IsConfirmingInstall);
        Assert.Equal("<Vehicle edited=\"yes\" />", File.ReadAllText(file));
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

    private const string AlphaUrl = "https://example.test/alpha.zip";

    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(30);

    private static GameSaveItem RoverRow(MainViewModel viewModel, string folder) => viewModel.VehiclesSection.Items.Single(row => row.FolderName == folder);

    /// <summary>A download that sends nothing until its read is canceled, which a stop does.</summary>
    private static HttpResponseMessage Stalled(TaskCompletionSource reading) =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new InstallStopTests.StalledBody(reading)) };

    /// <summary>Makes one release of a fixture on a known game need a newer game than the fixture has.</summary>
    private static string NeedsANewerGame(string entries, string id, string version) =>
        Regex.Replace(entries, $"(\"id\": \"{id}\", \"type\": \"vehicle\", \"version\": \"{Regex.Escape(version)}\".*?\"game_min\": \")2026\\.8\\.3\\.5117(\", \"game_min_revision\": )5117", "${1}2026.8.19.5261${2}5261", RegexOptions.Singleline);

    private static string DownloadIsGone(string entries, string id, string version) =>
        Regex.Replace(entries, $"(\"id\": \"{id}\", \"type\": \"vehicle\", \"version\": \"{Regex.Escape(version)}\".*?\"download\": \\{{ )", "$1\"unavailable_since\": \"2026-09-23T10:24:00Z\", ", RegexOptions.Singleline);

    /// <summary>One archive for the hash in the index and for its download, because each new archive has the time it was made.</summary>
    private static readonly byte[] SharedRover = RoverArchive();

    private static HttpResponseMessage? ModAndRoverDownload(HttpRequestMessage request) =>
        request.RequestUri?.AbsoluteUri == RoverUrl ? ArchiveResponse(SharedRover) : ContentUpdateTests.ServeArchive(request);

    private static Dictionary<string, string> ModAndRoverTypeFiles() =>
        TypeFiles(ContentIndexTypeFiles.Vehicles, Entry("ExampleRover", "vehicle", "Example Rover", RoverUrl, SharedRover, "1.0.0", "1.1.0"));

    /// <summary>Opens an active instance with a mod at 1.0.0 that has 1.1.0 on SpaceDock, and the recorded Example Rover 1.0.0.</summary>
    private static async Task<Instance> ModAndRoverAsync(ViewModelHarness harness)
    {
        harness.SpaceDock.Releases.AddRange([ContentUpdateTests.Release("1.0.0"), ContentUpdateTests.Release("1.1.0")]);
        var instance = await InstalledContent.AddAsync(harness, ViewModelHarness.FakeSpaceDock.OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        var release = (await harness.Services.UserContent.GetReleaseAsync("ExampleRover", ModVersion.Parse("1.0.0")))!;
        Assert.True((await harness.Services.UserContentInstaller.InstallAsync(instance.InstanceId, release, name: null)).IsDone);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        return instance;
    }

    /// <summary>An active instance with a mod from SpaceDock and the recorded Alpha Rover and Example Rover 1.0.0, each with 1.1.0 in the index. The game is known, so an update asks nothing by itself, and the later time of Alpha Rover puts its row first.</summary>
    /// <param name="holdDownload">Answers the download of an address in place of the archive, when it gives a response.</param>
    private static async Task<ViewModelHarness> TwoRoversOnAKnownGameAsync(string alphaDependencies = "", IReadOnlyList<string>? alphaVersions = null, Func<string, string>? editEntries = null, Func<string, HttpResponseMessage?>? holdDownload = null)
    {
        var alpha = Archive(("Alpha Rover/meta.toml", "name = \"Alpha Rover\"\nupdated = 2099-01-01T00:00:00\n"), ("Alpha Rover/vehicle.xml", "<Vehicle />"));
        var rover = RoverArchive();
        var entries = $"{EntryNeeding("AlphaRover", "vehicle", "Alpha Rover", AlphaUrl, alpha, alphaDependencies, alphaVersions ?? ["1.0.0", "1.1.0"])}, {Entry("ExampleRover", "vehicle", "Example Rover", RoverUrl, rover, "1.0.0", "1.1.0")}";
        var harness = await ViewModelHarness.CreateAsync(
            seed: services =>
            {
                var game = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Game")).FullName;
                File.Copy(Path.Combine(AppContext.BaseDirectory, "GameVersionFixture.dll"), Path.Combine(game, "KSA.dll"));
                return services.SettingsRepository.SaveAsync(services.Settings.WithGameDirectory(game));
            },
            respond: request => holdDownload?.Invoke(request.RequestUri?.AbsoluteUri ?? string.Empty) ?? request.RequestUri?.AbsoluteUri switch
            {
                AlphaUrl => ArchiveResponse(alpha),
                RoverUrl => ArchiveResponse(rover),
                _ => null,
            },
            typeFiles: TypeFiles(ContentIndexTypeFiles.Vehicles, (editEntries ?? (text => text))(entries.Replace("\"game_min\": \"2026.8.19.5261\", \"game_min_revision\": 5261", "\"game_min\": \"2026.8.3.5117\", \"game_min_revision\": 5117", StringComparison.Ordinal))));
        harness.SpaceDock.Releases.Add(ContentUpdateTests.Release("1.0.0"));
        var instance = await InstalledContent.AddAsync(harness, ViewModelHarness.FakeSpaceDock.OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        foreach (var id in new[] { "AlphaRover", "ExampleRover" })
            Assert.True((await harness.Services.UserContentInstaller.InstallAsync(instance.InstanceId, (await harness.Services.UserContent.GetReleaseAsync(id, ModVersion.Parse("1.0.0")))!, name: null)).IsDone);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        return harness;
    }

    /// <summary>The vehicle Example Rover with releases 1.0.0 and 1.1.0, served, with 42 downloads.</summary>
    internal static Task<ViewModelHarness> VehicleHarnessAsync(
        Func<string, string>? editSnapshot = null,
        string dependencies = "",
        Func<Borea.Composition.BoreaServices, Task>? seed = null,
        Func<Borea.Core.Planning.IInstallPlanner, Borea.Core.Planning.IInstallPlanner>? wrapInstallPlanner = null,
        Borea.Storage.Launch.IProcessStarter? processStarter = null,
        ILauncher? loaderLauncher = null,
        Func<string, string>? editEntry = null)
    {
        var rover = RoverArchive();
        var entry = EntryNeeding("ExampleRover", "vehicle", "Example Rover", RoverUrl, rover, dependencies, ["1.0.0", "1.1.0"])
            .Replace("\"id\": \"ExampleRover\",", "\"id\": \"ExampleRover\", \"downloads\": { \"total\": 42, \"hosts\": { \"github\": 42 } },", StringComparison.Ordinal);
        entry = editEntry?.Invoke(entry) ?? entry;
        return ViewModelHarness.CreateAsync(
            seed: seed,
            respond: request => request.RequestUri?.AbsoluteUri == RoverUrl ? ArchiveResponse(rover) : null,
            editSnapshot: editSnapshot,
            wrapInstallPlanner: wrapInstallPlanner,
            processStarter: processStarter,
            loaderLauncher: loaderLauncher,
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
