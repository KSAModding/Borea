using System.Text.Json.Nodes;
using Borea.App.ViewModels;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Storage.Launch;

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

        Assert.Equal(harness.Localization.FormatInstallKeptBuildUnavailable("2026.10.13.5602", harness.Services.Paths.GetGameBuildFolder(gone)), row.InstallError);
        Assert.Null(row.PendingPlan);
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

    [Fact]
    public async Task Play_OnAKeptBuildThatIsGone_SaysSoAndRatesNoLoader()
    {
        using var harness = await CreateWithKeptBuildAsync();
        var gone = await CreateInstanceAsync(harness, "Gone", GameVersion.Parse("2026.10.13.5602"));
        // a pin that is not installed would open the loader prompt
        await harness.Services.Instances.UpdateAsync(gone.InstanceId, record =>
        {
            record.SetLoader(LoaderSetting.Pinned("StarMap", ModVersion.Parse("0.4.9")));
            return true;
        });
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.ActivateInstanceAsync(gone.InstanceId);

        await harness.ViewModel.LaunchActiveInstanceAsync();

        Assert.False(harness.ViewModel.IsLoaderPromptOpen);
        Assert.Equal(harness.Localization.LaunchGameBuildMissing, harness.ViewModel.LaunchMessage);
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
        await viewModel.BeginAddGameBuildAsync(folder);

        Assert.Equal(harness.Localization.FormatModalAddGameBuildShared(KeptBuild.ToString(), folder), viewModel.KeepGameBuildText);
        Assert.Null(viewModel.KeepGameBuildIncompatibleText);
        Assert.False(viewModel.CanConfirmKeepGameBuild);
        Assert.Equal(harness.Localization.ModalClose, viewModel.KeepGameBuildDismissText);
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
    private static Task<ViewModelHarness> CreateWithKeptBuildAsync(IProcessStarter? processStarter = null) =>
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
}
