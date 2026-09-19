using Borea.App.ViewModels;
using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

public sealed class ManualInstallsViewModelTests
{
    [Fact]
    public async Task ShowManualInstalls_ListsForeignFoldersWithTheirIndexState()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        WriteForeignMod(harness, instance, "AdvancedFlightComputer");
        WriteForeignMod(harness, instance, "LocalOnly");
        Directory.CreateDirectory(Path.Combine(harness.Services.Paths.GetInstanceModsFolder(instance.InstanceId), "NotAMod"));
        await OpenAsync(harness, "Main");

        await viewModel.ShowInstanceManualInstallsCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsManualInstallsTab);
        Assert.False(viewModel.IsContentTab);
        Assert.False(viewModel.IsGameDataTab);
        Assert.True(viewModel.HasManualInstalls);
        Assert.Null(viewModel.ManualInstallsError);
        Assert.Equal(["AdvancedFlightComputer", "LocalOnly"], viewModel.ManualInstallItems.Select(item => item.FolderName));
        var known = viewModel.ManualInstallItems[0];
        Assert.True(known.IsInIndex);
        Assert.True(known.CanAct);
        Assert.Equal(harness.Localization.ManualInstallsInIndex, known.StatusText);
        var local = viewModel.ManualInstallItems[1];
        Assert.False(local.IsInIndex);
        Assert.False(local.CanAct);
        Assert.Equal(harness.Localization.ManualInstallsNotInIndex, local.StatusText);

        harness.Localization.TrySetCulture("de");
        Assert.Equal("Nicht im Inhaltsindex", local.StatusText);
    }

    [Fact]
    public async Task ShowManualInstalls_ModsBoreaRecorded_AreNotListed()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true, ownership: ModInstallOwnership.Borea);
        await OpenAsync(harness, "Main");

        await viewModel.ShowInstanceManualInstallsCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.ManualInstallItems);
        Assert.False(viewModel.HasManualInstalls);
    }

    [Fact]
    public async Task Manage_DownloadFails_ShowsAnErrorToastAndKeepsTheFolderForeign()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var folder = WriteForeignMod(harness, instance, "KSArmory");
        await OpenAsync(harness, "Main");
        await viewModel.ShowInstanceManualInstallsCommand.ExecuteAsync(null);
        var row = viewModel.ManualInstallItems.Single();

        await row.ManageCommand.ExecuteAsync(null);

        var toast = Assert.Single(viewModel.Toasts.Items);
        Assert.Equal(harness.Localization.FormatToastCheckFailed("KSArmory"), toast.Message);
        Assert.True(toast.HasDetail);
        Assert.Null(row.InstallError);
        Assert.False(row.IsChecking);
        Assert.True(Directory.Exists(folder));
        var saved = await harness.Services.Instances.GetByIdAsync(instance.InstanceId);
        Assert.Empty(saved!.Mods);
        Assert.Equal("KSArmory", Assert.Single(saved.ForeignMods).FolderName);
    }

    [Fact]
    public async Task Replace_FirstTime_AsksBeforeAnythingIsDeleted()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var folder = WriteForeignMod(harness, instance, "KSArmory");
        await OpenAsync(harness, "Main");
        await viewModel.ShowInstanceManualInstallsCommand.ExecuteAsync(null);
        var row = viewModel.ManualInstallItems.Single();

        await row.BeginReplaceCommand.ExecuteAsync(null);

        Assert.True(row.IsConfirmingReplace);
        Assert.False(row.CanAct);
        Assert.Contains("KSArmory", row.ReplaceWarningText);
        Assert.Null(row.PendingPlan);

        row.CancelReplaceCommand.Execute(null);

        Assert.False(row.IsConfirmingReplace);
        Assert.True(Directory.Exists(folder));
        await viewModel.WhenPreferencesSavedAsync();
        Assert.False((await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames)).Preferences.ForeignFolderDeletionConfirmed);
    }

    [Fact]
    public async Task Replace_InstallFails_RestoresTheFolderAndDoesNotAskAgain()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var folder = WriteForeignMod(harness, instance, "KSArmory");
        WriteForeignMod(harness, instance, "MeasureTools");
        await OpenAsync(harness, "Main");
        await viewModel.ShowInstanceManualInstallsCommand.ExecuteAsync(null);
        var row = viewModel.ManualInstallItems.Single(item => item.FolderName == "KSArmory");
        await row.BeginReplaceCommand.ExecuteAsync(null);

        await row.ConfirmReplaceCommand.ExecuteAsync(null);

        // without a game the compatibility is unknown, so the plan waits and the folder stays until then
        Assert.NotNull(row.InstallWarning);
        Assert.NotNull(row.PendingPlan);
        Assert.True(Directory.Exists(folder));

        await row.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(folder, "mod.toml")));
        Assert.Empty(RecoveryFolders(harness, instance));
        Assert.True(viewModel.Toasts.Items[^1].IsFailed);
        Assert.Null(viewModel.ManualInstallsError);
        Assert.Equal(["KSArmory", "MeasureTools"], viewModel.ManualInstallItems.Select(item => item.FolderName));
        await viewModel.WhenPreferencesSavedAsync();
        Assert.True((await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames)).Preferences.ForeignFolderDeletionConfirmed);

        var next = viewModel.ManualInstallItems.Single(item => item.FolderName == "MeasureTools");
        await next.BeginReplaceCommand.ExecuteAsync(null);

        Assert.False(next.IsConfirmingReplace);
        Assert.NotNull(next.PendingPlan);
    }

    [Fact]
    public async Task Replace_PlanHasAConflict_KeepsTheFolder()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var folder = WriteForeignMod(harness, instance, "KSArmory");
        var rival = new ModVersionMetadata(
            specVersion: 1,
            modId: "Rival",
            version: ModVersion.Parse("1.0.0"),
            releaseStatus: ReleaseStatus.Stable,
            releaseDate: DateTimeOffset.Parse("2026-09-12T00:00:00Z"),
            gameMin: "2026.9.7.5402",
            gameMinRevision: 5402,
            download: new DownloadInfo("https://example.invalid/rival.zip", new string('A', 64), null, "application/zip"),
            installSizeBytes: null,
            dependencies: [new ModDependency("KSArmory", ModDependencyKind.Conflict)]);
        instance.AddMod(new InstalledMod("Rival", rival.Version, InstallReason.Manual, DateTimeOffset.UtcNow, rival, ownership: ModInstallOwnership.Foreign));
        await harness.Services.Instances.SaveAsync(instance);
        await OpenAsync(harness, "Main");
        await viewModel.ShowInstanceManualInstallsCommand.ExecuteAsync(null);
        var row = viewModel.ManualInstallItems.Single();
        await row.BeginReplaceCommand.ExecuteAsync(null);

        await row.ConfirmReplaceCommand.ExecuteAsync(null);

        Assert.NotNull(row.InstallError);
        Assert.Null(row.PendingPlan);
        Assert.False(row.IsInstalling);
        Assert.True(File.Exists(Path.Combine(folder, "mod.toml")));
    }

    [Fact]
    public async Task Replace_InstanceChangesBeforeTheInstall_KeepsTheFolder()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var folder = WriteForeignMod(harness, instance, "KSArmory");
        await OpenAsync(harness, "Main");
        await viewModel.ShowInstanceManualInstallsCommand.ExecuteAsync(null);
        var row = viewModel.ManualInstallItems.Single();
        await row.BeginReplaceCommand.ExecuteAsync(null);
        await row.ConfirmReplaceCommand.ExecuteAsync(null);
        Assert.NotNull(row.PendingPlan);

        await InstalledContent.AddAsync(harness, "MeasureTools", activate: false);
        await row.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.ManualInstallsInstanceChanged, viewModel.Toasts.Items[^1].Detail);
        Assert.Null(viewModel.ManualInstallsError);
        Assert.True(File.Exists(Path.Combine(folder, "mod.toml")));
        Assert.Empty(RecoveryFolders(harness, instance));
    }

    private static string WriteForeignMod(ViewModelHarness harness, Instance instance, string folderName)
    {
        var folder = Path.Combine(harness.Services.Paths.GetInstanceModsFolder(instance.InstanceId), folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "mod.toml"), $"name = \"{folderName}\"");
        return folder;
    }

    private static string[] RecoveryFolders(ViewModelHarness harness, Instance instance)
        => Directory.GetDirectories(harness.Services.Paths.GetInstanceRoot(instance.InstanceId), ".borea-recovery-*");

    private static async Task OpenAsync(ViewModelHarness harness, string name)
    {
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.Instances.Single(instance => instance.Name == name).OpenCommand.ExecuteAsync(null);
    }
}
