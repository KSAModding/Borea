using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Borea.Core.Dependencies;
using Borea.Core.Mods;
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
        using var harness = await ViewModelHarness.CreateAsync(respond: request =>
        {
            if (request.RequestUri?.Host == ArchiveHost)
                download.Wait(TimeSpan.FromSeconds(30));
            return ServeArchive(request);
        });
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

        var plan = updateAll.PendingPlan;
        Assert.NotNull(plan);
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

    private static ModVersionMetadata Release(string version, ReleaseStatus status = ReleaseStatus.Stable, IReadOnlyList<ModDependency>? dependencies = null, string? changelog = null, string modId = OwnId) => new(
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
    private static HttpResponseMessage? ServeArchive(HttpRequestMessage request)
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

    private static HttpResponseMessage? FailArchive(HttpRequestMessage request)
        => request.RequestUri?.Host == ArchiveHost ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : null;
}
