using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Borea.App.Localization;
using Borea.App.ViewModels;
using Borea.Core.Dependencies;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.Settings;

namespace Borea.App.Tests.ViewModels;

public sealed class ContentUpdateTests
{
    private const string OwnId = ViewModelHarness.FakeSpaceDock.OwnId;
    private const string ArchiveHost = "archives.test";

    [Fact]
    public async Task Open_NewerRelease_ShowsTheUpdate()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea, version: "0.7.4");
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();

        var rows = viewModel.ContentGroups.Single().Items;
        var afc = rows.Single(row => row.ModId == "AdvancedFlightComputer");
        Assert.True(afc.HasUpdate);
        Assert.Equal("0.7.5", afc.UpdateVersion);
        Assert.Equal(harness.Localization.FormatContentUpdateTo("0.7.5"), afc.UpdateText);
        Assert.False(rows.Single(row => row.ModId == "MeasureTools").HasUpdate);
        Assert.True(viewModel.HasUpdates);
    }

    [Fact]
    public async Task Open_ReleaseWhoseDownloadIsGone_IsNoUpdate_AndAnInstalledCopyStaysWithANote()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: snapshot =>
            ViewModelHarness.MarkGone("AdvancedFlightComputer", "0.7.5")(ViewModelHarness.MarkGone("MeasureTools", "1.1.10")(snapshot)));
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea, version: "0.7.4");
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, ownership: ModInstallOwnership.Borea, version: "1.1.10");
        await viewModel.LoadAsync();

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();

        var rows = viewModel.ContentGroups.Single().Items;
        var afc = rows.Single(row => row.ModId == "AdvancedFlightComputer");
        Assert.Null(afc.UpdateVersion);
        Assert.Null(afc.GoneText);
        var measureTools = rows.Single(row => row.ModId == "MeasureTools");
        Assert.Equal("1.1.10", measureTools.Version);
        Assert.False(measureTools.IsMissing);
        Assert.Equal(harness.Localization.FormatContentGone(MainViewModel.DateText(ViewModelHarness.GoneSince)), measureTools.GoneText);
        Assert.False(viewModel.HasUpdates);
        Assert.Equal(["0.7.4", "1.1.10"], (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods.OrderBy(mod => mod.ModId).Select(mod => mod.Version.ToString()));
    }

    [Fact]
    public async Task Open_ModBoreaDidNotInstall_ShowsNoUpdate()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, version: "0.7.4");
        await viewModel.LoadAsync();

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();

        Assert.False(viewModel.ContentGroups.Single().Items.Single().HasUpdate);
        Assert.False(viewModel.HasUpdates);
    }

    [Fact]
    public async Task Open_DependencyWithNewerRelease_OffersOnlyUpdateAll()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, InstallReason.Dependency, ModInstallOwnership.Borea, version: "0.7.4");
        await viewModel.LoadAsync();

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();

        var row = viewModel.ContentGroups.SelectMany(group => group.Items).Single();
        Assert.Equal("0.7.5", row.UpdateVersion);
        Assert.False(row.HasUpdate);
        Assert.True(viewModel.HasUpdates);
    }

    [Theory]
    [InlineData(ReleaseChannel.Stable, null)]
    [InlineData(ReleaseChannel.Testing, "1.1.0")]
    public async Task Open_TestingRelease_FollowsTheSavedChannel(ReleaseChannel channel, string? expected)
    {
        using var harness = await ViewModelHarness.CreateAsync(seed: services => services.SettingsRepository.SaveAsync(new BoreaSettings(null, releaseChannel: channel)));
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("1.1.0", ReleaseStatus.Testing)]);
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await viewModel.LoadAsync();

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();

        Assert.Equal(expected, viewModel.ContentGroups.Single().Items.Single().UpdateVersion);
    }

    [Fact]
    public async Task Home_CountsTheOwnedModsOfTheActiveInstanceWithANewerRelease()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("1.1.0")]);
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea, version: "0.7.4");
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, InstallReason.Dependency, ModInstallOwnership.Borea, version: "1.1.9");
        await InstalledContent.AddAsync(harness, OwnId, activate: true, version: "1.0.0");

        await viewModel.LoadAsync();
        await viewModel.WhenContentUpdatesCheckedAsync();

        Assert.True(viewModel.CurrentWindowHome);
        Assert.Equal(2, viewModel.ActiveInstanceUpdateCount);
        Assert.True(viewModel.HasActiveInstanceUpdates);
        Assert.Equal(harness.Localization.FormatHomeUpdates(2), viewModel.ActiveInstanceUpdatesText);
    }

    [Fact]
    public async Task Home_UpdateCountClick_OpensTheInstancePageWithTheUpdate()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea, version: "0.7.4");
        await viewModel.LoadAsync();
        await viewModel.WhenContentUpdatesCheckedAsync();
        Assert.Equal(1, viewModel.ActiveInstanceUpdateCount);

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();

        Assert.True(viewModel.CurrentWindowInstance);
        Assert.Equal(viewModel.ActiveInstance.InstanceId, viewModel.SelectedInstance?.InstanceId);
        Assert.Equal("0.7.5", viewModel.ContentGroups.Single().Items.Single().UpdateVersion);
        Assert.Equal(1, viewModel.ActiveInstanceUpdateCount);
    }

    [Fact]
    public async Task Home_UpdateCount_FollowsTheSavedChannel()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("1.1.0", ReleaseStatus.Testing)]);
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await viewModel.LoadAsync();
        await viewModel.WhenContentUpdatesCheckedAsync();
        Assert.Equal(0, viewModel.ActiveInstanceUpdateCount);

        viewModel.SelectedReleaseChannel = viewModel.OptionFor(ReleaseChannel.Testing);
        await viewModel.WhenReleaseChannelSavedAsync();
        await viewModel.WhenContentUpdatesCheckedAsync();

        Assert.Equal(1, viewModel.ActiveInstanceUpdateCount);
    }

    [Fact]
    public async Task Home_UpdateCount_FollowsTheActiveInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea, version: "0.7.4");
        await viewModel.LoadAsync();
        await viewModel.WhenContentUpdatesCheckedAsync();

        await viewModel.ActiveInstance!.ToggleActiveCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();

        Assert.Null(viewModel.ActiveInstance);
        Assert.Equal(0, viewModel.ActiveInstanceUpdateCount);

        await viewModel.Instances.Single().ActivateCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();

        Assert.Equal(1, viewModel.ActiveInstanceUpdateCount);
    }

    [Fact]
    public async Task Home_UpdateCount_ClearsAfterTheUpdate()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("1.1.0")]);
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await viewModel.LoadAsync();
        await viewModel.WhenContentUpdatesCheckedAsync();
        Assert.Equal(1, viewModel.ActiveInstanceUpdateCount);
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.Single().Items.Single();

        await row.UpdateCommand.ExecuteAsync(null);
        await row.ConfirmUpdateCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();
        viewModel.SetMainWindowHome();

        Assert.Equal(0, viewModel.ActiveInstanceUpdateCount);
        Assert.False(viewModel.HasActiveInstanceUpdates);
    }

    [Fact]
    public async Task Update_PastTheMaxOfARecordedSave_WaitsAndNamesTheSave()
    {
        var save = UserContentFixtures.SaveArchive("<Universe />");
        using var harness = await ViewModelHarness.CreateAsync(
            respond: request => request.RequestUri?.AbsoluteUri == UserContentFixtures.SaveUrl ? UserContentFixtures.ArchiveResponse(save) : null,
            typeFiles: UserContentFixtures.TypeFiles(Borea.Core.Index.ContentIndexTypeFiles.Saves, UserContentFixtures.EntryNeeding("CampaignSave", "save", "Campaign", UserContentFixtures.SaveUrl, save, UserContentFixtures.Required("AdvancedFlightComputer", "0.7.0", "0.7.4"), ["1.0.0"])));
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea, version: "0.7.4");
        var release = (await harness.Services.UserContent.GetReleaseAsync("CampaignSave", ModVersion.Parse("1.0.0")))!;
        Assert.True((await harness.Services.UserContentInstaller.InstallAsync(instance.InstanceId, release, name: null)).IsDone);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.Single().Items.Single();

        await row.UpdateCommand.ExecuteAsync(null);

        Assert.True(row.IsConfirmingUpdate);
        Assert.StartsWith("The save Campaign needs AdvancedFlightComputer 0.7.0 - 0.7.4. After this change", row.InstallWarning, StringComparison.Ordinal);
        Assert.Equal(ModVersion.Parse("0.7.4"), Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods).Version);
    }

    [Fact]
    public async Task Update_ReplacesTheModAndClearsTheUpdate()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("1.1.0")]);
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.Single().Items.Single();

        await row.UpdateCommand.ExecuteAsync(null);

        // without a game the compatibility is unknown, so the update waits for a confirmation
        Assert.True(row.IsConfirmingUpdate);
        await row.ConfirmUpdateCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();

        Assert.Equal(ModVersion.Parse("1.1.0"), Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods).Version);
        var updated = viewModel.ContentGroups.Single().Items.Single();
        Assert.Equal("1.1.0", updated.Version);
        Assert.False(updated.HasUpdate);
        Assert.Null(updated.InstallError);
        Assert.False(viewModel.HasUpdates);
    }

    [Fact]
    public async Task Update_DownloadFails_ShowsTheErrorOnTheReloadedRow()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: FailArchive);
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("1.1.0")]);
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.Single().Items.Single();

        await row.UpdateCommand.ExecuteAsync(null);
        await row.ConfirmUpdateCommand.ExecuteAsync(null);

        var reloaded = viewModel.ContentGroups.Single().Items.Single();
        Assert.NotSame(row, reloaded);
        Assert.Equal("1.0.0", reloaded.Version);
        Assert.NotNull(reloaded.InstallError);
        Assert.Contains(harness.Requests, uri => uri.Host == ArchiveHost);
    }

    [Fact]
    public async Task Update_PlannerConflict_ShowsOnTheRowAndChangesNothing()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        var missing = new ModDependency("MissingMod", ModDependencyKind.Required);
        harness.SpaceDock.Releases.AddRange([Release("1.0.0", dependencies: [missing]), Release("1.1.0", dependencies: [missing])]);
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();
        var row = viewModel.ContentGroups.Single().Items.Single();
        Assert.True(row.HasUpdate);

        await row.UpdateCommand.ExecuteAsync(null);

        Assert.Contains("MissingMod", row.InstallError);
        Assert.Null(row.PendingPlan);
        Assert.False(row.IsInstalling);
        Assert.Equal(ModVersion.Parse("1.0.0"), Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods).Version);
        Assert.DoesNotContain(harness.Requests, uri => uri.Host == ArchiveHost);
    }

    [Fact]
    public async Task Update_WithChoicesPastARecordedSave_RunsOnTheFirstGo()
    {
        var save = UserContentFixtures.SaveArchive("<Universe />");
        using var harness = await ViewModelHarness.CreateAsync(
            respond: request => request.RequestUri?.AbsoluteUri == UserContentFixtures.SaveUrl ? UserContentFixtures.ArchiveResponse(save) : ServeArchive(request),
            typeFiles: UserContentFixtures.TypeFiles(Borea.Core.Index.ContentIndexTypeFiles.Saves, UserContentFixtures.EntryNeeding("CampaignSave", "save", "Campaign", UserContentFixtures.SaveUrl, save, UserContentFixtures.Required(OwnId, null, "1.0.0"), ["1.0.0"])));
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("1.1.0", dependencies: [new ModDependency("kept", ModDependencyKind.Recommends)]), Release("1.0.0", modId: "kept")]);
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        var release = (await harness.Services.UserContent.GetReleaseAsync("CampaignSave", ModVersion.Parse("1.0.0")))!;
        Assert.True((await harness.Services.UserContentInstaller.InstallAsync(instance.InstanceId, release, name: null)).IsDone);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.Single().Items.Single();
        await row.UpdateCommand.ExecuteAsync(null);
        Assert.NotNull(row.Choices);
        Assert.Contains("Campaign", row.InstallWarning, StringComparison.Ordinal);

        // the go plans again with the choices and reads the save's record again
        await row.ConfirmUpdateCommand.ExecuteAsync(null);

        Assert.Equal(ModVersion.Parse("1.1.0"), (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods.Single(mod => mod.ModId == OwnId).Version);
    }

    [Fact]
    public async Task Update_NewRecommendation_WaitsOnTheRowAndInstallsWithTheUpdate()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("1.1.0", dependencies: [new ModDependency("kept", ModDependencyKind.Recommends)]), Release("1.0.0", modId: "kept")]);
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.Single().Items.Single();

        await row.UpdateCommand.ExecuteAsync(null);

        Assert.True(row.IsConfirmingUpdate);
        Assert.True(Assert.Single(row.Choices!.Recommended).IsSelected);
        Assert.Equal(ModVersion.Parse("1.0.0"), Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods).Version);

        await row.ConfirmUpdateCommand.ExecuteAsync(null);

        var mods = (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods;
        Assert.Equal(ModVersion.Parse("1.1.0"), mods.Single(mod => mod.ModId == OwnId).Version);
        Assert.Contains(mods, mod => mod.ModId == "kept");
    }

    [Fact]
    public async Task Update_ReloadWhileItRuns_KeepsTheRowAndBlocksOtherUpdates()
    {
        using var download = new ManualResetEventSlim();
        using var harness = await ViewModelHarness.CreateAsync(respond: HoldArchive(download));
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("1.1.0")]);
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.Single().Items.Single();
        await row.UpdateCommand.ExecuteAsync(null);

        var update = row.ConfirmUpdateCommand.ExecuteAsync(null);
        for (var wait = 0; wait < 300 && !harness.Requests.Any(uri => uri.Host == ArchiveHost); wait++)
            await Task.Delay(100);
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();

        Assert.Same(row, viewModel.ContentGroups.Single().Items.Single());
        Assert.True(row.IsInstalling);
        Assert.False(row.HasUpdate);
        Assert.False(viewModel.CanChangeContent);
        await viewModel.UpdateAll!.UpdateCommand.ExecuteAsync(null);
        Assert.Null(viewModel.UpdateAll.InstallWarning);

        download.Set();
        await update;

        Assert.True(viewModel.CanChangeContent);
        Assert.Equal("1.1.0", viewModel.ContentGroups.Single().Items.Single().Version);
    }

    [Fact]
    public async Task Update_AFolderAddedByHandWhileItRuns_AReopenDoesNotStopTheUpdate()
    {
        using var download = new ManualResetEventSlim();
        using var harness = await ViewModelHarness.CreateAsync(respond: HoldArchive(download));
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("1.1.0")]);
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var folder = Path.Combine(harness.Services.Paths.GetInstanceModsFolder(instance.InstanceId), "LocalOnly");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "mod.toml"), "name = \"LocalOnly\"");
        var row = viewModel.ContentGroups.Single().Items.Single();
        await row.UpdateCommand.ExecuteAsync(null);

        var update = row.ConfirmUpdateCommand.ExecuteAsync(null);
        for (var wait = 0; wait < 300 && !harness.Requests.Any(uri => uri.Host == ArchiveHost); wait++)
            await Task.Delay(100);
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        // the rows come from the record while the update runs, and the record does not know the folder yet
        Assert.Empty(viewModel.ManualInstallItems);

        download.Set();
        await update;

        Assert.DoesNotContain(viewModel.Toasts.Items, toast => toast.IsFailed);
        Assert.Equal(ModVersion.Parse("1.1.0"), (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods.Single(mod => mod.ModId == OwnId).Version);
        Assert.Equal(["LocalOnly"], viewModel.ManualInstallItems.Select(item => item.FolderName));
    }

    [Fact]
    public async Task Update_WhileItRuns_TheFoldersBoreaDidNotInstallStayAsTheyAre()
    {
        using var download = new ManualResetEventSlim();
        using var harness = await ViewModelHarness.CreateAsync(respond: HoldArchive(download));
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("1.1.0")]);
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        var modsFolder = harness.Services.Paths.GetInstanceModsFolder(instance.InstanceId);
        foreach (var name in new[] { "KSArmory", "MeasureTools" })
        {
            Directory.CreateDirectory(Path.Combine(modsFolder, name));
            File.WriteAllText(Path.Combine(modsFolder, name, "mod.toml"), $"name = \"{name}\"");
        }

        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var replaced = viewModel.ManualInstallItems.Single(item => item.FolderName == "KSArmory");
        var waiting = viewModel.ManualInstallItems.Single(item => item.FolderName == "MeasureTools");
        // a plan with a warning waits on the row for Install anyway
        await waiting.BeginReplaceCommand.ExecuteAsync(null);
        await waiting.ConfirmReplaceCommand.ExecuteAsync(null);
        Assert.NotNull(waiting.PendingPlan);
        var row = viewModel.ContentGroups.Single().Items.Single();
        await row.UpdateCommand.ExecuteAsync(null);

        var update = row.ConfirmUpdateCommand.ExecuteAsync(null);
        for (var wait = 0; wait < 300 && !harness.Requests.Any(uri => uri.Host == ArchiveHost); wait++)
            await Task.Delay(100);
        Assert.False(viewModel.CanChangeContent);
        await replaced.ManageCommand.ExecuteAsync(null);
        await replaced.BeginReplaceCommand.ExecuteAsync(null);
        await waiting.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Equal(ManualInstallCheck.NotChecked, replaced.Check);
        Assert.False(replaced.IsInstalling);
        Assert.Null(replaced.PendingPlan);
        Assert.Null(replaced.InstallWarning);
        Assert.Null(replaced.InstallError);
        Assert.False(waiting.IsInstalling);
        Assert.NotNull(waiting.PendingPlan);
        Assert.DoesNotContain(viewModel.Toasts.Items, toast => toast.IsFailed);

        download.Set();
        await update;

        Assert.True(File.Exists(Path.Combine(modsFolder, "KSArmory", "mod.toml")));
        Assert.True(File.Exists(Path.Combine(modsFolder, "MeasureTools", "mod.toml")));
        Assert.Equal([OwnId], (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods.Select(mod => mod.ModId));
    }

    [Fact]
    public async Task Manage_ReloadWhileItRuns_KeepsTheRowAndBlocksOtherChanges()
    {
        using var download = new ManualResetEventSlim();
        using var harness = await ViewModelHarness.CreateAsync(respond: HoldArchive(download));
        harness.SpaceDock.Releases.Add(Release("1.0.0"));
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, OwnId, activate: true, version: "1.0.0");
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.SelectMany(group => group.Items).Single();
        await row.BeginManageCommand.ExecuteAsync(null);

        var handover = row.ConfirmManageCommand.ExecuteAsync(null);
        for (var wait = 0; wait < 300 && !harness.Requests.Any(uri => uri.Host == ArchiveHost); wait++)
            await Task.Delay(100);
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        Assert.Same(row, viewModel.ContentGroups.SelectMany(group => group.Items).Single());
        Assert.True(row.IsInstalling);
        Assert.False(row.CanManage);
        Assert.False(viewModel.CanChangeContent);

        download.Set();
        await handover;

        Assert.True(viewModel.CanChangeContent);
        var managed = Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
        Assert.Equal(ModInstallOwnership.Borea, managed.Ownership);
    }

    [Fact]
    public async Task Manage_MissingRequiredDependency_NamesItAndInstallsItFirst()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        harness.SpaceDock.Releases.AddRange([Release("1.0.0", dependencies: [new ModDependency("library", ModDependencyKind.Required)]), Release("1.0.0", modId: "library")]);
        var instance = await InstalledContent.AddAsync(harness, OwnId, activate: true, version: "1.0.0");
        var row = await OpenSingleRowAsync(harness);

        await row.BeginManageCommand.ExecuteAsync(null);

        Assert.True(row.IsConfirmingManage);
        Assert.Contains("library", row.ManageMissingText);
        Assert.Equal(harness.Localization.FormatContentManageInstallMissing("library 1.0.0"), row.ManageInstallMissingText);
        Assert.True(row.InstallManageDependencies);

        await row.ConfirmManageCommand.ExecuteAsync(null);

        var mods = (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods;
        Assert.Equal(ModInstallOwnership.Borea, mods.Single(mod => mod.ModId == OwnId).Ownership);
        Assert.Equal(InstallReason.Dependency, mods.Single(mod => mod.ModId == "library").Reason);
    }

    [Fact]
    public async Task Manage_MissingRequiredDependencyDeselected_TakesOverWithoutIt()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        harness.SpaceDock.Releases.AddRange([Release("1.0.0", dependencies: [new ModDependency("library", ModDependencyKind.Required)]), Release("1.0.0", modId: "library")]);
        var instance = await InstalledContent.AddAsync(harness, OwnId, activate: true, version: "1.0.0");
        var row = await OpenSingleRowAsync(harness);
        await row.BeginManageCommand.ExecuteAsync(null);

        row.InstallManageDependencies = false;
        await row.ConfirmManageCommand.ExecuteAsync(null);

        var managed = Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
        Assert.Equal(ModInstallOwnership.Borea, managed.Ownership);
        Assert.DoesNotContain(harness.Requests, uri => uri.AbsolutePath.StartsWith("/library/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Manage_UnavailableRequiredDependency_SaysBoreaCannotInstallIt()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        harness.SpaceDock.Releases.Add(Release("1.0.0", dependencies: [new ModDependency("library", ModDependencyKind.Required)]));
        await InstalledContent.AddAsync(harness, OwnId, activate: true, version: "1.0.0");
        var row = await OpenSingleRowAsync(harness);

        await row.BeginManageCommand.ExecuteAsync(null);

        Assert.True(row.IsConfirmingManage);
        Assert.Null(row.ManageInstallMissingText);
        Assert.StartsWith(harness.Localization.FormatContentManageMissing("library"), row.ManageMissingText);
        Assert.Contains(harness.Localization.FormatContentManageCannotInstall(string.Empty), row.ManageMissingText);
    }

    [Fact]
    public async Task Manage_DependencyBoreaDoesNotManage_AsksToManageItFirst()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        harness.SpaceDock.Releases.AddRange([Release("1.0.0", dependencies: [new ModDependency("library", ModDependencyKind.Required, ModVersion.Parse("2.0.0"))]), Release("1.0.0", modId: "library"), Release("2.0.0", modId: "library")]);
        await InstalledContent.AddAsync(harness, OwnId, activate: true, version: "1.0.0");
        await InstalledContent.AddAsync(harness, "library", activate: true, version: "1.0.0");
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = harness.ViewModel.ContentGroups.SelectMany(group => group.Items).Single(item => item.ModId == OwnId);

        await row.BeginManageCommand.ExecuteAsync(null);

        Assert.True(row.IsConfirmingManage);
        Assert.Null(row.ManageInstallMissingText);
        Assert.EndsWith(harness.Localization.FormatContentManageNotOwned(harness.ViewModel.ContentName("library")), row.ManageMissingText);
    }

    [Fact]
    public async Task Manage_MissingOptionalDependency_ReportsItAndInstallsNothingElse()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        harness.SpaceDock.Releases.AddRange([Release("1.0.0", dependencies: [new ModDependency("extra", ModDependencyKind.Optional)]), Release("1.0.0", modId: "extra")]);
        var instance = await InstalledContent.AddAsync(harness, OwnId, activate: true, version: "1.0.0");
        var row = await OpenSingleRowAsync(harness);

        await row.BeginManageCommand.ExecuteAsync(null);

        Assert.Null(row.ManageMissingText);
        Assert.Null(row.ManageInstallMissingText);
        Assert.Equal(harness.Localization.FormatContentManageNotInstalled("extra"), row.ManageNotInstalledText);

        await row.ConfirmManageCommand.ExecuteAsync(null);

        var managed = Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
        Assert.Equal(ModInstallOwnership.Borea, managed.Ownership);
    }

    private static async Task<ContentItem> OpenSingleRowAsync(ViewModelHarness harness)
    {
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        return harness.ViewModel.ContentGroups.SelectMany(group => group.Items).Single();
    }

    [Fact]
    public async Task UpdateAll_PlansEveryOwnedModInOnePlan()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("1.1.0")]);
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea, version: "0.7.4");
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, ownership: ModInstallOwnership.Borea, version: "1.1.9");
        var instance = await InstalledContent.AddAsync(harness, OwnId, activate: true, version: "1.0.0");
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var updateAll = viewModel.UpdateAll!;

        await updateAll.UpdateCommand.ExecuteAsync(null);

        var plan = updateAll.PendingPlan!;
        Assert.NotNull(updateAll.InstallWarning);
        Assert.Equal(
            ["AdvancedFlightComputer 0.7.5", "MeasureTools 1.1.10"],
            plan.Operations.Select(operation => $"{operation.Release.ModId} {operation.Release.Version}"));

        updateAll.CancelUpdateCommand.Execute(null);

        Assert.Null(updateAll.PendingPlan);
        Assert.Null(updateAll.InstallWarning);
        Assert.Equal(
            ["0.7.4", "1.1.9", "1.0.0"],
            (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods.Select(mod => mod.Version.ToString()));
    }

    [Fact]
    public async Task Pin_UpdateAllAndItsOwnUpdate_LeaveThePinnedModAndTheOtherMoves()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("1.1.0"), Release("1.0.0", modId: "helper"), Release("1.1.0", modId: "helper")]);
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        var instance = await InstalledContent.AddAsync(harness, "helper", activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();
        var row = viewModel.ContentGroups.Single().Items.Single(item => item.ModId == OwnId);
        Assert.True(row.HasUpdate);
        Assert.True(row.CanPin);

        await row.PinCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();

        var pinned = viewModel.ContentGroups.Single().Items.Single(item => item.ModId == OwnId);
        Assert.True(pinned.IsPinned);
        Assert.False(pinned.CanPin);
        Assert.Equal(harness.Localization.FormatContentPinned("1.0.0"), pinned.PinnedText);
        Assert.Null(pinned.UpdateVersion);
        Assert.False(pinned.HasUpdate);
        Assert.Equal(1, viewModel.ActiveInstanceUpdateCount);

        await pinned.UpdateCommand.ExecuteAsync(null);
        Assert.Null(pinned.PendingPlan);
        Assert.Null(pinned.InstallError);

        var updateAll = viewModel.UpdateAll!;
        await updateAll.UpdateCommand.ExecuteAsync(null);
        Assert.Equal(["helper 1.1.0"], updateAll.PendingPlan!.Operations.Select(operation => $"{operation.Release.ModId} {operation.Release.Version}"));
        await updateAll.ConfirmUpdateCommand.ExecuteAsync(null);

        var mods = (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods;
        Assert.Equal(["1.0.0 pinned", "1.1.0 free"], mods.OrderBy(mod => mod.ModId).Select(mod => $"{mod.Version} {(mod.IsPinned ? "pinned" : "free")}"));
    }

    [Fact]
    public async Task Unpin_BringsTheUpdateOfferBack()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("1.1.0")]);
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await harness.Services.Instances.UpdateAsync(instance.InstanceId, saved => saved.SetPinned(OwnId, pinned: true));
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();
        var row = viewModel.ContentGroups.Single().Items.Single();
        Assert.True(row.IsPinned);
        Assert.False(row.HasUpdate);

        await row.UnpinCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();

        var unpinned = viewModel.ContentGroups.Single().Items.Single();
        Assert.False(unpinned.IsPinned);
        Assert.Null(unpinned.PinnedText);
        Assert.Equal("1.1.0", unpinned.UpdateVersion);
        Assert.True(unpinned.HasUpdate);
        Assert.False(Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods).IsPinned);
    }

    [Fact]
    public async Task Open_NewestReleaseNeedsANewerPinnedMod_NamesThePinAndOffersNoUpdate()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        // the pinned mod has a listing, so the note names it by its name and not by its id
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("1.1.0"), Release("1.0.0", modId: "other"), Release("2.0.0", modId: "other", dependencies: [new ModDependency(OwnId, ModDependencyKind.Required, ModVersion.Parse("1.1.0"))])]);
        var instance = await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await InstalledContent.AddAsync(harness, "other", activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await harness.Services.Instances.UpdateAsync(instance.InstanceId, saved => saved.SetPinned(OwnId, pinned: true));

        var row = await OpenRowAsync(harness, "other");

        Assert.Equal(harness.Localization.FormatContentHeldByPin("2.0.0", "Aircraft HUD >= 1.1.0", "1.0.0"), row.HeldText);
        Assert.Null(row.UpdateVersion);
        Assert.False(row.HasUpdate);
        Assert.False(harness.ViewModel.HasUpdates);
        Assert.Equal(0, harness.ViewModel.ActiveInstanceUpdateCount);
    }

    [Fact]
    public async Task Open_OlderNewerReleaseFitsThePin_OffersItWithoutANote()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("1.5.0", dependencies: [NeedsHelper("1.0.0")]), Release("2.0.0", dependencies: [NeedsHelper("1.1.0")]), Release("1.0.0", modId: "helper"), Release("1.1.0", modId: "helper")]);
        var instance = await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await InstalledContent.AddAsync(harness, "helper", activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await harness.Services.Instances.UpdateAsync(instance.InstanceId, saved => saved.SetPinned("helper", pinned: true));

        var row = await OpenRowAsync(harness, OwnId);

        Assert.Equal("1.5.0", row.UpdateVersion);
        Assert.True(row.HasUpdate);
        Assert.Null(row.HeldText);
        Assert.Equal(1, harness.ViewModel.ActiveInstanceUpdateCount);
    }

    [Fact]
    public async Task UpdateAll_ModHeldByAPin_UpdatesTheOthersAndKeepsTheNote()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("2.0.0", dependencies: [NeedsHelper("1.1.0")]), Release("1.0.0", modId: "helper"), Release("1.1.0", modId: "helper"), Release("1.0.0", modId: "other"), Release("1.1.0", modId: "other")]);
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await InstalledContent.AddAsync(harness, "helper", activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await InstalledContent.AddAsync(harness, "other", activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await harness.Services.Instances.UpdateAsync(instance.InstanceId, saved => saved.SetPinned("helper", pinned: true));
        Assert.NotNull((await OpenRowAsync(harness, OwnId)).HeldText);
        var updateAll = viewModel.UpdateAll!;

        await updateAll.UpdateCommand.ExecuteAsync(null);
        Assert.Null(updateAll.InstallError);
        Assert.Equal(["other 1.1.0"], updateAll.PendingPlan!.Operations.Select(operation => $"{operation.Release.ModId} {operation.Release.Version}"));
        await updateAll.ConfirmUpdateCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();

        var mods = (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods;
        Assert.Equal(["5000 1.0.0", "helper 1.0.0", "other 1.1.0"], mods.OrderBy(mod => mod.ModId, StringComparer.Ordinal).Select(mod => $"{mod.ModId} {mod.Version}"));
        var held = viewModel.ContentGroups.Single().Items.Single(item => item.ModId == OwnId);
        Assert.NotNull(held.HeldText);
        Assert.False(held.HasUpdate);
    }

    [Fact]
    public async Task Unpin_ThePinThatHoldsAnUpdate_TakesTheNoteAwayAndOffersTheUpdate()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("2.0.0", dependencies: [NeedsHelper("1.1.0")]), Release("1.0.0", modId: "helper"), Release("1.1.0", modId: "helper")]);
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await InstalledContent.AddAsync(harness, "helper", activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await harness.Services.Instances.UpdateAsync(instance.InstanceId, saved => saved.SetPinned("helper", pinned: true));
        Assert.NotNull((await OpenRowAsync(harness, OwnId)).HeldText);

        await viewModel.ContentGroups.Single().Items.Single(item => item.ModId == "helper").UnpinCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();

        var row = viewModel.ContentGroups.Single().Items.Single(item => item.ModId == OwnId);
        Assert.Null(row.HeldText);
        Assert.Equal("2.0.0", row.UpdateVersion);
        Assert.True(row.HasUpdate);
    }

    [Fact]
    public async Task Pin_OfTheModANewerReleaseNeeds_ShowsTheNote()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("2.0.0", dependencies: [NeedsHelper("1.1.0")]), Release("1.0.0", modId: "helper"), Release("1.1.0", modId: "helper")]);
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await InstalledContent.AddAsync(harness, "helper", activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        var row = await OpenRowAsync(harness, OwnId);
        Assert.Equal("2.0.0", row.UpdateVersion);
        Assert.Null(row.HeldText);

        await viewModel.ContentGroups.Single().Items.Single(item => item.ModId == "helper").PinCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();

        var held = viewModel.ContentGroups.Single().Items.Single(item => item.ModId == OwnId);
        Assert.NotNull(held.HeldText);
        Assert.Null(held.UpdateVersion);
    }

    [Fact]
    public async Task Open_InstanceWithoutPins_PlansEachModOnce()
    {
        var plans = new ConcurrentQueue<InstallPlanningRequest>();
        using var harness = await ViewModelHarness.CreateAsync(wrapInstallPlanner: inner => new RecordingPlanner(inner, plans));
        // the newest release needs a mod no source has, so the update stays at the installed release as it does under a pin
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("2.0.0", dependencies: [NeedsAbsent]), Release("1.0.0", modId: "helper"), Release("1.1.0", modId: "helper")]);
        await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await InstalledContent.AddAsync(harness, "helper", activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.WhenContentUpdatesCheckedAsync();
        plans.Clear();

        await harness.ViewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await harness.ViewModel.WhenContentUpdatesCheckedAsync();

        Assert.Equal(
            [$"{OwnId} 1.0.0 update", "helper 1.0.0 update"],
            plans.Select(plan => string.Join(", ", plan.Requested.Select(mod => $"{mod.Release.ModId} {mod.Release.Version} {(mod.Exact ? "exact" : "update")}"))).Order(StringComparer.Ordinal));
        var rows = harness.ViewModel.ContentGroups.Single().Items;
        Assert.Null(rows.Single(item => item.ModId == OwnId).UpdateVersion);
        Assert.Equal("1.1.0", rows.Single(item => item.ModId == "helper").UpdateVersion);
        Assert.All(rows, item => Assert.Null(item.HeldText));
    }

    [Fact]
    public async Task Open_NewestReleaseAlsoNeedsAModNoSourceHas_ShowsNoNote()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        // unpinning the helper would not bring 2.0.0, so a note that names the pin would be wrong
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("2.0.0", dependencies: [NeedsHelper("1.1.0"), NeedsAbsent]), Release("1.0.0", modId: "helper"), Release("1.1.0", modId: "helper")]);
        var instance = await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await InstalledContent.AddAsync(harness, "helper", activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await harness.Services.Instances.UpdateAsync(instance.InstanceId, saved => saved.SetPinned("helper", pinned: true));

        var row = await OpenRowAsync(harness, OwnId);

        Assert.Null(row.HeldText);
        Assert.Null(row.UpdateVersion);
    }

    [Fact]
    public async Task Open_ModHeldForAnotherReasonInAnInstanceWithAPin_PlansItOnce()
    {
        var plans = new ConcurrentQueue<InstallPlanningRequest>();
        using var harness = await ViewModelHarness.CreateAsync(wrapInstallPlanner: inner => new RecordingPlanner(inner, plans));
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("2.0.0", dependencies: [NeedsAbsent]), Release("1.0.0", modId: "helper"), Release("1.1.0", modId: "helper")]);
        var instance = await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await InstalledContent.AddAsync(harness, "helper", activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await harness.Services.Instances.UpdateAsync(instance.InstanceId, saved => saved.SetPinned("helper", pinned: true));

        var row = await OpenRowAsync(harness, OwnId);

        Assert.NotEmpty(plans);
        Assert.DoesNotContain(plans, plan => plan.Requested.Any(mod => mod.Exact));
        Assert.Null(row.HeldText);
    }

    [Fact]
    public async Task Home_InstanceWithAPinThatHoldsAnUpdate_CountsWithoutAnExactPlan()
    {
        var plans = new ConcurrentQueue<InstallPlanningRequest>();
        using var harness = await ViewModelHarness.CreateAsync(wrapInstallPlanner: inner => new RecordingPlanner(inner, plans));
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("2.0.0", dependencies: [NeedsHelper("1.1.0")]), Release("1.0.0", modId: "helper"), Release("1.1.0", modId: "helper")]);
        var instance = await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await InstalledContent.AddAsync(harness, "helper", activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await harness.Services.Instances.UpdateAsync(instance.InstanceId, saved => saved.SetPinned("helper", pinned: true));

        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.WhenContentUpdatesCheckedAsync();

        Assert.NotEmpty(plans);
        Assert.DoesNotContain(plans, plan => plan.Requested.Any(mod => mod.Exact));
        Assert.Equal(0, harness.ViewModel.ActiveInstanceUpdateCount);
    }

    private static async Task<ContentItem> OpenRowAsync(ViewModelHarness harness, string modId)
    {
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await harness.ViewModel.WhenContentUpdatesCheckedAsync();
        return harness.ViewModel.ContentGroups.Single().Items.Single(item => item.ModId == modId);
    }

    private static ModDependency NeedsHelper(string version) => new("helper", ModDependencyKind.Required, ModVersion.Parse(version));

    private static ModDependency NeedsAbsent => new("absent", ModDependencyKind.Required, ModVersion.Parse("1.0.0"));

    private sealed class RecordingPlanner(IInstallPlanner inner, ConcurrentQueue<InstallPlanningRequest> requests) : IInstallPlanner
    {
        public Task<InstallPlan> PlanAsync(InstallPlanningRequest request, CancellationToken cancellationToken = default)
        {
            requests.Enqueue(request);
            return inner.PlanAsync(request, cancellationToken);
        }
    }

    [Fact]
    public async Task ChangeVersion_OfAPinnedMod_StopsAndNamesThePin()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("1.1.0")]);
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await harness.Services.Instances.UpdateAsync(instance.InstanceId, saved => saved.SetPinned(OwnId, pinned: true));
        await viewModel.LoadAsync();
        await viewModel.OpenContentCommand.ExecuteAsync(new DiscoverItem(viewModel, (await harness.SpaceDock.GetListingAsync(OwnId))!));
        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);
        var newer = viewModel.ContentVersions.Single(row => row.Version == "1.1.0");

        await newer.InstallCommand.ExecuteAsync(null);

        var pin = new PlanningMessage(OwnId, PlanningMessageKind.Pinned) { Version = ModVersion.Parse("1.0.0"), OtherVersion = ModVersion.Parse("1.1.0") };
        Assert.Equal($"{OwnId}: {PlanningText.Message(pin)}", newer.InstallError);
        Assert.Equal(ModVersion.Parse("1.0.0"), Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods).Version);
        Assert.DoesNotContain(harness.Requests, uri => uri.Host == ArchiveHost);
    }

    [Fact]
    public async Task Pin_WhileAnUpdateOfTheInstanceRuns_ChangesNothing()
    {
        using var download = new ManualResetEventSlim();
        using var harness = await ViewModelHarness.CreateAsync(respond: HoldArchive(download));
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("1.1.0"), Release("1.0.0", modId: "helper")]);
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        var instance = await InstalledContent.AddAsync(harness, "helper", activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.Single().Items.Single(item => item.ModId == OwnId);
        var helper = viewModel.ContentGroups.Single().Items.Single(item => item.ModId == "helper");
        await row.UpdateCommand.ExecuteAsync(null);

        var update = row.ConfirmUpdateCommand.ExecuteAsync(null);
        for (var wait = 0; wait < 300 && !harness.Requests.Any(uri => uri.Host == ArchiveHost); wait++)
            await Task.Delay(100);
        await helper.PinCommand.ExecuteAsync(null);
        download.Set();
        await update;

        Assert.DoesNotContain(viewModel.Toasts.Items, toast => toast.IsFailed);
        Assert.DoesNotContain((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods, mod => mod.IsPinned);
        Assert.Equal("1.1.0", viewModel.ContentGroups.Single().Items.Single(item => item.ModId == OwnId).Version);
    }

    [Fact]
    public async Task UpdateAll_RecommendationOfAnUnchangedMod_AsksNothing()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        harness.SpaceDock.Releases.AddRange([Release("1.0.0", dependencies: [new ModDependency("declined", ModDependencyKind.Recommends)]), Release("1.0.0", modId: "declined")]);
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea, version: "0.7.4");
        await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var updateAll = viewModel.UpdateAll!;

        await updateAll.UpdateCommand.ExecuteAsync(null);

        Assert.Null(updateAll.Choices);
        Assert.Equal(["AdvancedFlightComputer 0.7.5"], updateAll.PendingPlan!.Operations.Select(operation => $"{operation.Release.ModId} {operation.Release.Version}"));
    }

    [Fact]
    public async Task UpdateAll_DownloadFails_ShowsTheErrorAfterTheReload()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: FailArchive);
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("1.1.0")]);
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var updateAll = viewModel.UpdateAll!;

        await updateAll.UpdateCommand.ExecuteAsync(null);
        await updateAll.ConfirmUpdateCommand.ExecuteAsync(null);

        Assert.NotSame(updateAll, viewModel.UpdateAll);
        Assert.NotNull(viewModel.UpdateAll!.InstallError);
        Assert.Contains(harness.Requests, uri => uri.Host == ArchiveHost);
    }

    [Fact]
    public async Task Update_ListsTheChangelogsOfThePassedVersionsNewestFirst()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        harness.SpaceDock.Releases.AddRange(
        [
            Release("1.0.0", changelog: "Installed."),
            Release("1.1.0", changelog: "- Fixes the HUD."),
            Release("1.2.0"),
            Release("1.3.0", changelog: "https://example.com/aircraft-hud/1.3.0"),
        ]);
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.Single().Items.Single();

        await row.UpdateCommand.ExecuteAsync(null);

        Assert.True(row.IsConfirmingUpdate);
        Assert.True(row.HasChangelogs);
        Assert.Equal([$"{row.Name} 1.3.0", $"{row.Name} 1.1.0"], row.Changelogs.Select(changelog => changelog.Title));
        Assert.Equal("https://example.com/aircraft-hud/1.3.0", row.Changelogs[0].Link?.Url);
        Assert.Equal("- Fixes the HUD.", row.Changelogs[1].Text);
        Assert.Equal(harness.Localization.UpdateAnyway, row.ConfirmUpdateText);

        row.CancelUpdateCommand.Execute(null);

        Assert.Empty(row.Changelogs);
        Assert.False(row.IsConfirmingUpdate);

        await row.UpdateCommand.ExecuteAsync(null);
        await row.ConfirmUpdateCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();

        var updated = viewModel.ContentGroups.Single().Items.Single();
        Assert.Equal("1.3.0", updated.Version);
        Assert.Empty(updated.Changelogs);
        Assert.Empty(row.Changelogs);
    }

    [Fact]
    public async Task UpdateAll_ListsTheChangelogsOfEveryUpdatedMod()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("1.1.0", changelog: "- Fixes the HUD.")]);
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea, version: "0.7.4");
        await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var updateAll = viewModel.UpdateAll!;

        await updateAll.UpdateCommand.ExecuteAsync(null);

        Assert.True(updateAll.IsConfirmingUpdate);
        Assert.Contains(updateAll.Changelogs, changelog => changelog.Link?.Url == "https://github.com/Maximilian-Nesslauer/KSA-AdvancedFlightComputer/releases/tag/v0.7.5");
        Assert.DoesNotContain(updateAll.Changelogs, changelog => changelog.Link?.Url.EndsWith("v0.7.4", StringComparison.Ordinal) == true);
        Assert.Contains(updateAll.Changelogs, changelog => changelog.Text == "- Fixes the HUD.");

        updateAll.CancelUpdateCommand.Execute(null);

        Assert.Empty(updateAll.Changelogs);
        Assert.False(updateAll.IsConfirmingUpdate);
    }

    [Fact]
    public async Task Update_PassedReleaseWhoseDownloadIsGone_StillShowsItsChangelog()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.MarkGone("AdvancedFlightComputer", "0.7.4"));
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea, version: "0.7.3");
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.Single().Items.Single();

        await row.UpdateCommand.ExecuteAsync(null);

        Assert.True(row.IsConfirmingUpdate);
        Assert.Equal(
            ["https://github.com/Maximilian-Nesslauer/KSA-AdvancedFlightComputer/releases/tag/v0.7.5", "https://github.com/Maximilian-Nesslauer/KSA-AdvancedFlightComputer/releases/tag/v0.7.4"],
            row.Changelogs.Select(changelog => changelog.Link?.Url));
    }

    [Fact]
    public async Task Update_ChangelogTextFromTheIndex_IsShownInsteadOfTheLink()
    {
        const string Link = "\"changelog\": \"https://github.com/Maximilian-Nesslauer/KSA-AdvancedFlightComputer/releases/tag/v0.7.5\",";
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => json.Replace(
            Link,
            Link + " \"changelog_text\": \"## Changes\\n- Marks the mod as compatible with KSA 2026.9.7.5402.\","));
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea, version: "0.7.4");
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.Single().Items.Single();

        await row.UpdateCommand.ExecuteAsync(null);

        var changelog = Assert.Single(row.Changelogs);
        Assert.Equal("## Changes\n- Marks the mod as compatible with KSA 2026.9.7.5402.", changelog.Text);
        Assert.Null(changelog.Link);
    }

    [Fact]
    public async Task Update_ChangelogLookupFails_StillHoldsThePlan()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("1.1.0", changelog: "- Fixes the HUD."), Release("1.2.0")]);
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();
        var row = viewModel.ContentGroups.Single().Items.Single();

        // the planner reads 1.1.0 first, and the changelog lookup reads it second
        var reads = 0;
        harness.SpaceDock.ReleaseFailure = version => version == ModVersion.Parse("1.1.0") && ++reads == 2 ? new JsonException("Not JSON.") : null;
        await row.UpdateCommand.ExecuteAsync(null);

        Assert.Equal(2, reads);
        Assert.NotNull(row.PendingPlan);
        Assert.Null(row.InstallError);
        Assert.Empty(row.Changelogs);
    }

    internal static ModVersionMetadata Release(string version, ReleaseStatus status = ReleaseStatus.Stable, IReadOnlyList<ModDependency>? dependencies = null, string? changelog = null, string modId = OwnId) => new(
        specVersion: 1,
        modId: modId,
        version: ModVersion.Parse(version),
        releaseStatus: status,
        releaseDate: DateTimeOffset.UnixEpoch,
        gameMin: "2026.1.1.1",
        gameMinRevision: 1,
        download: new DownloadInfo($"https://{ArchiveHost}/{modId}/{version}.zip", sha256: null, sizeBytes: null, contentType: "application/zip"),
        installSizeBytes: null,
        dependencies: dependencies ?? [],
        changelog: changelog);

    /// <summary>Serves a zip with a mod.toml at its root, named for the mod the archive URL names.</summary>
    internal static HttpResponseMessage? ServeArchive(HttpRequestMessage request)
    {
        if (request.RequestUri?.Host != ArchiveHost)
            return null;

        var modId = request.RequestUri.Segments[1].TrimEnd('/');
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("mod.toml").Open(), Encoding.UTF8);
            writer.Write($"name = \"{modId}\"");
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(buffer.ToArray()) };
    }

    /// <summary>Serves archives as <see cref="ServeArchive"/> does, and holds each archive download until <paramref name="download"/> is set.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage?> HoldArchive(ManualResetEventSlim download) => request =>
    {
        if (request.RequestUri?.Host == ArchiveHost)
            download.Wait(TimeSpan.FromSeconds(30));
        return ServeArchive(request);
    };

    private static HttpResponseMessage? FailArchive(HttpRequestMessage request)
        => request.RequestUri?.Host == ArchiveHost ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : null;
}
