using System.Globalization;
using Borea.App.ViewModels;
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
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
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
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
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
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
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
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        WriteItem(harness.Services.Paths.GetInstanceSavesFolder(instance.InstanceId), "Orbit", "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", 10);
        await OpenAsync(harness, "Main");
        var section = harness.ViewModel.SavesSection;

        await Assert.Single(section.Items).BackUpCommand.ExecuteAsync(null);

        var zip = Assert.Single(Directory.GetFiles(Path.Combine(harness.Services.Paths.GetBackupsRoot(), instance.InstanceId.ToString(), "saves"), "*.zip"));
        Assert.Equal(harness.Localization.FormatGameSaveBackedUp("Orbit", zip), harness.ViewModel.Toasts.Items[^1].Message);
        Assert.Null(section.Error);
    }

    [Fact]
    public async Task BackUpAllSaves_ZipsEverySaveOfTheInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var saves = harness.Services.Paths.GetInstanceSavesFolder(instance.InstanceId);
        WriteItem(saves, "Orbit", "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", 10);
        WriteItem(saves, "Moon", "2026-08-02T14:34:32.4054896", "v2026.8.3.5117", 10);
        await OpenAsync(harness, "Main");
        await harness.ViewModel.ShowInstanceGameDataCommand.ExecuteAsync(null);

        await harness.ViewModel.BackUpAllSavesCommand.ExecuteAsync(null);

        var backups = Path.Combine(harness.Services.Paths.GetBackupsRoot(), instance.InstanceId.ToString(), "saves");
        Assert.Equal(2, Directory.GetFiles(backups, "*.zip").Length);
        Assert.Equal(harness.Localization.FormatGameSavesBackedUp(2, backups), harness.ViewModel.Toasts.Items[^1].Message);
        Assert.True(harness.ViewModel.IsContentTab);
    }

    [Fact]
    public async Task CopyToInstance_NameExists_AsksBeforeReplacing()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var paths = harness.Services.Paths;
        var first = (await harness.Services.Instances.CreateAsync("First", InstanceSource.Custom.Value)).Instance;
        var second = (await harness.Services.Instances.CreateAsync("Second", InstanceSource.Custom.Value)).Instance;
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
        Assert.Equal(harness.Localization.FormatGameSaveCopied("Orbit", "Second"), harness.ViewModel.Toasts.Items[^1].Message);
    }

    [Fact]
    public async Task CopyFromGameProfile_CopiesTheChosenItemsAndAsksBeforeReplacing()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var paths = harness.Services.Paths;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
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
        Assert.Equal(harness.Localization.FormatGameSavesCopiedFromProfile(1), harness.ViewModel.Toasts.Items[^1].Message);

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
    public async Task CopyFromGameProfile_SelectAll_TakesOnlyItsOwnSectionAndCountsTheChoice()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var paths = harness.Services.Paths;
        await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        var profileSaves = Path.Combine(paths.GetSharedProfileRoot(), "saves");
        WriteItem(profileSaves, "Orbit", "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", 10);
        WriteItem(profileSaves, "Moon", "2026-08-02T14:34:32.4054896", "v2026.8.3.5117", 300);
        WriteItem(Path.Combine(paths.GetSharedProfileRoot(), "vehicles"), "Rocket", "2026-08-02T14:34:32.4054896", "v2026.8.3.5117", 10, "vehicle.xml");
        await OpenAsync(harness, "Main");
        var saves = harness.ViewModel.SavesSection;
        var vehicles = harness.ViewModel.VehiclesSection;
        await saves.BeginCopyFromProfileCommand.ExecuteAsync(null);
        await vehicles.BeginCopyFromProfileCommand.ExecuteAsync(null);
        var changed = new List<string?>();
        saves.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.False(saves.AreAllProfileItemsSelected);
        Assert.Equal(harness.Localization.FormatGameSaveProfileSelected(0, 2), saves.ProfileSelectionText);

        saves.ToggleAllProfileItemsCommand.Execute(null);

        Assert.True(saves.AreAllProfileItemsSelected);
        Assert.All(saves.ProfileItems, item => Assert.True(item.IsSelected));
        Assert.Equal(harness.Localization.FormatGameSaveProfileSelected(2, 2), saves.ProfileSelectionText);
        Assert.False(Assert.Single(vehicles.ProfileItems).IsSelected);

        saves.ToggleAllProfileItemsCommand.Execute(null);

        Assert.False(saves.AreAllProfileItemsSelected);
        Assert.All(saves.ProfileItems, item => Assert.False(item.IsSelected));

        changed.Clear();
        saves.ProfileItems[0].IsSelected = true;

        Assert.Null(saves.AreAllProfileItemsSelected);
        Assert.Equal(harness.Localization.FormatGameSaveProfileSelected(1, 2), saves.ProfileSelectionText);
        Assert.Contains(nameof(GameSaveSection.ProfileSelectionText), changed);

        saves.ToggleAllProfileItemsCommand.Execute(null);

        Assert.True(saves.AreAllProfileItemsSelected);
        Assert.All(saves.ProfileItems, item => Assert.True(item.IsSelected));
    }

    [Fact]
    public async Task CopyFromGameProfile_ClearingTheSelection_TakesTheChooserBackFromTheReplaceQuestion()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var paths = harness.Services.Paths;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var profileSaves = Path.Combine(paths.GetSharedProfileRoot(), "saves");
        WriteItem(profileSaves, "Moon", "2026-08-02T14:34:32.4054896", "v2026.8.3.5117", 300);
        WriteItem(paths.GetInstanceSavesFolder(instance.InstanceId), "Moon", "2026-08-02T14:34:32.4054896", "v2026.8.3.5117", 20);
        await OpenAsync(harness, "Main");
        var section = harness.ViewModel.SavesSection;
        await section.BeginCopyFromProfileCommand.ExecuteAsync(null);
        section.ProfileItems.Single().IsSelected = true;
        await section.CopyFromProfileCommand.ExecuteAsync(null);

        Assert.True(section.IsConfirmingProfileReplace);

        section.ToggleAllProfileItemsCommand.Execute(null);

        Assert.False(section.IsConfirmingProfileReplace);
        Assert.True(section.IsChoosingFromProfile);
        Assert.False(section.AreAllProfileItemsSelected);
    }

    [Fact]
    public async Task EmptyInstance_ProfileHasSaves_SaysSoUntilASaveIsCopied()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        WriteItem(Path.Combine(harness.Services.Paths.GetSharedProfileRoot(), "saves"), "Orbit", "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", 10);
        await OpenAsync(harness, "Main");

        Assert.True(viewModel.ShowInstanceStartsEmpty);

        await viewModel.CopyAllFromProfileCommand.ExecuteAsync(null);
        Assert.True(viewModel.SavesSection.IsChoosingFromProfile);
        Assert.False(viewModel.VehiclesSection.IsChoosingFromProfile);
        Assert.Single(viewModel.SavesSection.ProfileItems).IsSelected = true;
        await viewModel.SavesSection.CopyFromProfileCommand.ExecuteAsync(null);

        Assert.Single(viewModel.SavesSection.Items);
        Assert.False(viewModel.ShowInstanceStartsEmpty);
    }

    [Fact]
    public async Task InstanceStartsEmpty_HiddenWhenTheProfileIsEmptyOrTheInstanceIsNot()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Empty", InstanceSource.Custom.Value);
        var played = (await harness.Services.Instances.CreateAsync("Played", InstanceSource.Custom.Value)).Instance;
        WriteItem(harness.Services.Paths.GetInstanceVehiclesFolder(played.InstanceId), "Rocket", "2026-08-10T06:44:36.6429982", "v2026.8.3.5117", 10, "vehicle.xml");

        await OpenAsync(harness, "Empty");
        Assert.False(viewModel.ShowInstanceStartsEmpty);

        WriteItem(Path.Combine(harness.Services.Paths.GetSharedProfileRoot(), "saves"), "Orbit", "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", 10);
        await OpenAsync(harness, "Played");
        Assert.False(viewModel.ShowInstanceStartsEmpty);

        await OpenAsync(harness, "Empty");
        Assert.True(viewModel.ShowInstanceStartsEmpty);
    }

    [Fact]
    public async Task GameProfileInfoText_NamesTheFolderWithTheUserFolderShortened()
    {
        var folder = "BoreaAppTest_" + Guid.NewGuid();
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), folder);
        string expected;
        string? text;
        bool created;
        try
        {
            using var harness = await ViewModelHarness.CreateAsync(sharedProfileRoot: Path.Combine(root, "GameProfile"));
            expected = harness.Localization.FormatGameSaveProfileInfo(Path.Combine("~", folder, "GameProfile"));
            text = harness.ViewModel.GameProfileInfoText;
            created = Directory.Exists(root);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        Assert.Equal(expected, text);
        Assert.False(created);
    }

    [Fact]
    public async Task Delete_AfterConfirmation_MovesTheFolderIntoTheBackups()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
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
        Assert.Equal(harness.Localization.FormatGameSaveDeleted("Rocket"), harness.ViewModel.Toasts.Items[^1].Message);
    }

    [Fact]
    public async Task LockedFile_CopyFailsNamingTheFile()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var paths = harness.Services.Paths;
        var first = (await harness.Services.Instances.CreateAsync("First", InstanceSource.Custom.Value)).Instance;
        var second = (await harness.Services.Instances.CreateAsync("Second", InstanceSource.Custom.Value)).Instance;
        var saves = paths.GetInstanceSavesFolder(first.InstanceId);
        WriteItem(saves, "Orbit", "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", 10);
        await OpenAsync(harness, "First");
        var section = harness.ViewModel.SavesSection;
        var row = Assert.Single(section.Items);
        var file = Path.Combine(saves, "Orbit", "universe.xml");
        using var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        row.BeginCopyCommand.Execute(null);
        await row.CopyCommand.ExecuteAsync(null);

        var toast = harness.ViewModel.Toasts.Items[^1];
        Assert.Equal(harness.Localization.FormatToastCopyFailed("Orbit"), toast.Message);
        Assert.Contains(file, toast.Detail);
        Assert.Null(section.Error);
        Assert.False(Directory.Exists(Path.Combine(paths.GetInstanceSavesFolder(second.InstanceId), "Orbit")));
    }

    [WindowsFact("Only Windows refuses to move a folder while a handle below it is open.")]
    public async Task LockedFile_DeleteFailsNamingTheFolderAndKeepsTheRow()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var saves = harness.Services.Paths.GetInstanceSavesFolder(instance.InstanceId);
        WriteItem(saves, "Orbit", "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", 10);
        await OpenAsync(harness, "Main");
        var section = harness.ViewModel.SavesSection;
        var row = Assert.Single(section.Items);
        using var locked = new FileStream(Path.Combine(saves, "Orbit", "universe.xml"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        row.BeginDeleteCommand.Execute(null);
        await row.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatToastDeleteFailed("Orbit"), harness.ViewModel.Toasts.Items[^1].Message);
        Assert.Contains(Path.Combine(saves, "Orbit"), harness.ViewModel.Toasts.Items[^1].Detail);
        Assert.True(Directory.Exists(Path.Combine(saves, "Orbit")));
        Assert.Single(section.Items);
    }

    [Fact]
    public async Task Delete_WhileBoreaRunsTheInstance_RefusesWithCloseTheGame()
    {
        var starter = new RunningGameStarter();
        using var harness = await ViewModelHarness.CreateAsync(WithStarMap, processStarter: starter);
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var saves = harness.Services.Paths.GetInstanceSavesFolder(instance.InstanceId);
        WriteItem(saves, "Orbit", "2026-08-01T14:34:32.4054896", "v2026.8.3.5117", 10);
        starter.GameLogPath = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        await OpenAsync(harness, "Main");
        await viewModel.PlayCommand.ExecuteAsync(null);
        Assert.True(harness.Services.Launcher.IsRunning(instance.InstanceId));
        var row = Assert.Single(viewModel.SavesSection.Items);

        row.BeginDeleteCommand.Execute(null);
        await row.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.GameSaveCloseGame, viewModel.Toasts.Items[^1].Detail);
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

    internal static Task WithStarMap(BoreaServices services)
    {
        var loader = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Loaders", "StarMap")).FullName;
        File.WriteAllBytes(Path.Combine(loader, "StarMap.exe"), []);
        return services.SettingsRepository.SaveAsync(services.Settings.WithLoaderInstallation(
            "StarMap",
            new LoaderInstallation(loader, ModVersion.Parse("0.4.6"), rawVersion: null, isAdopted: false)));
    }

    /// <summary>Hands out a loader process that keeps running and whose game writes its log at once, so the launch watch ends early.</summary>
    internal sealed class RunningGameStarter : IProcessStarter
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
