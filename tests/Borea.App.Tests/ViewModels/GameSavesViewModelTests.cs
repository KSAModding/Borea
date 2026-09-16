using System.Globalization;
using Borea.Composition;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Storage.Launch;

namespace Borea.App.Tests.ViewModels;

public sealed class GameSavesViewModelTests
{
    [Fact]
    public async Task Open_ListsSavesAndVehiclesWithTheirDetails()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        var root = harness.Services.Paths.GetInstanceRoot(instance.InstanceId);
        WriteItem(Path.Combine(root, "saves"), "Earth Launch 1", "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", 1500);
        WriteItem(Path.Combine(root, "vehicles"), "Rocket", "2026-08-10T06:44:36.6429982", "v2026.8.1.5240-DEBUG--dev-baker", 10, "vehicle.xml");

        await OpenAsync(harness, "Main");

        var save = Assert.Single(viewModel.SavesSection.Items);
        Assert.Equal("Earth Launch 1", save.Name);
        var updated = new DateTimeOffset(2026, 8, 1, 14, 34, 32, TimeSpan.Zero).AddTicks(4054896);
        Assert.Equal(harness.Localization.FormatGameSaveUpdated(updated.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)), save.UpdatedText);
        Assert.Equal("2026.8.3.5117", save.BuildText);
        Assert.Equal(viewModel.FormatGameDataSize(save.Entry.SizeBytes), save.SizeText);
        Assert.False(save.IsVehicle);
        var vehicle = Assert.Single(viewModel.VehiclesSection.Items);
        Assert.Equal("Rocket", vehicle.Name);
        Assert.True(vehicle.IsVehicle);
    }

    [Fact]
    public async Task Open_SaveOfAnOlderBuild_ShowsTheWarning()
    {
        using var harness = await ViewModelHarness.CreateAsync(services =>
        {
            var game = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Game")).FullName;
            File.Copy(Path.Combine(AppContext.BaseDirectory, "GameVersionFixture.dll"), Path.Combine(game, "KSA.dll"));
            return services.SettingsRepository.SaveAsync(services.Settings.WithGameDirectory(game));
        });
        var instance = await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        var saves = harness.Services.Paths.GetInstanceSavesFolder(instance.InstanceId);
        WriteItem(saves, "Old", "2026-07-03T12:00:00.0000000", "v2026.7.3.4826", 10);
        WriteItem(saves, "Current", "2026-08-03T12:00:00.0000000", "v2026.8.3.5117", 10);

        await OpenAsync(harness, "Main");

        var old = harness.ViewModel.SavesSection.Items.Single(item => item.Name == "Old");
        Assert.True(old.IsOlderBuild);
        Assert.Equal(harness.Localization.FormatGameSaveOlderBuild("2026.7.3.4826", "2026.8.3.5117"), old.OlderBuildText);
        Assert.False(harness.ViewModel.SavesSection.Items.Single(item => item.Name == "Current").IsOlderBuild);
    }

    [Fact]
    public async Task EmptySection_OpensTheFolderItCreates()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        await OpenAsync(harness, "Main");
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;

        Assert.False(viewModel.SavesSection.HasItems);
        viewModel.SavesSection.OpenFolderCommand.Execute(null);

        var saves = harness.Services.Paths.GetInstanceSavesFolder(instance.InstanceId);
        Assert.Equal([saves], opened);
        Assert.True(Directory.Exists(saves));
        Assert.Null(viewModel.SavesSection.Error);
    }

    [Fact]
    public async Task BackUp_WritesAZipIntoTheBackups()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        WriteItem(harness.Services.Paths.GetInstanceSavesFolder(instance.InstanceId), "Orbit", "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", 10);
        await OpenAsync(harness, "Main");
        var section = harness.ViewModel.SavesSection;

        await Assert.Single(section.Items).BackUpCommand.ExecuteAsync(null);

        var zip = Assert.Single(Directory.GetFiles(Path.Combine(harness.Services.Paths.GetBackupsRoot(), instance.InstanceId.ToString(), "saves")));
        Assert.Equal(harness.Localization.FormatGameSaveBackedUp("Orbit", zip), section.Message);
        Assert.Null(section.Error);
    }

    [Fact]
    public async Task BackUpAllSaves_ZipsEverySaveOfTheInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        var saves = harness.Services.Paths.GetInstanceSavesFolder(instance.InstanceId);
        WriteItem(saves, "Orbit", "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", 10);
        WriteItem(saves, "Moon", "2026-08-02T14:34:32.4054896", "v2026.8.3.5117", 10);
        await OpenAsync(harness, "Main");
        await harness.ViewModel.ShowInstanceGameDataCommand.ExecuteAsync(null);

        await harness.ViewModel.BackUpAllSavesCommand.ExecuteAsync(null);

        var backups = Path.Combine(harness.Services.Paths.GetBackupsRoot(), instance.InstanceId.ToString(), "saves");
        Assert.Equal(2, Directory.GetFiles(backups, "*.zip").Length);
        Assert.Equal(harness.Localization.FormatGameSavesBackedUp(2, backups), harness.ViewModel.SavesSection.Message);
        Assert.True(harness.ViewModel.IsContentTab);
    }

    [Fact]
    public async Task CopyToInstance_NameExists_AsksBeforeReplacing()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var paths = harness.Services.Paths;
        var first = await harness.Services.Instances.CreateAsync("First", InstanceSource.Custom.Value);
        var second = await harness.Services.Instances.CreateAsync("Second", InstanceSource.Custom.Value);
        WriteItem(paths.GetInstanceSavesFolder(first.InstanceId), "Orbit", "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", 300);
        WriteItem(paths.GetInstanceSavesFolder(second.InstanceId), "Orbit", "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", 20);
        await OpenAsync(harness, "First");
        var row = Assert.Single(harness.ViewModel.SavesSection.Items);
        var target = Path.Combine(paths.GetInstanceSavesFolder(second.InstanceId), "Orbit", "universe.xml");

        row.BeginCopyCommand.Execute(null);
        Assert.Equal(["Second"], row.CopyTargets.Select(instance => instance.Name));
        Assert.Equal(harness.Localization.GameSaveCopyModsNote, row.CopyNoteText);
        await row.CopyCommand.ExecuteAsync(null);

        Assert.True(row.IsConfirmingReplace);
        Assert.Equal(harness.Localization.FormatGameSaveReplace("Orbit", "Second"), row.ReplaceText);
        Assert.Equal(20, new FileInfo(target).Length);

        await row.ConfirmReplaceCommand.ExecuteAsync(null);

        Assert.Equal(300, new FileInfo(target).Length);
        Assert.False(row.IsConfirmingReplace);
        Assert.Equal(harness.Localization.FormatGameSaveCopied("Orbit", "Second"), harness.ViewModel.SavesSection.Message);
    }

    [Fact]
    public async Task CopyFromGameProfile_CopiesTheChosenItemsAndAsksBeforeReplacing()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var paths = harness.Services.Paths;
        var instance = await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        var profileSaves = Path.Combine(paths.GetSharedProfileRoot(), "saves");
        WriteItem(profileSaves, "Orbit", "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", 10);
        WriteItem(profileSaves, "Moon", "2026-08-02T14:34:32.4054896", "v2026.8.3.5117", 300);
        WriteItem(paths.GetInstanceSavesFolder(instance.InstanceId), "Moon", "2026-08-02T14:34:32.4054896", "v2026.8.3.5117", 20);
        await OpenAsync(harness, "Main");
        var section = harness.ViewModel.SavesSection;

        await section.BeginCopyFromProfileCommand.ExecuteAsync(null);
        Assert.True(section.IsChoosingFromProfile);
        Assert.Equal([false, true], section.ProfileItems.OrderBy(item => item.Name, StringComparer.Ordinal).Select(item => item.ExistsInInstance).Reverse());
        section.ProfileItems.Single(item => item.Name == "Orbit").IsSelected = true;
        await section.CopyFromProfileCommand.ExecuteAsync(null);

        Assert.False(section.IsChoosingFromProfile);
        Assert.Equal(["Moon", "Orbit"], section.Items.Select(item => item.Name).Order(StringComparer.Ordinal));
        Assert.Equal(harness.Localization.FormatGameSavesCopiedFromProfile(1), section.Message);

        await section.BeginCopyFromProfileCommand.ExecuteAsync(null);
        section.ProfileItems.Single(item => item.Name == "Moon").IsSelected = true;
        await section.CopyFromProfileCommand.ExecuteAsync(null);
        var moon = Path.Combine(paths.GetInstanceSavesFolder(instance.InstanceId), "Moon", "universe.xml");
        Assert.True(section.IsConfirmingProfileReplace);
        Assert.Equal(20, new FileInfo(moon).Length);

        await section.ReplaceFromProfileCommand.ExecuteAsync(null);

        Assert.Equal(300, new FileInfo(moon).Length);
        Assert.False(section.IsChoosingFromProfile);
    }

    [Fact]
    public async Task Delete_AfterConfirmation_MovesTheFolderIntoTheBackups()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        var vehicles = harness.Services.Paths.GetInstanceVehiclesFolder(instance.InstanceId);
        WriteItem(vehicles, "Rocket", "2026-08-10T06:44:36.6429982", "v2026.8.3.5117", 10, "vehicle.xml");
        await OpenAsync(harness, "Main");
        var section = harness.ViewModel.VehiclesSection;
        var row = Assert.Single(section.Items);

        row.BeginDeleteCommand.Execute(null);
        Assert.True(row.IsConfirmingDelete);
        Assert.False(row.IsIdle);
        await row.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.False(Directory.Exists(Path.Combine(vehicles, "Rocket")));
        Assert.Empty(section.Items);
        Assert.Single(Directory.GetDirectories(Path.Combine(harness.Services.Paths.GetBackupsRoot(), instance.InstanceId.ToString(), "Vehicles")));
        Assert.Equal(harness.Localization.FormatGameSaveDeleted("Rocket"), section.Message);
    }

    [Fact]
    public async Task LockedFile_CopyAndDeleteRefuseWithCloseTheGame()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var paths = harness.Services.Paths;
        var first = await harness.Services.Instances.CreateAsync("First", InstanceSource.Custom.Value);
        var second = await harness.Services.Instances.CreateAsync("Second", InstanceSource.Custom.Value);
        var saves = paths.GetInstanceSavesFolder(first.InstanceId);
        WriteItem(saves, "Orbit", "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", 10);
        await OpenAsync(harness, "First");
        var section = harness.ViewModel.SavesSection;
        var row = Assert.Single(section.Items);
        using var locked = new FileStream(Path.Combine(saves, "Orbit", "universe.xml"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        row.BeginCopyCommand.Execute(null);
        await row.CopyCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.GameSaveCloseGame, section.Error);
        Assert.False(Directory.Exists(Path.Combine(paths.GetInstanceSavesFolder(second.InstanceId), "Orbit")));

        row.BeginDeleteCommand.Execute(null);
        await row.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.GameSaveCloseGame, section.Error);
        Assert.True(Directory.Exists(Path.Combine(saves, "Orbit")));
        Assert.Single(section.Items);
    }

    [Fact]
    public async Task Delete_WhileBoreaRunsTheInstance_RefusesWithCloseTheGame()
    {
        var starter = new RunningGameStarter();
        using var harness = await ViewModelHarness.CreateAsync(WithStarMap, processStarter: starter);
        var viewModel = harness.ViewModel;
        var instance = await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        var saves = harness.Services.Paths.GetInstanceSavesFolder(instance.InstanceId);
        WriteItem(saves, "Orbit", "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", 10);
        starter.GameLogPath = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        await OpenAsync(harness, "Main");
        await viewModel.PlayCommand.ExecuteAsync(null);
        Assert.True(harness.Services.Launcher.IsRunning(instance.InstanceId));
        var row = Assert.Single(viewModel.SavesSection.Items);

        row.BeginDeleteCommand.Execute(null);
        await row.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.GameSaveCloseGame, viewModel.SavesSection.Error);
        Assert.True(Directory.Exists(Path.Combine(saves, "Orbit")));
        Assert.False(Directory.Exists(harness.Services.Paths.GetBackupsRoot()));
    }

    [Fact]
    public async Task LanguageChange_RetitlesTheSections()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        await OpenAsync(harness, "Main");

        harness.Localization.TrySetCulture("de");

        Assert.Equal(harness.Localization.InstanceGroupSaves, harness.ViewModel.SavesSection.Title);
        Assert.Equal(harness.Localization.GameSaveNoVehicles, harness.ViewModel.VehiclesSection.EmptyText);
        Assert.NotEqual("Saves", harness.Localization.InstanceGroupSaves);
    }

    private static async Task OpenAsync(ViewModelHarness harness, string name)
    {
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.Instances.Single(instance => instance.Name == name).OpenCommand.ExecuteAsync(null);
    }

    /// <summary>A folder the way the game writes it, with a meta.toml in the shape of SaveMetaData.</summary>
    private static void WriteItem(string kindFolder, string name, string updated, string version, int dataBytes, string dataFile = "universe.xml")
    {
        var folder = Directory.CreateDirectory(Path.Combine(kindFolder, name)).FullName;
        File.WriteAllText(Path.Combine(folder, "meta.toml"), $"""
            name = "{name}"
            created = 2026-07-03T09:15:02.1200000
            updated = {updated}
            version = "{version}"
            systems = [ "Sol", ]

            """);
        File.WriteAllBytes(Path.Combine(folder, dataFile), new byte[dataBytes]);
    }

    private static Task WithStarMap(BoreaServices services)
    {
        var loader = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Loaders", "StarMap")).FullName;
        File.WriteAllBytes(Path.Combine(loader, "StarMap.exe"), []);
        File.WriteAllBytes(Path.Combine(loader, "StarMap.dll"), []);
        return services.SettingsRepository.SaveAsync(services.Settings.WithLoaderInstallation(
            "StarMap",
            new LoaderInstallation(loader, ModVersion.Parse("0.4.6"), rawVersion: null, isAdopted: false)));
    }

    /// <summary>Hands out a loader process that keeps running and whose game writes its log at once, so the launch watch ends early.</summary>
    private sealed class RunningGameStarter : IProcessStarter
    {
        public string? GameLogPath { get; set; }

        public IStartedProcess Start(LaunchPlan plan) => new RunningProcess(this);

        private sealed class RunningProcess(RunningGameStarter owner) : IStartedProcess
        {
            public int Id => 4243;

            public bool HasExited => false;

            public int? ExitCode => null;

            public IReadOnlyList<string> RecentOutput => [];

            public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
            {
                if (owner.GameLogPath is { } log)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                    File.WriteAllText(log, "started");
                }

                return Task.FromResult(false);
            }

            public void Dispose()
            {
            }
        }
    }
}
