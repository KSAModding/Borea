using System.Globalization;
using Borea.App.ViewModels;
using Borea.Core.History;
using Borea.Core.Instances;
using Borea.Core.Preferences;

namespace Borea.App.Tests.ViewModels;

public sealed class BackupsViewModelTests
{
    [Fact]
    public async Task GameDataTab_ListsTheBackupsNewestFirst()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var backups = Path.Combine(harness.Services.Paths.GetBackupsRoot(), instance.InstanceId.ToString());
        WriteItem(Path.Combine(backups, "saves"), "Orbit-2026-09-01T080000Z", "Orbit", 10);
        WriteItem(Path.Combine(backups, "Vehicles"), "Rocket-2026-09-02T080000Z", "Rocket", 10);
        var loose = Directory.CreateDirectory(Path.Combine(backups, "saves", "copied by hand")).FullName;
        Directory.SetLastWriteTimeUtc(loose, new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc));

        await OpenGameDataAsync(harness, "Main");

        var items = harness.ViewModel.BackupItems;
        Assert.Equal(["Rocket", "Orbit", "copied by hand"], items.Select(item => item.Name));
        Assert.True(items[0].IsVehicle);
        var time = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        Assert.Equal(harness.Localization.FormatBackupMoved(time), items[1].TimeText);
        Assert.True(items[1].CanRestore);
        Assert.False(items[2].CanRestore);
        Assert.True(harness.ViewModel.HasBackups);
    }

    [Fact]
    public async Task Restore_OccupiedTarget_AsksAndThenReplaces()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var saves = harness.Services.Paths.GetInstanceSavesFolder(instance.InstanceId);
        WriteItem(saves, "Orbit", "Orbit", 300);
        await harness.Services.GameSaves.DeleteAsync(instance.InstanceId, Assert.Single(await harness.Services.GameSaves.ListAsync(instance.InstanceId, GameSaveKind.Save)));
        WriteItem(saves, "Orbit", "Orbit", 20);
        await OpenGameDataAsync(harness, "Main");
        var row = Assert.Single(harness.ViewModel.BackupItems);

        await row.RestoreCommand.ExecuteAsync(null);

        Assert.True(row.IsConfirmingReplace);
        Assert.Equal(harness.Localization.FormatGameSaveReplace("Orbit", "Main"), row.ReplaceText);
        Assert.Equal(20, new FileInfo(Path.Combine(saves, "Orbit", "universe.xml")).Length);
        Assert.DoesNotContain(harness.ViewModel.Tasks.History, task => task.Kind == TaskKind.BackupRestore);

        await row.ConfirmReplaceCommand.ExecuteAsync(null);

        Assert.Equal(300, new FileInfo(Path.Combine(saves, "Orbit", "universe.xml")).Length);
        var task = Assert.Single(harness.ViewModel.Tasks.History, task => task.Kind == TaskKind.BackupRestore);
        Assert.Equal(TaskState.Finished, task.State);
        Assert.Equal(harness.Localization.FormatToastBackupRestored("Orbit", "Main"), harness.ViewModel.Toasts.Items[^1].Message);
        var replaced = Assert.Single(harness.ViewModel.BackupItems);
        Assert.Equal(GameSaveBackupReason.Replaced, replaced.Backup.Reason);
        Assert.Equal(300, new FileInfo(Path.Combine(Assert.Single(harness.ViewModel.SavesSection.Items).Entry.Path, "universe.xml")).Length);
    }

    [Fact]
    public async Task Restore_FileInUse_FailsTheTaskAndChangesNothing()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var backups = Path.Combine(harness.Services.Paths.GetBackupsRoot(), instance.InstanceId.ToString(), "saves");
        WriteItem(backups, "Orbit-2026-09-01T080000Z", "Orbit", 10);
        await OpenGameDataAsync(harness, "Main");
        var row = Assert.Single(harness.ViewModel.BackupItems);

        using (new FileStream(Path.Combine(backups, "Orbit-2026-09-01T080000Z", "universe.xml"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await row.RestoreCommand.ExecuteAsync(null);

        var task = Assert.Single(harness.ViewModel.Tasks.History, task => task.Kind == TaskKind.BackupRestore);
        Assert.Equal(TaskState.Failed, task.State);
        Assert.Equal(harness.Localization.FormatToastBackupRestoreFailed("Orbit"), harness.ViewModel.Toasts.Items[^1].Message);
        Assert.Equal(harness.Localization.GameSaveCloseGame, harness.ViewModel.Toasts.Items[^1].Detail);
        Assert.True(Directory.Exists(Path.Combine(backups, "Orbit-2026-09-01T080000Z")));
        Assert.False(Directory.Exists(harness.Services.Paths.GetInstanceSavesFolder(instance.InstanceId)));
    }

    [Fact]
    public async Task Delete_AsksFirstAndThenDeletes()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var backups = Path.Combine(harness.Services.Paths.GetBackupsRoot(), instance.InstanceId.ToString(), "saves");
        WriteItem(backups, "Orbit-2026-09-01T080000Z", "Orbit", 10);
        await OpenGameDataAsync(harness, "Main");
        var row = Assert.Single(harness.ViewModel.BackupItems);

        row.BeginDeleteCommand.Execute(null);
        Assert.True(row.IsConfirmingDelete);
        Assert.True(Directory.Exists(Path.Combine(backups, "Orbit-2026-09-01T080000Z")));

        await row.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.Empty(Directory.GetFileSystemEntries(backups));
        Assert.Empty(harness.ViewModel.BackupItems);
        Assert.Equal(TaskState.Finished, Assert.Single(harness.ViewModel.Tasks.History, task => task.Kind == TaskKind.BackupDelete).State);
        Assert.Equal(harness.Localization.FormatToastBackupDeleted("Orbit"), harness.ViewModel.Toasts.Items[^1].Message);
    }

    [Fact]
    public async Task WhileTheGameRuns_DeleteWorksAndRestoreRefuses()
    {
        var starter = new GameSavesViewModelTests.RunningGameStarter();
        using var harness = await ViewModelHarness.CreateAsync(GameSavesViewModelTests.WithStarMap, processStarter: starter);
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var backups = Path.Combine(harness.Services.Paths.GetBackupsRoot(), instance.InstanceId.ToString(), "saves");
        WriteItem(backups, "Orbit-2026-09-01T080000Z", "Orbit", 10);
        WriteItem(backups, "Moon-2026-09-02T080000Z", "Moon", 10);
        starter.GameLogPath = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        await OpenGameDataAsync(harness, "Main");
        await harness.ViewModel.PlayCommand.ExecuteAsync(null);
        Assert.True(harness.Services.Launcher.IsRunning(instance.InstanceId));

        await harness.ViewModel.BackupItems.Single(item => item.Name == "Orbit").RestoreCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.GameSaveCloseGame, harness.ViewModel.Toasts.Items[^1].Detail);
        Assert.False(Directory.Exists(harness.Services.Paths.GetInstanceSavesFolder(instance.InstanceId)));

        await harness.ViewModel.BackupItems.Single(item => item.Name == "Moon").ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.Equal(["Orbit"], harness.ViewModel.BackupItems.Select(item => item.Name));
        Assert.Equal(TaskState.Finished, Assert.Single(harness.ViewModel.Tasks.History, task => task.Kind == TaskKind.BackupDelete).State);
    }

    [Fact]
    public async Task Restore_BackupGoneMeanwhile_FailsAndDropsTheRow()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var backups = Path.Combine(harness.Services.Paths.GetBackupsRoot(), instance.InstanceId.ToString(), "saves");
        WriteItem(backups, "Orbit-2026-09-01T080000Z", "Orbit", 10);
        await OpenGameDataAsync(harness, "Main");
        var row = Assert.Single(harness.ViewModel.BackupItems);
        Directory.Delete(Path.Combine(backups, "Orbit-2026-09-01T080000Z"), recursive: true);

        await row.RestoreCommand.ExecuteAsync(null);

        Assert.Equal(TaskState.Failed, Assert.Single(harness.ViewModel.Tasks.History, task => task.Kind == TaskKind.BackupRestore).State);
        Assert.Empty(harness.ViewModel.BackupItems);
        Assert.True(harness.ViewModel.ShowNoBackups);
    }

    [Fact]
    public async Task Start_RetentionOff_KeepsOldBackups()
    {
        var instanceId = Guid.Empty;
        using var harness = await ViewModelHarness.CreateAsync(async services =>
        {
            instanceId = (await services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance.InstanceId;
            WriteItem(Path.Combine(services.Paths.GetBackupsRoot(), instanceId.ToString(), "saves"), "Old-2020-01-01T000000Z", "Old", 10);
        });

        await harness.ViewModel.WhenBackupsCleanedAsync();

        Assert.Null(harness.ViewModel.SelectedBackupRetention.Days);
        Assert.Single(await harness.Services.GameSaveBackups.ListAsync(instanceId));
    }

    [Fact]
    public async Task Start_WithRetention_DeletesTheOlderBackupsAndLogsTheCount()
    {
        var instanceId = Guid.Empty;
        using var harness = await ViewModelHarness.CreateAsync(async services =>
        {
            instanceId = (await services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance.InstanceId;
            var saves = Path.Combine(services.Paths.GetBackupsRoot(), instanceId.ToString(), "saves");
            WriteItem(saves, "Old-2020-01-01T000000Z", "Old", 10);
            WriteItem(saves, $"New-{DateTimeOffset.UtcNow.AddDays(-1):yyyy-MM-dd'T'HHmmss'Z'}", "New", 10);
            await services.AppPreferences.SaveAsync(AppPreferences.Empty.WithBackupRetentionDays(30), MainViewModel.BundledThemeNames);
        });

        await harness.ViewModel.WhenBackupsCleanedAsync();

        Assert.Equal(30, harness.ViewModel.SelectedBackupRetention.Days);
        Assert.Equal(["New"], (await harness.Services.GameSaveBackups.ListAsync(instanceId)).Select(backup => backup.FolderName));
        Assert.Contains(harness.Services.Log.ReadRecentLines(20), line => line.EndsWith("Deleted 1 backups older than 30 days.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RetentionChange_SavesItAndDeletesTheOlderBackups()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        WriteItem(Path.Combine(harness.Services.Paths.GetBackupsRoot(), instance.InstanceId.ToString(), "saves"), "Old-2020-01-01T000000Z", "Old", 10);
        var viewModel = harness.ViewModel;

        Assert.Equal([null, 30, 90, 365], viewModel.BackupRetentionOptions.Select(option => option.Days));
        viewModel.SelectedBackupRetention = viewModel.BackupRetentionOptions.Single(option => option.Days == 365);
        await viewModel.WhenBackupsCleanedAsync();
        await viewModel.WhenPreferencesSavedAsync();

        Assert.Empty(await harness.Services.GameSaveBackups.ListAsync(instance.InstanceId));
        var saved = await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames);
        Assert.Equal(365, saved.Preferences.BackupRetentionDays);
        Assert.Equal(harness.Localization.FormatBackupRetentionDays(365), viewModel.SelectedBackupRetention.Text);
    }

    private static async Task OpenGameDataAsync(ViewModelHarness harness, string name)
    {
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.Instances.Single(instance => instance.Name == name).OpenCommand.ExecuteAsync(null);
        await harness.ViewModel.ShowInstanceGameDataCommand.ExecuteAsync(null);
    }

    private static void WriteItem(string parent, string folderName, string name, int dataBytes)
    {
        var folder = Directory.CreateDirectory(Path.Combine(parent, folderName)).FullName;
        File.WriteAllText(Path.Combine(folder, "meta.toml"), $"""
            name = "{name}"
            updated = 2026-08-01T14:34:32.4054896
            version = "v2026.8.3.5117"

            """);
        File.WriteAllBytes(Path.Combine(folder, "universe.xml"), new byte[dataBytes]);
    }
}
