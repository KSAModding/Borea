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
    public async Task Open_InstanceFromAPack_GroupsThePinnedMembersUnderThePack()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPack("armory-pack", "Armory Pack", "1.0.0", ("KSArmory", "0.8.44")));
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Armory", new InstanceSource.FromModPack("armory-pack", ModVersion.Parse("1.0.0")));
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true, InstallReason.ModPack);
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, InstallReason.ModPack);
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true);
        await viewModel.LoadAsync();

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        Assert.Equal(
            [harness.Localization.FormatInstanceGroupModpack("Armory Pack", "1.0.0"), harness.Localization.InstanceGroupModpacks, harness.Localization.InstanceGroupMods],
            viewModel.ContentGroups.Select(group => group.Title));
        Assert.Equal("KSArmory", Assert.Single(viewModel.ContentGroups[0].Items).ModId);
        Assert.Equal("MeasureTools", Assert.Single(viewModel.ContentGroups[1].Items).ModId);
        Assert.Equal("AdvancedFlightComputer", Assert.Single(viewModel.ContentGroups[2].Items).ModId);
    }

    [Fact]
    public async Task Open_PackMembersWithoutARecordedPack_ShareOneModpacksGroupBeforeTheMods()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true, InstallReason.ModPack);
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true);
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, InstallReason.Dependency);
        await viewModel.LoadAsync();

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        Assert.Equal(
            [harness.Localization.InstanceGroupModpacks, harness.Localization.InstanceGroupMods, harness.Localization.InstanceGroupDependencies],
            viewModel.ContentGroups.Select(group => group.Title));
        Assert.Equal("KSArmory", Assert.Single(viewModel.ContentGroups[0].Items).ModId);
    }

    [Fact]
    public async Task Open_RecordedPackNotInTheIndex_KeepsItsMembersInTheModpacksGroup()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Gone", new InstanceSource.FromModPack("gone-pack", ModVersion.Parse("1.0.0")));
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true, InstallReason.ModPack);
        await viewModel.LoadAsync();

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        var group = Assert.Single(viewModel.ContentGroups);
        Assert.Equal(harness.Localization.InstanceGroupModpacks, group.Title);
        Assert.Equal("KSArmory", Assert.Single(group.Items).ModId);
    }

    private static Func<string, string> WithPack(string id, string name, string version, params (string Id, string Version)[] pins) => snapshot =>
    {
        const string empty = "\"packs\": []";
        if (!snapshot.Contains(empty, StringComparison.Ordinal))
            throw new InvalidOperationException("The snapshot fixture no longer has an empty packs array.");

        var mods = string.Join(", ", pins.Select(pin => $$"""{ "id": "{{pin.Id}}", "version": "{{pin.Version}}" }"""));
        var pack = $$"""{ "id": "{{id}}", "versions": [{ "authored": { "spec_version": 1, "id": "{{id}}", "type": "modpack", "name": "{{name}}", "authors": ["Maxi"], "abstract": "{{name}} abstract.", "license": "MIT", "version": "{{version}}", "released_at": "2026-09-01T12:00:00Z", "links": { "forums": "https://forums.example.com/{{id}}" }, "compatibility": { "game_min": "2026.8.19.5261" }, "mods": [{{mods}}] } }] }""";
        return snapshot.Replace(empty, $"\"packs\": [{pack}]", StringComparison.Ordinal);
    };

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

    [Fact]
    public async Task Play_GameComesUp_ShowsTheInstanceAsPlayedJustNow()
    {
        using var harness = await ViewModelHarness.CreateAsync(RecordStarMapAsync, processStarter: new GameStartingStarter());
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        Assert.Equal(harness.Localization.LibraryNeverPlayed, viewModel.ActiveInstance.LastPlayedText);

        await viewModel.PlayCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasLaunchOutput);
        Assert.NotNull((await harness.Services.Instances.GetByIdAsync(viewModel.ActiveInstance.InstanceId))?.LastPlayedAt);
        Assert.Equal(harness.Localization.FormatTimeAgo(TimeSpan.Zero), viewModel.ActiveInstance.LastPlayedText);
    }

    [Fact]
    public async Task Open_InstanceWithGameLogs_ShowsThePlaytimeAndTheSessions()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        var archives = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId))!, "Archives"));
        await File.WriteAllLinesAsync(Path.Combine(archives.FullName, "KittenSpaceAgency.260914.0.log"), ["20:00:00.000  INFO loaded settings from settings.toml", "20:40:00.000 DEBUG Shutting down application"]);
        await viewModel.LoadAsync();

        await Assert.Single(viewModel.Instances).OpenCommand.ExecuteAsync(null);
        await viewModel.WhenPlaytimeLoadedAsync();

        Assert.Equal(harness.Localization.FormatInstancePlayed(harness.Localization.FormatDuration(TimeSpan.FromMinutes(40))), viewModel.InstancePlaytimeText);
        Assert.Equal(harness.Localization.FormatInstanceSessions(1), viewModel.InstanceSessionsText);
        Assert.Equal(harness.Localization.InstancePlaytimeToolTip, viewModel.InstancePlaytimeToolTip);
    }

    [Fact]
    public async Task Open_InstanceWithoutGameLogs_SaysThereIsNoPlaytimeYet()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();

        await Assert.Single(viewModel.Instances).OpenCommand.ExecuteAsync(null);
        await viewModel.WhenPlaytimeLoadedAsync();

        Assert.Equal(harness.Localization.InstanceNoPlaytime, viewModel.InstancePlaytimeText);
        Assert.Null(viewModel.InstanceSessionsText);
    }

    private static Task RecordStarMapAsync(Borea.Composition.BoreaServices services)
    {
        var loader = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Loaders", "StarMap")).FullName;
        File.WriteAllBytes(Path.Combine(loader, "StarMap.exe"), []);
        File.WriteAllBytes(Path.Combine(loader, "StarMap.dll"), []);
        return services.SettingsRepository.SaveAsync(services.Settings.WithLoaderInstallation(
            "StarMap",
            new Borea.Core.ModLoaders.LoaderInstallation(loader, ModVersion.Parse("0.4.6"), rawVersion: null, isAdopted: false)));
    }

    /// <summary>Hands out a loader that keeps running and whose game writes its log while the start is watched.</summary>
    private sealed class GameStartingStarter : Borea.Storage.Launch.IProcessStarter
    {
        public Borea.Storage.Launch.IStartedProcess Start(Borea.Core.Launch.LaunchPlan plan) => new RunningGame(plan.Arguments[1]);

        private sealed class RunningGame(string instanceRoot) : Borea.Storage.Launch.IStartedProcess
        {
            public int Id => 4242;

            public bool HasExited => false;

            public int? ExitCode => null;

            public IReadOnlyList<string> RecentOutput => [];

            public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
            {
                var log = Path.Combine(instanceRoot, "logs", "KittenSpaceAgency.260915-112433.4242.log");
                Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                File.AppendAllText(log, "11:24:36.689  INFO loaded settings from settings.toml\n");
                return Task.FromResult(false);
            }

            public void Dispose()
            {
            }
        }
    }
}
