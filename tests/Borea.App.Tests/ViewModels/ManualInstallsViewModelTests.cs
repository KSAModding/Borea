using Borea.App.ViewModels;
using Borea.Core.Dependencies;
using Borea.Core.History;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Storage.Logging;

namespace Borea.App.Tests.ViewModels;

public sealed class ManualInstallsViewModelTests
{
    private const string OwnId = ViewModelHarness.FakeSpaceDock.OwnId;

    [Fact]
    public void InstanceTabs_AreContentGameDataAndLog()
    {
        Assert.Equal([InstanceTab.Content, InstanceTab.GameData, InstanceTab.Log], Enum.GetValues<InstanceTab>());
    }

    [Fact]
    public async Task OpenInstance_ListsForeignFoldersWithTheirIndexStateOnTheContentTab()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        WriteForeignMod(harness, instance, "AdvancedFlightComputer");
        WriteForeignMod(harness, instance, "LocalOnly");
        Directory.CreateDirectory(Path.Combine(harness.Services.Paths.GetInstanceModsFolder(instance.InstanceId), "NotAMod"));

        await OpenAsync(harness, "Main");

        Assert.True(viewModel.IsContentTab);
        Assert.True(viewModel.HasManualInstalls);
        Assert.True(viewModel.ShowManualInstalls);
        Assert.Empty(viewModel.ContentGroups);
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
    public async Task OpenInstance_ModsBoreaRecorded_AreNotListed()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true, ownership: ModInstallOwnership.Borea);

        await OpenAsync(harness, "Main");

        Assert.Empty(viewModel.ManualInstallItems);
        Assert.False(viewModel.HasManualInstalls);
        Assert.False(viewModel.ShowManualInstalls);
    }

    [Fact]
    public async Task OpenInstance_TheScanFails_ShowsTheTableWithTheError()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        // the scan reads the instance record, so it fails once the record is gone
        await harness.Services.Instances.DeleteAsync(viewModel.Instances.Single().InstanceId);

        await viewModel.Instances.Single().OpenCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasManualInstalls);
        Assert.NotNull(viewModel.ManualInstallsError);
        Assert.True(viewModel.ShowManualInstalls);
    }

    [Fact]
    public async Task OpenInstanceAgain_KeepsTheRowThatIsBusyAndReadsTheOthersAgain()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var replaced = WriteForeignMod(harness, instance, "KSArmory");
        var idle = WriteForeignMod(harness, instance, "LocalOnly");
        await OpenAsync(harness, "Main");
        // the update check and the size walk of the page read in the mods folder, and Windows does not move a folder that is open
        await harness.WhenIdleAsync();
        var busy = viewModel.ManualInstallItems.Single(item => item.FolderName == "KSArmory");
        busy.IsInstalling = true;
        // a replace moves the folder aside while it installs
        Directory.Move(replaced, Path.Combine(harness.Services.Paths.GetInstanceRoot(instance.InstanceId), "KSArmory-aside"));
        Directory.Delete(idle, recursive: true);
        WriteForeignMod(harness, instance, "MeasureTools");

        await viewModel.Instances.Single().OpenCommand.ExecuteAsync(null);

        Assert.Equal(["KSArmory", "MeasureTools"], viewModel.ManualInstallItems.Select(item => item.FolderName));
        Assert.Same(busy, viewModel.ManualInstallItems[0]);
    }

    [Fact]
    public async Task OpenInstanceAgain_ARowThatTurnsBusyWhileTheRowsLoad_StaysOnThePage()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        WriteForeignMod(harness, instance, "KSArmory");
        await OpenAsync(harness, "Main");
        var row = viewModel.ManualInstallItems.Single();

        // the held lock makes the scan wait, and the row starts its replace meanwhile
        Task open;
        using (var held = Locks(harness).TryHold([instance.InstanceId]))
        {
            Assert.NotNull(held);
            open = viewModel.Instances.Single().OpenCommand.ExecuteAsync(null);
            await Task.WhenAny(open, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.False(open.IsCompleted);
            row.IsInstalling = true;
        }

        await open;

        Assert.Same(row, viewModel.ManualInstallItems.Single());
    }

    [Fact]
    public async Task OpenAnotherInstance_ShowsOnlyItsOwnFolders()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var main = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.CreateAsync("Second", InstanceSource.Custom.Value);
        WriteForeignMod(harness, main, "KSArmory");
        await OpenAsync(harness, "Main");
        viewModel.ManualInstallItems.Single().IsInstalling = true;

        await viewModel.Instances.Single(instance => instance.Name == "Second").OpenCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.ManualInstallItems);
        Assert.False(viewModel.ShowManualInstalls);
    }

    [Fact]
    public async Task OpenInstance_WhileTheLibraryFolderMoves_OpensAtOnceWithTheRecordedFolders()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        WriteForeignMod(harness, instance, "KSArmory");
        await OpenAsync(harness, "Main");
        WriteForeignMod(harness, instance, "LocalOnly");

        // a move holds the lock of every instance until it ends
        bool opened;
        using (var held = Locks(harness).TryHold([instance.InstanceId]))
        {
            Assert.NotNull(held);
            viewModel.IsChangingLibraryFolder = true;
            var open = viewModel.Instances.Single().OpenCommand.ExecuteAsync(null);
            opened = await Task.WhenAny(open, Task.Delay(TimeSpan.FromSeconds(10))) == open;
        }

        Assert.True(opened);
        Assert.True(viewModel.CurrentWindowInstance);
        Assert.Equal(["KSArmory"], viewModel.ManualInstallItems.Select(item => item.FolderName));
        Assert.Null(viewModel.ManualInstallsError);
    }

    [Fact]
    public async Task Manage_DownloadFails_ShowsAnErrorToastAndKeepsTheFolderForeign()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var folder = WriteForeignMod(harness, instance, "KSArmory");
        await OpenAsync(harness, "Main");
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
    public async Task Replace_AnInstalledModDeclaresAConflict_ShowsTheModalAndPlansAgainAfterTheRemoval()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ConflictDialogTests.Declares("MeasureTools", "KSArmory"));
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, ownership: ModInstallOwnership.Borea);
        var folder = WriteForeignMod(harness, instance, "KSArmory");
        await OpenAsync(harness, "Main");
        var row = viewModel.ManualInstallItems.Single();
        await row.BeginReplaceCommand.ExecuteAsync(null);

        await row.ConfirmReplaceCommand.ExecuteAsync(null);

        var conflict = Assert.Single(viewModel.ConflictDialog!.Rows);
        Assert.Equal(harness.Localization.FormatConflictDeclared(viewModel.ContentName("MeasureTools"), viewModel.ContentName("KSArmory")), conflict.Text);

        await conflict.RemoveCommand.ExecuteAsync(null);

        Assert.True(Directory.Exists(folder));
        Assert.True(viewModel.ManualInstallItems.Single().PendingPlan is { IsReady: true });
    }

    [Fact]
    public async Task Replace_FirstTime_AsksBeforeAnythingIsDeleted()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var folder = WriteForeignMod(harness, instance, "KSArmory");
        await OpenAsync(harness, "Main");
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
        Assert.True(viewModel.IsContentTab);
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
    public async Task Replace_WaitsForTheLoadOfAPreviewThatClosed_BeforeTheFolderMoves()
    {
        using var harness = await UserContentViewModelTests.VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var folder = WriteForeignMod(harness, instance, "KSArmory");
        var (service, held) = await VehiclePreviewViewModelTests.OpenRoverWithHeldLoadAsync(viewModel);
        await OpenAsync(harness, "Main");
        var row = viewModel.ManualInstallItems.Single();
        await row.BeginReplaceCommand.ExecuteAsync(null);
        await row.ConfirmReplaceCommand.ExecuteAsync(null);

        var install = row.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(folder, "mod.toml")));
        Assert.Empty(RecoveryFolders(harness, instance));
        Assert.False(install.IsCompleted);

        held.SetResult(service.Ready(new FakeAssets()));
        await install;

        // the download of the test fails, so the folder is back
        Assert.True(File.Exists(Path.Combine(folder, "mod.toml")));
        Assert.Empty(RecoveryFolders(harness, instance));
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

    [Fact]
    public async Task Replace_WhileTheInstancePageHoldsAFileOfTheFolder_WaitsForTheReadAndReplaces()
    {
        var hold = new FileHold();
        using var harness = await HoldingHarnessAsync(hold);
        var viewModel = harness.ViewModel;
        var (instance, row) = await ConfirmingReplaceWhileHeldAsync(harness, hold);

        var install = row.ConfirmInstallCommand.ExecuteAsync(null);
        try
        {
            await Task.WhenAny(install, Task.Delay(TimeSpan.FromSeconds(1)));

            Assert.False(install.IsCompleted);
            Assert.True(File.Exists(hold.File));
        }
        finally
        {
            // the harness waits for the update check when it ends, so a failed assert still lets the read go
            hold.Release.SetResult();
        }

        await install;

        Assert.Equal(TaskState.Finished, Assert.Single(viewModel.Tasks.History, task => task.Kind == TaskKind.ManualReplace).State);
        Assert.False(viewModel.Toasts.Items[^1].IsFailed);
        var saved = await harness.Services.Instances.GetByIdAsync(instance.InstanceId);
        Assert.Equal(ModInstallOwnership.Borea, saved!.Mods.Single(mod => mod.ModId == OwnId).Ownership);
        Assert.Empty(RecoveryFolders(harness, instance));
        Assert.Empty(viewModel.ManualInstallItems);
    }

    [Fact]
    public async Task Replace_WindowClosesWhileItWaitsForTheInstancePage_StopsAtOnceAndKeepsTheFolder()
    {
        var hold = new FileHold();
        using var harness = await HoldingHarnessAsync(hold);
        var viewModel = harness.ViewModel;
        var (instance, row) = await ConfirmingReplaceWhileHeldAsync(harness, hold);

        var install = row.ConfirmInstallCommand.ExecuteAsync(null);
        var stop = viewModel.StopInstallsAsync();
        var first = await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(30)));
        hold.Release.SetResult();
        await install;

        Assert.Same(stop, first);
        Assert.Equal(TaskState.Stopped, Assert.Single(viewModel.Tasks.History, task => task.Kind == TaskKind.ManualReplace).State);
        Assert.True(File.Exists(hold.File));
        Assert.Empty(RecoveryFolders(harness, instance));
    }

    [WindowsFact("Only Windows refuses to move a folder while a handle below it is open.")]
    public async Task Replace_AnotherProgramHoldsAFileOfTheFolder_FailsWithAccessDeniedAndKeepsTheFolder()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ContentUpdateTests.ServeArchive);
        harness.SpaceDock.Releases.Add(ContentUpdateTests.Release("1.0.0"));
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var folder = WriteForeignMod(harness, instance, OwnId);
        await OpenAsync(harness, "Main");
        var row = viewModel.ManualInstallItems.Single();
        await row.BeginReplaceCommand.ExecuteAsync(null);
        await row.ConfirmReplaceCommand.ExecuteAsync(null);

        IOException denied;
        using (new FileStream(Path.Combine(folder, "mod.toml"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await row.ConfirmInstallCommand.ExecuteAsync(null);
            denied = Assert.ThrowsAny<IOException>(() => Directory.Move(folder, folder + "-moved"));
        }

        Assert.Equal(TaskState.Failed, Assert.Single(viewModel.Tasks.History, task => task.Kind == TaskKind.ManualReplace).State);
        Assert.True(viewModel.Toasts.Items[^1].IsFailed);
        Assert.Equal(denied.Message, viewModel.Toasts.Items[^1].Detail);
        Assert.True(File.Exists(Path.Combine(folder, "mod.toml")));
        Assert.Empty(RecoveryFolders(harness, instance));
        Assert.Equal([OwnId], viewModel.ManualInstallItems.Select(item => item.FolderName));
    }

    private static string WriteForeignMod(ViewModelHarness harness, Instance instance, string folderName)
    {
        var folder = Path.Combine(harness.Services.Paths.GetInstanceModsFolder(instance.InstanceId), folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "mod.toml"), $"name = \"{folderName}\"");
        return folder;
    }

    private static IInstanceLocks Locks(ViewModelHarness harness)
        => (IInstanceLocks)((LoggingInstanceRepository)harness.Services.Instances).Inner;

    private static string[] RecoveryFolders(ViewModelHarness harness, Instance instance)
        => Directory.GetDirectories(harness.Services.Paths.GetInstanceRoot(instance.InstanceId), ".borea-recovery-*");

    private static async Task OpenAsync(ViewModelHarness harness, string name)
    {
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.Instances.Single(instance => instance.Name == name).OpenCommand.ExecuteAsync(null);
    }

    private static Task<ViewModelHarness> HoldingHarnessAsync(FileHold hold)
        => ViewModelHarness.CreateAsync(respond: ContentUpdateTests.ServeArchive, wrapInstallPlanner: inner => new HoldingPlanner(inner, hold));

    /// <summary>
    /// Opens the instance page with a foreign folder that the update check holds open, and confirms its take-over.
    /// </summary>
    private static async Task<(Instance Instance, ManualInstallItem Row)> ConfirmingReplaceWhileHeldAsync(ViewModelHarness harness, FileHold hold)
    {
        harness.SpaceDock.Releases.AddRange([ContentUpdateTests.Release("1.0.0"), ContentUpdateTests.Release("1.0.0", modId: "helper")]);
        var instance = await InstalledContent.AddAsync(harness, "helper", activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        hold.File = Path.Combine(WriteForeignMod(harness, instance, OwnId), "mod.toml");
        await OpenAsync(harness, "Main");
        await hold.Held.Task;
        var row = harness.ViewModel.ManualInstallItems.Single();
        await row.BeginReplaceCommand.ExecuteAsync(null);
        await row.ConfirmReplaceCommand.ExecuteAsync(null);
        return (instance, row);
    }

    private sealed class FileHold
    {
        private int _taken;

        public string File { get; set; } = string.Empty;

        public TaskCompletionSource Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Take() => Interlocked.Exchange(ref _taken, 1) == 0;
    }

    /// <summary>
    /// The update check of the instance page plans each mod Borea owns, so the first plan for helper
    /// holds the file open there until the test releases it, as a slow read of the folder does.
    /// </summary>
    private sealed class HoldingPlanner(IInstallPlanner inner, FileHold hold) : IInstallPlanner
    {
        public async Task<InstallPlan> PlanAsync(InstallPlanningRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Requested.Any(mod => mod.Release.ModId == "helper") && hold.Take())
            {
                using (new FileStream(hold.File, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    hold.Held.SetResult();
                    await hold.Release.Task;
                }
            }

            return await inner.PlanAsync(request, cancellationToken);
        }
    }
}
