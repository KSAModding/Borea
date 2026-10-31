using System.Text.Json.Nodes;
using Borea.App.ViewModels;
using Borea.Core.Game;
using Borea.Core.History;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Preview;
using Borea.Core.Settings;
using Borea.Storage.Launch;
using static Borea.App.Tests.ViewModels.UserContentFixtures;

namespace Borea.App.Tests.ViewModels;

public sealed class InstanceGameBuildTests
{
    // the file version 0.4.6.0 of this fixture reads as a game build of revision 0, older than the settings' 2026.8.3.5117
    private const string OlderBuildFixture = "LoaderVersionFixture.dll";

    private static readonly GameVersion KeptBuild = GameVersion.Parse("0.4.6.0");

    [Fact]
    public async Task Discover_ChecksTheBuildOfTheActiveInstance()
    {
        using var harness = await CreateWithKeptBuildAsync();
        var kept = await CreateInstanceAsync(harness, "Kept", KeptBuild);
        var current = await CreateInstanceAsync(harness, "Current", null);
        await harness.Services.Instances.SetActiveInstanceAsync(kept.InstanceId);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        var row = harness.ViewModel.DiscoverItems.Single(item => item.ModId == "MeasureTools");

        Assert.Equal(GameCompatibility.Incompatible, row.Compatibility);

        await harness.ViewModel.ActivateInstanceAsync(current.InstanceId);

        Assert.Equal(GameCompatibility.Compatible, row.Compatibility);
    }

