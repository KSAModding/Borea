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
    /// The settings point at build 5117 and the library keeps build 0.4.6.0, and each build lists its own content.
    /// The newest MeasureTools release needs 5117, and the one before it fits every build. StarMap is installed.
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
                return root.ToJsonString();
            });

    private static void WriteContent(string gameDirectory, string contentId)
    {
        var content = Directory.CreateDirectory(Path.Combine(gameDirectory, "Content")).FullName;
        File.WriteAllText(Path.Combine(content, "manifest.toml"), $"[[mods]]\nid = \"{contentId}\"\nenabled = true\n");
    }
}