    [Fact]
    public async Task Install_IntoAKeptBuild_PlansTheNewestReleaseThatFitsIt()
    {
        using var harness = await CreateWithKeptBuildAsync();
        var kept = await CreateInstanceAsync(harness, "Kept", KeptBuild);
        var current = await CreateInstanceAsync(harness, "Current", null);
        await harness.ViewModel.LoadAsync();
        var newest = (await harness.Services.Mods.GetLatestReleaseAsync("MeasureTools"))!;
        var intoKept = new VersionItem(harness.ViewModel, newest);
        var intoCurrent = new VersionItem(harness.ViewModel, newest);

        await harness.ViewModel.PlanInstallAsync(intoKept, () => Task.FromResult<ModVersionMetadata?>(newest), exactVersion: null, kept.InstanceId, confirm: true);
        await harness.ViewModel.PlanInstallAsync(intoCurrent, () => Task.FromResult<ModVersionMetadata?>(newest), exactVersion: null, current.InstanceId, confirm: true);

        Assert.Equal("1.1.9", intoKept.PendingPlan!.Operations.Single().Release.Version.ToString());
        Assert.Equal("1.1.10", intoCurrent.PendingPlan!.Operations.Single().Release.Version.ToString());
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(kept.InstanceId))!.Mods);
    }

    [Fact]
    public async Task Install_IntoAKeptBuildThatIsGone_IsRefusedWithTheBuild()
    {
        using var harness = await CreateWithKeptBuildAsync();
        var gone = GameVersion.Parse("2026.10.13.5602");
        var instance = await CreateInstanceAsync(harness, "Gone", gone);
        await harness.ViewModel.LoadAsync();
        var newest = (await harness.Services.Mods.GetLatestReleaseAsync("MeasureTools"))!;
        var row = new VersionItem(harness.ViewModel, newest);

        await harness.ViewModel.PlanInstallAsync(row, () => Task.FromResult<ModVersionMetadata?>(newest), exactVersion: null, instance.InstanceId, confirm: true);

        Assert.Equal(harness.Localization.FormatInstallKeptBuildUnavailable("2026.10.13.5602", MainViewModel.KeptBuildFolderText(harness.Services.Paths, gone)), row.InstallError);
        Assert.Null(row.PendingPlan);
    }

    [Fact]
    public async Task InstallVehicle_IntoAKeptBuild_PlansTheNeededModThatFitsIt()
    {
        using var harness = await CreateWithKeptBuildAsync(typeFiles: RoverNeedingMeasureTools());
        var kept = await CreateInstanceAsync(harness, "Kept", KeptBuild);
        var current = await CreateInstanceAsync(harness, "Current", null);
        await harness.ViewModel.LoadAsync();
        var rover = (await harness.Services.UserContent.GetLatestReleaseAsync("ExampleRover", harness.Services.Settings.ReleaseChannel))!;
        var intoKept = new VersionItem(harness.ViewModel, rover);
        var intoCurrent = new VersionItem(harness.ViewModel, rover);

        await harness.ViewModel.PlanContentAsync(intoKept, kept.InstanceId, rover);
        await harness.ViewModel.PlanContentAsync(intoCurrent, current.InstanceId, rover);

        Assert.Equal("1.1.9", intoKept.PendingContentPlan!.Mods.Operations.Single(operation => operation.Release.ModId == "MeasureTools").Release.Version.ToString());
        Assert.Equal("1.1.10", intoCurrent.PendingContentPlan!.Mods.Operations.Single(operation => operation.Release.ModId == "MeasureTools").Release.Version.ToString());
    }

    [Fact]
    public async Task InstallVehicle_NewerThanTheKeptBuild_IsRefusedAndNamesTheBuildItNeeds()
    {
        var entry = Entry("ExampleRover", "vehicle", "Example Rover", RoverUrl, RoverArchive(), "1.0.0")
            .Replace("\"game_min\": \"2026.8.19.5261\", \"game_min_revision\": 5261", "\"game_min\": \"2026.7.1.5000\", \"game_min_revision\": 5000", StringComparison.Ordinal);
        using var harness = await CreateWithKeptBuildAsync(typeFiles: TypeFiles(ContentIndexTypeFiles.Vehicles, entry));
        var kept = await CreateInstanceAsync(harness, "Kept", KeptBuild);
        await harness.ViewModel.LoadAsync();
        var rover = (await harness.Services.UserContent.GetLatestReleaseAsync("ExampleRover", harness.Services.Settings.ReleaseChannel))!;
        var row = new VersionItem(harness.ViewModel, rover);

        await harness.ViewModel.PlanContentAsync(row, kept.InstanceId, rover);

        Assert.Contains("2026.7.1.5000", row.InstallError);
        Assert.Null(row.PendingContentPlan);
    }

    [Fact]
    public async Task InstallVehicle_IntoAKeptBuildThatIsGone_IsRefusedWithTheBuild()
    {
        using var harness = await CreateWithKeptBuildAsync(typeFiles: RoverNeedingMeasureTools());
        var gone = GameVersion.Parse("2026.10.13.5602");
        var instance = await CreateInstanceAsync(harness, "Gone", gone);
        await harness.ViewModel.LoadAsync();
        var rover = (await harness.Services.UserContent.GetLatestReleaseAsync("ExampleRover", harness.Services.Settings.ReleaseChannel))!;
        var row = new VersionItem(harness.ViewModel, rover);

        await harness.ViewModel.PlanContentAsync(row, instance.InstanceId, rover);

        Assert.Equal(harness.Localization.FormatInstallKeptBuildUnavailable("2026.10.13.5602", MainViewModel.KeptBuildFolderText(harness.Services.Paths, gone)), row.InstallError);
        Assert.Null(row.PendingContentPlan);
    }

    [Fact]
    public async Task Discover_ActiveInstanceWithAKeptBuildThatIsGone_RefusesAddAndSaysWhy()
    {
        using var harness = await CreateWithKeptBuildAsync();
        var gone = await CreateInstanceAsync(harness, "Gone", GameVersion.Parse("2026.10.13.5602"));
        var current = await CreateInstanceAsync(harness, "Current", null);
        await harness.Services.Instances.SetActiveInstanceAsync(gone.InstanceId);
        await harness.ViewModel.LoadAsync();

        Assert.False(harness.ViewModel.CanAddToActiveInstance);
        Assert.Equal(harness.Localization.FormatDiscoverKeptBuildUnavailable("Gone", "2026.10.13.5602"), harness.ViewModel.AddToText);
        Assert.Equal(harness.ViewModel.AddToText, harness.ViewModel.ReplaceVersionText("1.1.9"));

        await harness.ViewModel.ActivateInstanceAsync(current.InstanceId);

        Assert.True(harness.ViewModel.CanAddToActiveInstance);
        Assert.Equal(harness.Localization.FormatDiscoverAddTo("Current"), harness.ViewModel.AddToText);
    }

    [Fact]
    public async Task Discover_KeptBuildOfTheActiveInstanceThatGoesAway_DisablesAddWhenTheInstancePageReadsIt()
    {
        using var harness = await CreateWithKeptBuildAsync();
        var viewModel = harness.ViewModel;
        var kept = await CreateInstanceAsync(harness, "Kept", KeptBuild);
        await harness.Services.Instances.SetActiveInstanceAsync(kept.InstanceId);
        await viewModel.LoadAsync();
        // the loader check of the start reads KSA.dll of the kept build after LoadAsync returns, and Windows refuses to delete a file while that read maps it
        await viewModel.WhenLoaderUpdatesCheckedAsync();
        Assert.True(viewModel.CanAddToActiveInstance);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Directory.Delete(harness.Services.Paths.GetGameBuildFolder(KeptBuild), recursive: true);

        await viewModel.OpenInstanceAsync(viewModel.ActiveInstance!);

        Assert.Contains(nameof(MainViewModel.CanAddToActiveInstance), changed);
        Assert.False(viewModel.CanAddToActiveInstance);
        Assert.Equal(harness.Localization.FormatDiscoverKeptBuildUnavailable("Kept", KeptBuild.ToString()), viewModel.AddToText);
    }

    [Fact]
    public async Task Play_OnAKeptBuild_PutsTheContentOfThatBuildFirst()
    {
        var starter = new GameSavesViewModelTests.RunningGameStarter();
        using var harness = await CreateWithKeptBuildAsync(starter);
        var kept = await CreateInstanceAsync(harness, "Kept", KeptBuild);
        starter.GameLogPath = harness.Services.Paths.GetInstanceGameLogPath(kept.InstanceId);
        await harness.Services.Instances.SetActiveInstanceAsync(kept.InstanceId);
        await harness.ViewModel.LoadAsync();

        await harness.ViewModel.LaunchActiveInstanceAsync();

        Assert.True(harness.Services.Launcher.IsRunning(kept.InstanceId));
        Assert.Equal(["KeptContent"], (await harness.Services.ModState.GetEntriesAsync(kept.InstanceId)).Select(entry => entry.ModId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Play_NotesTheUpdatePopupOnlyAfterAStartOnAKeptBuild(bool keepsBuild)
    {
        var starter = new GameSavesViewModelTests.RunningGameStarter();
        using var harness = await CreateWithKeptBuildAsync(starter);
        var instance = await CreateInstanceAsync(harness, "Instance", keepsBuild ? KeptBuild : null);
        starter.GameLogPath = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await harness.ViewModel.LoadAsync();

        await harness.ViewModel.LaunchActiveInstanceAsync();

        Assert.True(harness.Services.Launcher.IsRunning(instance.InstanceId));
        var started = harness.Localization.FormatLaunchStarted("StarMap", "Instance");
        Assert.Equal(keepsBuild ? started + " " + harness.Localization.ModalKeepGameBuildPopupNote : started, harness.ViewModel.LaunchMessage);
    }

    [Fact]
    public async Task Play_WhileAKeptBuildRunsFromTheSameFolder_IsRefusedAndTheFileStays()
    {
        var starter = new GameSavesViewModelTests.RunningGameStarter();
        using var harness = await CreateWithKeptBuildAsync(starter);
        var kept = await CreateInstanceAsync(harness, "Kept", KeptBuild);
        var current = await CreateInstanceAsync(harness, "Current", null);
        var file = await LoaderFileAsync(harness);
        await harness.ViewModel.LoadAsync();
        // both instances start the one StarMap folder, so the start of the kept build leaves its folder in the file
        starter.GameLogPath = harness.Services.Paths.GetInstanceGameLogPath(kept.InstanceId);
        await harness.ViewModel.ActivateInstanceAsync(kept.InstanceId);
        await harness.ViewModel.LaunchActiveInstanceAsync();
        Assert.True(harness.Services.Launcher.IsRunning(kept.InstanceId));
        harness.IsGameProcessRunning = () => true;
        var before = await File.ReadAllBytesAsync(file);
        await harness.ViewModel.ActivateInstanceAsync(current.InstanceId);

        await harness.ViewModel.LaunchActiveInstanceAsync();

        Assert.Equal(harness.Localization.FormatLaunchOtherBuildRunning("StarMap"), harness.ViewModel.LaunchMessage);
        Assert.Equal(before, await File.ReadAllBytesAsync(file));
        Assert.False(harness.Services.Launcher.IsRunning(current.InstanceId));
    }

    [Fact]
    public async Task Play_LoaderFileThatCannotBeRead_SaysSoAndTheFileStays()
    {
        using var harness = await CreateWithKeptBuildAsync(new GameSavesViewModelTests.RunningGameStarter());
        var current = await CreateInstanceAsync(harness, "Current", null);
        var file = await LoaderFileAsync(harness);
        await harness.ViewModel.LoadAsync();
        await File.WriteAllTextAsync(file, "{");
        await harness.ViewModel.ActivateInstanceAsync(current.InstanceId);

        await harness.ViewModel.LaunchActiveInstanceAsync();

        Assert.Equal(harness.Localization.FormatLaunchLoaderConfigurationUnreadable(file, "StarMap"), harness.ViewModel.LaunchMessage);
        Assert.Equal("{", await File.ReadAllTextAsync(file));
        Assert.False(harness.Services.Launcher.IsRunning(current.InstanceId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Play_OnAKeptBuildThatGoesAway_NamesTheBuildAndOffersTheCurrentBuildUntilTheInstanceIsDeleted(bool launcherFindsItGone)
    {
        // the launcher reads the kept build again after the check of the launch, and the folder can go in between
        var launcher = launcherFindsItGone ? new LaunchFailureTests.FixedLauncher(LaunchResult.Failed(LaunchOutcome.GameBuildMissing, "English text.")) : null;
        using var harness = await CreateWithKeptBuildAsync(loaderLauncher: launcher);
        var viewModel = harness.ViewModel;
        var kept = await CreateInstanceAsync(harness, "Kept", KeptBuild);
        if (!launcherFindsItGone)
        {
            // a pin that is not installed would open the loader prompt if the launch went on past the check of the build
            await harness.Services.Instances.UpdateAsync(kept.InstanceId, record =>
            {
                record.SetLoader(LoaderSetting.Pinned("StarMap", ModVersion.Parse("0.4.9")));
                return true;
            });
        }

        await viewModel.LoadAsync();
        await viewModel.ActivateInstanceAsync(kept.InstanceId);
        await viewModel.OpenInstanceAsync(viewModel.Instances.Single(item => item.InstanceId == kept.InstanceId));
        Assert.True(viewModel.SelectedInstance!.ShowsGameBuildChip);
        if (!launcherFindsItGone)
            Directory.Delete(harness.Services.Paths.GetGameBuildFolder(KeptBuild), recursive: true);

        await viewModel.LaunchActiveInstanceAsync();

        Assert.False(viewModel.IsLoaderPromptOpen);
        Assert.Equal(harness.Localization.FormatLaunchGameBuildMissing(KeptBuild.ToString(), Path.Combine("Game Builds", KeptBuild.ToString())), viewModel.LaunchMessage);
        Assert.True(viewModel.HasLaunchFailed);
        Assert.Equal(kept.InstanceId, viewModel.LaunchGameBuildMissingItem?.InstanceId);
        Assert.Equal(harness.Localization.FormatInstanceGameBuildNotFound(KeptBuild.ToString()), viewModel.SelectedInstance!.GameBuildNotFoundText);
        var loader = viewModel.InstanceLoader!.Rows[0];
        Assert.Equal(harness.Localization.CompatibilityUnknown, loader.StatusText);
        Assert.False(loader.CanInstall);
        Assert.False(loader.CanUpdate);

        viewModel.BeginDeleteInstance(viewModel.LaunchGameBuildMissingItem!);
        await viewModel.ConfirmDeleteModalCommand.ExecuteAsync(null);

        Assert.Null(viewModel.LaunchMessage);
        Assert.Null(viewModel.LaunchGameBuildMissingItem);
    }

    [Fact]
    public async Task Play_OnAKeptBuildThatIsBack_ShowsThePinChipAndRatesTheLoaderAgain()
    {
        // the launch ends after the check of the build, so no game starts
        using var harness = await CreateWithKeptBuildAsync(loaderLauncher: new LaunchFailureTests.FixedLauncher(LaunchResult.Failed(LaunchOutcome.OtherBuildRunning, "English text.")));
        var viewModel = harness.ViewModel;
        var kept = await CreateInstanceAsync(harness, "Kept", KeptBuild);
        await viewModel.LoadAsync();
        await viewModel.ActivateInstanceAsync(kept.InstanceId);
        var folder = harness.Services.Paths.GetGameBuildFolder(KeptBuild);
        Directory.Move(folder, folder + ".away");
        await viewModel.OpenInstanceAsync(viewModel.Instances.Single(item => item.InstanceId == kept.InstanceId));
        Assert.Equal(InstanceLoaderState.KeptBuildMissing, viewModel.InstanceLoader!.Rows[0].State);
        Directory.Move(folder + ".away", folder);

        await viewModel.LaunchActiveInstanceAsync();

        Assert.True(viewModel.SelectedInstance!.ShowsGameBuildChip);
        Assert.Equal(InstanceLoaderState.Installed, viewModel.InstanceLoader!.Rows[0].State);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InstancePage_OfAKeptBuild_ShowsTheBuildOrThatItIsNotFound(bool folderThere)
    {
        using var harness = await CreateWithKeptBuildAsync();
        var viewModel = harness.ViewModel;
        var build = folderThere ? KeptBuild : GameVersion.Parse("2026.10.13.5602");
        var kept = await CreateInstanceAsync(harness, "Kept", build);
        await viewModel.LoadAsync();
        var row = viewModel.Instances.Single(item => item.InstanceId == kept.InstanceId);

        await viewModel.OpenInstanceAsync(row);

        Assert.Equal(folderThere, row.ShowsGameBuildChip);
        Assert.Equal(folderThere ? null : harness.Localization.FormatInstanceGameBuildNotFound("2026.10.13.5602"), row.GameBuildNotFoundText);

        await row.BeginUseCurrentGameBuildCommand.ExecuteAsync(null);

        var current = viewModel.UseCurrentGameBuildPreview!.Current!.Value.ToString();
        Assert.Equal(
            folderThere
                ? harness.Localization.FormatModalUseCurrentGameBuildText("Kept", current, build.ToString())
                : harness.Localization.FormatModalUseCurrentGameBuildMissingText("Kept", current, build.ToString()),
            viewModel.UseCurrentGameBuildText);
    }

    [Fact]
    public async Task InstancePage_OfAKeptBuild_NotesTheUpdatePopup()
    {
        using var harness = await CreateWithKeptBuildAsync();
        var kept = await CreateInstanceAsync(harness, "Kept", KeptBuild);
        var current = await CreateInstanceAsync(harness, "Current", null);
        await harness.ViewModel.LoadAsync();

        await harness.ViewModel.OpenInstanceAsync(harness.ViewModel.Instances.Single(item => item.InstanceId == kept.InstanceId));

        Assert.Contains(harness.Localization.ModalKeepGameBuildPopupNote, harness.ViewModel.SelectedInstance!.GameBuildToolTip);

        await harness.ViewModel.OpenInstanceAsync(harness.ViewModel.Instances.Single(item => item.InstanceId == current.InstanceId));

        Assert.Null(harness.ViewModel.SelectedInstance!.GameBuildToolTip);
    }

    [Fact]
    public async Task SaveGameFolder_OnTheGameTab_LoadsTheKeptBuildsAgain()
    {
        using var harness = await CreateWithKeptBuildAsync();
        var viewModel = harness.ViewModel;
        var folder = harness.Services.Paths.GetGameBuildFolder(KeptBuild);
        viewModel.SetMainWindowSettings();
        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);
        await viewModel.WhenKeptGameBuildsLoadedAsync();
        Assert.True(Assert.Single(viewModel.KeptGameBuilds).CanRemove);

        viewModel.GameDirectoryInput = folder;
        await viewModel.SaveGameDirectoryCommand.ExecuteAsync(null);
        await viewModel.WhenKeptGameBuildsLoadedAsync();

        Assert.Null(viewModel.SetupError);
        var row = Assert.Single(viewModel.KeptGameBuilds);
        Assert.False(row.CanRemove);
        Assert.Equal(harness.Localization.SettingsGameBuildIsGameFolder, row.GameFolderText);
        Assert.Equal(harness.Localization.SettingsGameBuildIsGameFolder, row.RemoveToolTip);
    }

    [Fact]
    public async Task RemoveKeptBuild_AsksFirstThenDeletesAndNamesWhatIsFree()
    {
        using var harness = await CreateWithKeptBuildAsync();
        var viewModel = harness.ViewModel;
        var folder = harness.Services.Paths.GetGameBuildFolder(KeptBuild);
        viewModel.SetMainWindowSettings();
        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);
        await viewModel.WhenKeptGameBuildsLoadedAsync();
        var row = Assert.Single(viewModel.KeptGameBuilds);
        var size = row.SizeText;

        row.RemoveCommand.Execute(null);

        Assert.True(row.IsConfirmingRemove);
        Assert.Equal(harness.Localization.FormatSettingsGameBuildRemoveConfirm(KeptBuild.ToString(), size), row.RemoveConfirmText);
        Assert.True(Directory.Exists(folder));

        await row.ConfirmRemoveCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.KeptGameBuilds);
        Assert.False(Directory.Exists(folder));
        Assert.Equal(harness.Localization.FormatSettingsGameBuildRemoved(KeptBuild.ToString(), size), viewModel.KeptGameBuildMessage);
    }

    [Fact]
    public async Task RemoveKeptBuild_WaitsForTheLoadOfAPreviewThatClosed()
    {
        using var harness = await CreateWithKeptBuildAsync(typeFiles: RoverNeedingMeasureTools());
        var viewModel = harness.ViewModel;
        var folder = harness.Services.Paths.GetGameBuildFolder(KeptBuild);
        var (service, held) = await VehiclePreviewViewModelTests.OpenRoverWithHeldLoadAsync(viewModel);
        viewModel.SetMainWindowSettings();
        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);
        await viewModel.WhenKeptGameBuildsLoadedAsync();
        var row = Assert.Single(viewModel.KeptGameBuilds);
        row.RemoveCommand.Execute(null);

        var remove = row.ConfirmRemoveCommand.ExecuteAsync(null);

        Assert.True(Directory.Exists(folder));
        Assert.False(remove.IsCompleted);

        held.SetResult(service.Ready(new FakeAssets()));
        await remove;

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task Discover_ShowsTheStillOfTheBuildOfTheActiveInstance()
    {
        using var harness = await CreateWithKeptBuildAsync(typeFiles: RoverNeedingMeasureTools());
        var viewModel = harness.ViewModel;
        var kept = await CreateInstanceAsync(harness, "Kept", KeptBuild);
        var current = await CreateInstanceAsync(harness, "Current", null);
        var keptBuild = PreviewGameBuild.Of(harness.Services.InstanceGames.Resolve((await harness.Services.Instances.GetByIdAsync(kept.InstanceId))!).Version)!;
        var settingsBuild = PreviewGameBuild.Of(harness.Services.InstalledVersion.GetInstalledVersion())!;
        Assert.NotEqual(keptBuild, settingsBuild);
        var sha256 = (await harness.Services.UserContent.GetLatestReleaseAsync("ExampleRover", harness.Services.Settings.ReleaseChannel))!.Download.Sha256!;
        harness.Services.PreviewThumbnails.Store(sha256, keptBuild, "scene", [1]);
        harness.Services.PreviewThumbnails.Store(sha256, settingsBuild, "scene", [1]);
        await harness.Services.Instances.SetActiveInstanceAsync(kept.InstanceId);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);
        var row = Assert.Single(viewModel.DiscoverItems);

        Assert.Equal(harness.Services.PreviewThumbnails.TryGetPath(sha256, keptBuild), row.PreviewStillPath);

        await viewModel.ActivateInstanceAsync(current.InstanceId);

        Assert.Equal(harness.Services.PreviewThumbnails.TryGetPath(sha256, settingsBuild), row.PreviewStillPath);
    }

    [Fact]
    public async Task VehiclePage_AnInstanceThatLeavesItsKeptBuild_MakesThePreviewAgain()
    {
        using var harness = await CreateWithKeptBuildAsync(typeFiles: RoverNeedingMeasureTools());
        var viewModel = harness.ViewModel;
        var service = new FakeVehiclePreview();
        viewModel.VehiclePreviewServiceOf = _ => service;
        var kept = await CreateInstanceAsync(harness, "Kept", KeptBuild);
        await harness.Services.Instances.SetActiveInstanceAsync(kept.InstanceId);
        await viewModel.LoadAsync();
        await VehiclePreviewViewModelTests.OpenRoverAsync(viewModel);
        var first = viewModel.VehiclePreview!;

        await viewModel.LoadAsync();
        Assert.Same(first, viewModel.VehiclePreview);

        Assert.Equal(GameBuildChangeRefusal.None, await harness.Services.GameBuilds.UseCurrentBuildAsync(kept.InstanceId, CancellationToken.None));
        await viewModel.LoadAsync();

        Assert.NotSame(first, viewModel.VehiclePreview);
    }

    [Fact]
    public async Task RemoveKeptBuild_ThatAnInstanceUses_SaysWhyInItsRow()
    {
        using var harness = await CreateWithKeptBuildAsync();
        var viewModel = harness.ViewModel;
        viewModel.SetMainWindowSettings();
        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);
        await viewModel.WhenKeptGameBuildsLoadedAsync();
        var row = Assert.Single(viewModel.KeptGameBuilds);
        Assert.True(row.CanRemove);

        await CreateInstanceAsync(harness, "Kept", KeptBuild);
        row.RemoveCommand.Execute(null);
        await row.ConfirmRemoveCommand.ExecuteAsync(null);

        var reloaded = Assert.Single(viewModel.KeptGameBuilds);
        Assert.Equal(harness.Localization.SettingsGameBuildInUse, reloaded.RemoveError);
        Assert.False(reloaded.CanRemove);
        Assert.Equal(harness.Localization.SettingsGameBuildInUse, reloaded.RemoveToolTip);
        Assert.Null(viewModel.KeptGameBuildError);
        Assert.True(Directory.Exists(harness.Services.Paths.GetGameBuildFolder(KeptBuild)));
    }

    [Fact]
    public async Task UseCurrentGameBuild_ToAnOlderBuild_BacksUpTheSavesAndHoldsTheConfirm()
    {
        using var harness = await CreateWithKeptBuildAsync();
        var viewModel = harness.ViewModel;
        var newer = GameVersion.Parse("2026.10.13.5602");
        // the first instance becomes the active one, so the backup must take the instance of the modal and not this one
        var active = await CreateInstanceAsync(harness, "Active", build: null);
        var kept = await CreateInstanceAsync(harness, "Kept", newer);
        foreach (var instance in new[] { active, kept })
            GameSavesViewModelTests.WriteItem(harness.Services.Paths.GetInstanceSavesFolder(instance.InstanceId), "Orbit", "2026-10-14T12:00:00.0000000", $"v{newer}", 10);
        await viewModel.LoadAsync();
        await viewModel.Instances.Single(item => item.InstanceId == kept.InstanceId).BeginUseCurrentGameBuildCommand.ExecuteAsync(null);
        var canConfirm = new List<bool>();
        viewModel.ConfirmUseCurrentGameBuildCommand.CanExecuteChanged += (_, _) => canConfirm.Add(viewModel.ConfirmUseCurrentGameBuildCommand.CanExecute(null));

        await viewModel.BackUpUseCurrentGameBuildSavesCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsUseCurrentGameBuildToOlder);
        var zip = Assert.Single(Directory.GetFiles(Path.Combine(harness.Services.Paths.GetBackupsRoot(), kept.InstanceId.ToString(), "saves"), "*.zip"));
        Assert.False(Directory.Exists(Path.Combine(harness.Services.Paths.GetBackupsRoot(), active.InstanceId.ToString())));
        Assert.Equal([false, true], canConfirm);
        Assert.True(viewModel.IsUseCurrentGameBuildModalOpen);
        Assert.Equal(harness.Localization.FormatGameSavesBackedUp(1, Path.GetDirectoryName(zip)!), viewModel.UseCurrentGameBuildBackupText);
        Assert.False(viewModel.IsUseCurrentGameBuildBackupFailed);
        Assert.Empty(viewModel.Toasts.Items);
    }

    [Fact]
    public async Task UseCurrentGameBuild_BackupWhileTheGameRuns_ShowsTheRefusalInTheModalUntilItCloses()
    {
        using var harness = await CreateWithKeptBuildAsync();
        var viewModel = harness.ViewModel;
        var kept = await CreateInstanceAsync(harness, "Kept", GameVersion.Parse("2026.10.13.5602"));
        await viewModel.LoadAsync();
        await viewModel.Instances.Single(item => item.InstanceId == kept.InstanceId).BeginUseCurrentGameBuildCommand.ExecuteAsync(null);
        harness.IsGameProcessRunning = () => true;

        await viewModel.BackUpUseCurrentGameBuildSavesCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatModalUseCurrentGameBuildBackupFailed(harness.Localization.GameSaveCloseGame), viewModel.UseCurrentGameBuildBackupText);
        Assert.True(viewModel.IsUseCurrentGameBuildBackupFailed);
        Assert.Empty(viewModel.Toasts.Items);

        viewModel.CancelUseCurrentGameBuildCommand.Execute(null);

        Assert.Null(viewModel.UseCurrentGameBuildBackupText);
        Assert.False(viewModel.IsUseCurrentGameBuildBackupFailed);
    }

    [Fact]
    public async Task UseCurrentGameBuild_WhileTheModsAreRead_ShowsTheCheckingStateAndDropsTheResultAfterCancel()
    {
        using var harness = await CreateWithKeptBuildAsync();
        var viewModel = harness.ViewModel;

        // both releases need a newer build than the settings, and the instance lists them out of their order
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: false, version: "1.1.8");
        await InstalledContent.AddAsync(harness, "KSArmory", activate: false);
        var row = await KeptInstanceWithSpaceDockModAsync(harness);
        var held = new TaskCompletionSource();
        harness.SpaceDock.ReleaseLookup = () => held.Task;

        var begin = row.BeginUseCurrentGameBuildCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsUseCurrentGameBuildModalOpen);
        Assert.True(viewModel.IsUseCurrentGameBuildChecking);
        Assert.False(viewModel.CanConfirmUseCurrentGameBuild);

        viewModel.CancelUseCurrentGameBuildCommand.Execute(null);

        Assert.True(row.BeginUseCurrentGameBuildCommand.CanExecute(null));

        held.SetResult();
        await begin;

        Assert.False(viewModel.IsUseCurrentGameBuildModalOpen);
        Assert.Null(viewModel.UseCurrentGameBuildPreview);

        harness.SpaceDock.ReleaseLookup = null;
        await row.BeginUseCurrentGameBuildCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsUseCurrentGameBuildChecking);
        Assert.True(viewModel.CanConfirmUseCurrentGameBuild);
        Assert.Equal("KSArmory, MeasureTools", viewModel.UseCurrentGameBuildIncompatibleText);
    }

    [Fact]
    public async Task UseCurrentGameBuild_CheckThatFails_ClosesTheModalAndNamesTheInstance()
    {
        using var harness = await CreateWithKeptBuildAsync();
        var viewModel = harness.ViewModel;
        var row = await KeptInstanceWithSpaceDockModAsync(harness);

        // the reads skip a network or IO error, so only this one fails the check
        harness.SpaceDock.ReleaseFailure = _ => new UnauthorizedAccessException("Access denied.");

        await viewModel.BeginUseCurrentGameBuildAsync(row);

        Assert.False(viewModel.IsUseCurrentGameBuildModalOpen);
        Assert.False(viewModel.IsUseCurrentGameBuildChecking);
        Assert.Contains(viewModel.Toasts.Items, toast => toast.Message == harness.Localization.FormatToastUseCurrentGameBuildFailed("Main"));
    }

    [Fact]
    public async Task KeepGameBuild_ReopenAfterCancelDuringTheCheck_StopsTheWalkOfTheFirstCheck()
    {
        HoldingGameBuildKeeper? keeper = null;
        using var harness = await CreateWithKeptBuildAsync(wrapGameBuilds: inner => keeper = new HoldingGameBuildKeeper(inner));
        var viewModel = harness.ViewModel;
        var instance = await CreateInstanceAsync(harness, "Main", build: null);
        await viewModel.LoadAsync();
        var row = viewModel.Instances.Single(item => item.InstanceId == instance.InstanceId);
        using var release = new ManualResetEventSlim();
        var held = keeper!.HoldNextCheck(release);

        var first = viewModel.BeginKeepGameBuildAsync(row);
        var firstCheck = await held;
        viewModel.CancelKeepGameBuildCommand.Execute(null);

        Assert.True(firstCheck.IsCancellationRequested);

        await viewModel.BeginKeepGameBuildAsync(row);
        release.Set();
        await first;

        Assert.Equal((Walked: 1, Stopped: 1), (keeper.Walked, keeper.Stopped));
        Assert.True(viewModel.CanConfirmKeepGameBuild);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("0.4.6.0", false)]
    public async Task DeleteModal_NamesTheKeptBuildOnlyForItsLastInstance(string? otherBuild, bool named)
    {
        using var harness = await CreateWithKeptBuildAsync();
        var viewModel = harness.ViewModel;
        var kept = await CreateInstanceAsync(harness, "Kept", KeptBuild);
        await CreateInstanceAsync(harness, "Other", otherBuild is null ? null : GameVersion.Parse(otherBuild));
        await viewModel.LoadAsync();

        viewModel.Instances.Single(item => item.InstanceId == kept.InstanceId).BeginDeleteCommand.Execute(null);
        await viewModel.WhenKeptGameBuildsLoadedAsync();

        var bytes = Assert.Single(await harness.Services.GameBuilds.ListKeptAsync(CancellationToken.None)).Bytes;
        Assert.Equal(
            named ? harness.Localization.FormatModalDeleteInstanceKeptBuild(KeptBuild.ToString(), MainViewModel.KeepSizeText(bytes)) : null,
            viewModel.DeleteModalKeptBuildText);
    }

    [Fact]
    public async Task NewInstance_OnAKeptBuild_StartsAgainstIt()
    {
        var starter = new GameSavesViewModelTests.RunningGameStarter();
        using var harness = await CreateWithKeptBuildAsync(starter);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();

        viewModel.BeginCreateInstanceCommand.Execute(null);
        await viewModel.WhenGameBuildChoicesLoadedAsync();
        Assert.Equal([null, KeptBuild], viewModel.GameBuildChoices.Select(choice => choice.Build));
        Assert.Equal(
            [(harness.Localization.ModalGameBuildSettings, harness.Localization.FormatInstanceGameBuild(viewModel.InstalledVersionText!)),
                (harness.Localization.ModalGameBuildKept, harness.Localization.FormatInstanceGameBuild(KeptBuild.ToString()))],
            viewModel.GameBuildChoices.Select(choice => (choice.Text, choice.VersionText)));
        Assert.True(viewModel.CanPickGameBuild);
        viewModel.SelectedGameBuildChoice = viewModel.GameBuildChoices[1];
        viewModel.ModalInstanceName = "Kept";
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);

        var created = (await harness.Services.Instances.GetAllAsync()).Single(item => item.Name == "Kept");
        Assert.Equal(KeptBuild, created.GameBuild);
        starter.GameLogPath = harness.Services.Paths.GetInstanceGameLogPath(created.InstanceId);
        await harness.Services.Instances.SetActiveInstanceAsync(created.InstanceId);
        await viewModel.LoadAsync();

        await viewModel.LaunchActiveInstanceAsync();

        Assert.True(harness.Services.Launcher.IsRunning(created.InstanceId));
        Assert.Equal(["KeptContent"], (await harness.Services.ModState.GetEntriesAsync(created.InstanceId)).Select(entry => entry.ModId));
    }

    [Theory]
    [InlineData("1.1.10", true)]
    [InlineData("1.1.9", false)]
    public async Task KeepModal_CountsTheModsThatDoNotSupportTheBuild(string version, bool incompatible)
    {
        using var harness = await CreateWithKeptBuildAsync();
        var viewModel = harness.ViewModel;
        var folder = harness.Services.Paths.GetGameBuildFolder(KeptBuild);
        // a folder the player chooses must hold the executable of the host
        File.WriteAllBytes(Path.Combine(folder, "KSA.exe"), []);
        File.WriteAllBytes(Path.Combine(folder, "KSA"), []);
        viewModel.SetMainWindowSettings();
        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);
        viewModel.GameDirectoryInput = folder;
        await viewModel.SaveGameDirectoryCommand.ExecuteAsync(null);
        var instance = await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, version: version);
        await viewModel.LoadAsync();

        await viewModel.BeginKeepGameBuildAsync(viewModel.Instances.Single(item => item.InstanceId == instance.InstanceId));

        Assert.True(viewModel.CanConfirmKeepGameBuild);
        Assert.Equal(incompatible ? harness.Localization.FormatModalKeepGameBuildIncompatible(1, 1, KeptBuild.ToString()) : null, viewModel.KeepGameBuildIncompatibleText);

        viewModel.CancelKeepGameBuildCommand.Execute(null);

        // macOS has no executable of its own, so GameBuildKeeper.PlanFolder refuses a chosen folder there
        if (OperatingSystem.IsMacOS())
            return;

        await viewModel.BeginAddGameBuildAsync(folder);

        Assert.Equal(harness.Localization.FormatModalAddGameBuildShared(KeptBuild.ToString(), folder), viewModel.KeepGameBuildText);
        Assert.Null(viewModel.KeepGameBuildIncompatibleText);
        Assert.False(viewModel.CanConfirmKeepGameBuild);
        Assert.Equal(harness.Localization.ModalClose, viewModel.KeepGameBuildDismissText);
    }

    [Fact]
    public async Task KeepGameBuild_PutsTheCopyInViewAndASecondKeepShowsTheRunningTask()
    {
        using var harness = await CreateWithKeptBuildAsync();
        var viewModel = harness.ViewModel;
        var first = await CreateInstanceAsync(harness, "First", null);
        var second = await CreateInstanceAsync(harness, "Second", null);
        await viewModel.LoadAsync();

        await viewModel.BeginKeepGameBuildAsync(viewModel.Instances.Single(item => item.InstanceId == first.InstanceId));
        await viewModel.ConfirmKeepGameBuildCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsTasksOpen);
        Assert.Equal((TaskKind.GameBuildKeep, first.InstanceId, TaskState.Finished), (viewModel.TaskInView!.Kind, viewModel.TaskInView.InstanceId, viewModel.TaskInView.State));

        var running = viewModel.Tasks.Start(TaskKind.GameBuildKeep, "2026.8.3.5117", second.InstanceId, second.Name, null, null, TaskState.Running);
        viewModel.IsTasksOpen = false;
        await viewModel.BeginKeepGameBuildAsync(viewModel.Instances.Single(item => item.InstanceId == second.InstanceId));

        Assert.False(viewModel.IsKeepGameBuildModalOpen);
        Assert.True(viewModel.IsTasksOpen);
        Assert.Same(running, viewModel.TaskInView);
        viewModel.Tasks.Discard(running);
    }

    [Fact]
    public async Task CloseNow_WithKeepCopies_WarnsWithTheBuildOfAnInstanceAndNotAsAnInstall()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var folderTask = viewModel.Tasks.Start(TaskKind.GameBuildKeep, "2026.7.6.4939", null, null, null, null, TaskState.Running);
        folderTask.Run = new InstallRun(harness.Localization, folderTask);
        var instanceTask = viewModel.Tasks.Start(TaskKind.GameBuildKeep, "2026.8.3.5117", Guid.NewGuid(), "Kept", null, null, TaskState.Running);
        instanceTask.Run = new InstallRun(harness.Localization, instanceTask);

        viewModel.CloseWaitsFor = [folderTask];

        Assert.Null(viewModel.CloseNowGameBuildKeepWarningText);
        Assert.False(viewModel.CloseStopsInstall);

        viewModel.CloseWaitsFor = [folderTask, instanceTask];

        Assert.Equal(harness.Localization.FormatCloseNowGameBuildKeepWarning("2026.8.3.5117"), viewModel.CloseNowGameBuildKeepWarningText);
        Assert.False(viewModel.CloseStopsInstall);
        Assert.True(instanceTask.CanStop);
        Assert.False(instanceTask.CanPause);
        viewModel.Tasks.Discard(folderTask);
        viewModel.Tasks.Discard(instanceTask);
    }

    private static async Task<Instance> CreateInstanceAsync(ViewModelHarness harness, string name, GameVersion? build)
    {
        var instance = (await harness.Services.Instances.CreateAsync(name, InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.UpdateAsync(instance.InstanceId, record =>
        {
            record.SetGameBuild(build);
            return true;
        });
        return instance;
    }

    /// <summary>
    /// The instance Main on the kept build with one mod that only SpaceDock serves, so its release read goes to <see cref="ViewModelHarness.FakeSpaceDock"/>.
    /// The mods that the test added to Main before stay in it.
    /// </summary>
    private static async Task<InstanceItem> KeptInstanceWithSpaceDockModAsync(ViewModelHarness harness)
    {
        harness.SpaceDock.Releases.Add(new ModVersionMetadata(
            specVersion: 1,
            modId: ViewModelHarness.FakeSpaceDock.OwnId,
            version: ModVersion.Parse("1.0.0"),
            releaseStatus: ReleaseStatus.Stable,
            releaseDate: DateTimeOffset.UnixEpoch,
            gameMin: "2026.1.1.1",
            gameMinRevision: 1,
            download: new DownloadInfo("https://archives.test/1.0.0.zip", sha256: null, sizeBytes: null, contentType: "application/zip"),
            installSizeBytes: null,
            dependencies: []));
        var instance = await InstalledContent.AddAsync(harness, ViewModelHarness.FakeSpaceDock.OwnId, activate: false, version: "1.0.0");

        // the add saves the instance it read, so the build is set after it
        await harness.Services.Instances.UpdateAsync(instance.InstanceId, record =>
        {
            record.SetGameBuild(KeptBuild);
            return true;
        });
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.WhenContentUpdatesCheckedAsync();
        return harness.ViewModel.Instances.Single(item => item.InstanceId == instance.InstanceId);
    }

    /// <summary>The configuration file of the recorded StarMap folder, which holds its game path.</summary>
    private static async Task<string> LoaderFileAsync(ViewModelHarness harness)
    {
        var listing = (await harness.Services.Mods.GetListingAsync("StarMap"))!;
        return Path.GetFullPath(Path.Combine(harness.Services.Paths.GetLoaderDirectoryPath("StarMap")!, listing.Provides!.Configure!.File));
    }

    /// <summary>
    /// The settings point at build 5117 and the library keeps build 0.4.6.0, and each build lists its own content.
    /// The newest MeasureTools release needs 5117, and the one before it fits every build. StarMap 0.4.6 is installed and fits every build.
    /// </summary>
    /// <summary>A vehicle that the kept build can load and that needs MeasureTools.</summary>
    private static Dictionary<string, string> RoverNeedingMeasureTools() => TypeFiles(
        ContentIndexTypeFiles.Vehicles,
        EntryNeeding("ExampleRover", "vehicle", "Example Rover", RoverUrl, RoverArchive(), Required("MeasureTools"), ["1.0.0"])
            .Replace("\"game_min\": \"2026.8.19.5261\", \"game_min_revision\": 5261", "\"game_min\": \"0.4.6.0\", \"game_min_revision\": 0", StringComparison.Ordinal));

    internal static Task<ViewModelHarness> CreateWithKeptBuildAsync(IProcessStarter? processStarter = null, ILauncher? loaderLauncher = null, Func<IGameBuildKeeper, IGameBuildKeeper>? wrapGameBuilds = null, IReadOnlyDictionary<string, string>? typeFiles = null) =>
        ViewModelHarness.CreateAsync(
            services =>
            {
                var root = Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!;
                var game = Directory.CreateDirectory(Path.Combine(root, "Game")).FullName;
                File.Copy(Path.Combine(AppContext.BaseDirectory, "GameVersionFixture.dll"), Path.Combine(game, "KSA.dll"));
                WriteContent(game, "SettingsContent");
                var kept = Directory.CreateDirectory(services.Paths.GetGameBuildFolder(KeptBuild)).FullName;
                File.Copy(Path.Combine(AppContext.BaseDirectory, OlderBuildFixture), Path.Combine(kept, "KSA.dll"));
                WriteContent(kept, "KeptContent");
                var loader = Directory.CreateDirectory(Path.Combine(root, "Loaders", "StarMap")).FullName;
                File.WriteAllBytes(Path.Combine(loader, "StarMap.exe"), []);
                return services.SettingsRepository.SaveAsync(services.Settings
                    .WithGameDirectory(game)
                    .WithLoaderInstallation("StarMap", new LoaderInstallation(loader, ModVersion.Parse("0.4.6"), rawVersion: null, isAdopted: false)));
            },
            processStarter: processStarter,
            loaderLauncher: loaderLauncher,
            wrapGameBuilds: wrapGameBuilds,
            typeFiles: typeFiles,
            editSnapshot: json =>
            {
                var root = JsonNode.Parse(json)!;
                var releases = root["listings"]!.AsArray().Single(listing => (string?)listing!["id"] == "MeasureTools")!["releases"]!;
                releases[0]!["game_min"] = "2026.8.3.5117";
                releases[0]!["game_min_revision"] = 5117;
                releases[1]!["game_min"] = "0.4.6.0";
                releases[1]!["game_min_revision"] = 0;

                // a launch rates the loader version against the build of the instance, so the installed StarMap must fit the kept build too
                var starMap = root["listings"]!.AsArray().Single(listing => (string?)listing!["id"] == "StarMap")!["releases"]!.AsArray()
                    .Single(release => (string?)release!["version"] == "0.4.6")!;
                starMap["game_min"] = "0.4.6.0";
                starMap["game_min_revision"] = 0;
                return root.ToJsonString();
            });

    private static void WriteContent(string gameDirectory, string contentId)
    {
        var content = Directory.CreateDirectory(Path.Combine(gameDirectory, "Content")).FullName;
        File.WriteAllText(Path.Combine(content, "manifest.toml"), $"[[mods]]\nid = \"{contentId}\"\nenabled = true\n");
    }

    /// <summary>
    /// The keeper of the services, which can hold one keep check before the keeper walks the files, and counts the
    /// checks that walked to the end and the checks that a cancel stopped.
    /// </summary>
    private sealed class HoldingGameBuildKeeper(IGameBuildKeeper inner) : IGameBuildKeeper
    {
        private Hold? _hold;
        private int _walked;
        private int _stopped;

        public int Walked => Volatile.Read(ref _walked);

        public int Stopped => Volatile.Read(ref _stopped);

        /// <summary>Holds the next check until <paramref name="release"/> is set, and gives its token once it waits.</summary>
        public Task<CancellationToken> HoldNextCheck(ManualResetEventSlim release)
        {
            var hold = new Hold(new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously), release);
            _hold = hold;
            return hold.Waiting.Task;
        }

        public GameBuildKeepCheck Check(Instance instance, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _hold, null) is { } hold)
            {
                hold.Waiting.SetResult(cancellationToken);
                if (!hold.Release.Wait(TimeSpan.FromSeconds(30)))
                    throw new TimeoutException("The test did not release the held keep check.");
            }

            try
            {
                var check = inner.Check(instance, cancellationToken);
                Interlocked.Increment(ref _walked);
                return check;
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _stopped);
                throw;
            }
        }

        public Task<GameBuildKeepCheck> KeepAsync(Guid instanceId, GameVersion expected, IProgress<LibraryMoveProgress>? progress, CancellationToken cancellationToken)
            => inner.KeepAsync(instanceId, expected, progress, cancellationToken);

        public Task<GameBuildChangeRefusal> UseCurrentBuildAsync(Guid instanceId, CancellationToken cancellationToken) => inner.UseCurrentBuildAsync(instanceId, cancellationToken);

        public Task<IReadOnlyList<KeptGameBuild>> ListKeptAsync(CancellationToken cancellationToken) => inner.ListKeptAsync(cancellationToken);

        public Task<GameBuildChangeRefusal> RemoveAsync(GameVersion build, CancellationToken cancellationToken) => inner.RemoveAsync(build, cancellationToken);

        public GameBuildKeepCheck CheckFolder(string folder, CancellationToken cancellationToken) => inner.CheckFolder(folder, cancellationToken);

        public GameBuildKeepRefusal CheckGameFolder() => inner.CheckGameFolder();

        public Task<GameBuildKeepCheck> KeepFolderAsync(string folder, GameVersion expected, IProgress<LibraryMoveProgress>? progress, CancellationToken cancellationToken)
            => inner.KeepFolderAsync(folder, expected, progress, cancellationToken);

        public IReadOnlyList<GameVersion> ListKept() => inner.ListKept();

        private sealed record Hold(TaskCompletionSource<CancellationToken> Waiting, ManualResetEventSlim Release);
    }
}
