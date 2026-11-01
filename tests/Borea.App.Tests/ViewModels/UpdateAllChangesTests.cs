using Borea.App.Localization;
using Borea.App.ViewModels;
using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.App.Tests.ViewModels;

/// <summary>What "Update all" shows while its plan waits for a confirmation.</summary>
public sealed class UpdateAllChangesTests
{
    private const string ArchiveHost = "archives.test";

    [Fact]
    public async Task Changes_NameEveryOperation_WithAModWithoutChangelogANewDependencyAndAnOlderRelease()
    {
        using var harness = await CreateWithGameAsync();
        harness.SpaceDock.Releases.AddRange(
        [
            Release("alpha", "1.0.0"), Release("alpha", "1.1.0", changelog: "- Fixes the HUD."),
            Release("beta", "1.0.0"), Release("beta", "1.1.0", dependencies: [new ModDependency("gamma", ModDependencyKind.Required)]),
            Release("gamma", "2.0.0"),
        ]);
        await InstallAsync(harness, ("beta", "1.0.0"), ("alpha", "1.0.0"));
        var updateAll = await OpenAsync(harness);

        await updateAll.UpdateCommand.ExecuteAsync(null);

        Assert.Equal(
            [("alpha", "1.0.0", "1.1.0", false, false), ("beta", "1.0.0", "1.1.0", false, false), ("gamma", null, "2.0.0", true, false)],
            updateAll.Changes.Select(change => (change.Name, change.FromVersion, change.ToVersion, change.IsNew, change.IsOlder)));
        Assert.Equal([false, true, false], updateAll.Changes.Select(change => change.HasNoNotes));

        updateAll.PendingPlan = PlanOf(updateAll.PendingPlan!, [Release("alpha", "0.9.0")]);

        Assert.Equal(("alpha", "1.0.0", "0.9.0", false, true), Assert.Single(updateAll.Changes.Select(change => (change.Name, change.FromVersion, change.ToVersion, change.IsNew, change.IsOlder))));
        Assert.False(updateAll.Changes.Single().HasNoNotes);
    }

    [Fact]
    public async Task Changes_GroupTheChangelogsByMod_NewestFirst_AndShowThemOnlyWhileOpen()
    {
        using var harness = await CreateWithGameAsync();
        harness.SpaceDock.Releases.AddRange(
        [
            Release("alpha", "1.0.0"), Release("alpha", "1.1.0", changelog: "- First."), Release("alpha", "1.2.0", changelog: "- Second."),
            Release("beta", "1.0.0"), Release("beta", "1.1.0", changelog: "https://example.com/beta/1.1.0"),
            Release("delta", "1.0.0"), Release("delta", "1.1.0", changelog: "- Only."),
        ]);
        await InstallAsync(harness, ("alpha", "1.0.0"), ("beta", "1.0.0"), ("delta", "1.0.0"));
        var updateAll = await OpenAsync(harness);

        await updateAll.UpdateCommand.ExecuteAsync(null);

        var alpha = updateAll.Changes.Single(change => change.Name == "alpha");
        Assert.Empty(alpha.ShownChangelogs);
        Assert.Equal(harness.Localization.FormatUpdateAllShowChanges("alpha"), alpha.ToggleText);

        alpha.ToggleCommand.Execute(null);

        Assert.Equal([("1.2.0", "- Second."), ("1.1.0", "- First.")], alpha.ShownChangelogs.Select(changelog => (changelog.Title, changelog.Text)));
        Assert.Equal(harness.Localization.FormatUpdateAllHideChanges("alpha"), alpha.ToggleText);

        var beta = updateAll.Changes.Single(change => change.Name == "beta");
        Assert.False(beta.CanExpand);
        Assert.Equal("https://example.com/beta/1.1.0", beta.Link?.Url);

        var delta = updateAll.Changes.Single(change => change.Name == "delta");
        delta.ToggleCommand.Execute(null);
        Assert.Equal(string.Empty, Assert.Single(delta.ShownChangelogs).Title);
    }

    [Fact]
    public async Task Changes_AVersionLabelStandsOnlyAboveATextThatDoesNotOpenWithThatVersion()
    {
        using var harness = await CreateWithGameAsync();
        harness.SpaceDock.Releases.AddRange(
        [
            Release("alpha", "1.0.0"),
            Release("alpha", "1.1.0", changelog: "## Fixes\n\n- First."),
            Release("alpha", "1.2.0", changelog: "\n# Alpha 1.2.0\n\n- Second."),
            Release("alpha", "1.3.0", changelog: "Alpha 1.3.0 is here."),
        ]);
        await InstallAsync(harness, ("alpha", "1.0.0"));
        var updateAll = await OpenAsync(harness);

        await updateAll.UpdateCommand.ExecuteAsync(null);

        var alpha = updateAll.Changes.Single();
        alpha.IsExpanded = true;
        Assert.Equal(["1.3.0", string.Empty, "1.1.0"], alpha.ShownChangelogs.Select(changelog => changelog.Title));
    }

    [Fact]
    public async Task Changes_OneModStartsOpenAndNeedsNoHint_SeveralStartFoldedWithTheHint()
    {
        using var single = await CreateWithGameAsync();
        single.SpaceDock.Releases.AddRange([Release("alpha", "1.0.0"), Release("alpha", "1.1.0", changelog: "- Fixes the HUD.")]);
        await InstallAsync(single, ("alpha", "1.0.0"));
        var one = await OpenAsync(single);

        await one.UpdateCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(one.Changes).IsExpanded);
        Assert.Equal(single.Localization.UpdateAllListHeading, one.ListHeadingText);

        using var several = await CreateWithGameAsync();
        several.SpaceDock.Releases.AddRange([Release("alpha", "1.0.0"), Release("alpha", "1.1.0", changelog: "- Fixes the HUD."), Release("beta", "1.0.0"), Release("beta", "1.1.0")]);
        await InstallAsync(several, ("alpha", "1.0.0"), ("beta", "1.0.0"));
        var two = await OpenAsync(several);

        await two.UpdateCommand.ExecuteAsync(null);

        Assert.All(two.Changes, change => Assert.False(change.IsExpanded));
        Assert.Equal(several.Localization.UpdateAllListHint, two.ListHeadingText);
    }

    [Fact]
    public async Task UpdateAgain_AfterACancel_StartsFolded()
    {
        using var harness = await CreateWithGameAsync();
        harness.SpaceDock.Releases.AddRange([Release("alpha", "1.0.0"), Release("alpha", "1.1.0", changelog: "- Fixes."), Release("beta", "1.0.0"), Release("beta", "1.1.0", changelog: "- Fixes.")]);
        await InstallAsync(harness, ("alpha", "1.0.0"), ("beta", "1.0.0"));
        var updateAll = await OpenAsync(harness);
        await updateAll.UpdateCommand.ExecuteAsync(null);
        updateAll.Changes[0].IsExpanded = true;

        updateAll.CancelUpdateCommand.Execute(null);
        await updateAll.UpdateCommand.ExecuteAsync(null);

        Assert.All(updateAll.Changes, change => Assert.False(change.IsExpanded));
    }

    [Theory]
    [InlineData("en", 0, false, "Update the mods in Main", "Update")]
    [InlineData("en", 0, true, "Update the mods in Main", "Update anyway")]
    [InlineData("en", 1, false, "Update 1 mod in Main", "Update 1 mod")]
    [InlineData("en", 1, true, "Update 1 mod in Main", "Update 1 mod anyway")]
    [InlineData("en", 3, false, "Update 3 mods in Main", "Update 3 mods")]
    [InlineData("en", 3, true, "Update 3 mods in Main", "Update 3 mods anyway")]
    [InlineData("de", 0, false, "Die Mods in Main aktualisieren", "Aktualisieren")]
    [InlineData("de", 0, true, "Die Mods in Main aktualisieren", "Trotzdem aktualisieren")]
    [InlineData("de", 1, false, "1 Mod in Main aktualisieren", "1 Mod aktualisieren")]
    [InlineData("de", 1, true, "1 Mod in Main aktualisieren", "1 Mod trotzdem aktualisieren")]
    [InlineData("de", 3, false, "3 Mods in Main aktualisieren", "3 Mods aktualisieren")]
    [InlineData("de", 3, true, "3 Mods in Main aktualisieren", "3 Mods trotzdem aktualisieren")]
    public async Task TitleAndButton_NameTheCount_AndTheWarning(string language, int count, bool warning, string title, string confirm)
    {
        using var harness = await CreateWithGameAsync();
        harness.SpaceDock.Releases.AddRange([Release("alpha", "1.0.0"), Release("alpha", "1.1.0", changelog: "- Fixes the HUD.")]);
        await InstallAsync(harness, ("alpha", "1.0.0"));
        var updateAll = await OpenAsync(harness);
        await updateAll.UpdateCommand.ExecuteAsync(null);
        harness.Localization.TrySetCulture(language);

        updateAll.PendingPlan = PlanOf(updateAll.PendingPlan!, Enumerable.Range(0, count).Select(number => Release($"mod{number}", "1.0.0")));
        updateAll.InstallWarning = warning ? "A warning." : null;

        Assert.Equal(title, updateAll.TitleText);
        Assert.Equal(confirm, updateAll.ConfirmUpdateText);
    }

    [Fact]
    public async Task Button_NamesTheSizeThatThePlanDownloads()
    {
        using var harness = await CreateWithGameAsync();
        harness.SpaceDock.Releases.AddRange([Release("alpha", "1.0.0"), Release("alpha", "1.1.0", changelog: "- Fixes the HUD.", sizeBytes: 12_400_000)]);
        await InstallAsync(harness, ("alpha", "1.0.0"));
        var updateAll = await OpenAsync(harness);

        await updateAll.UpdateCommand.ExecuteAsync(null);

        Assert.Equal($"Update 1 mod ({MainViewModel.PlanSizeText(updateAll.PendingPlan)})", updateAll.ConfirmUpdateText);
        Assert.Contains("12", updateAll.ConfirmUpdateText);
    }

    [Fact]
    public async Task WarningLines_GroupEqualMessagesUnderTheNames_AndPutABrokenSaveFirst()
    {
        using var harness = await CreateWithGameAsync();
        harness.SpaceDock.Releases.AddRange([Release("alpha", "1.0.0"), Release("alpha", "1.1.0", changelog: "- Fixes the HUD.")]);
        await InstallAsync(harness, ("alpha", "1.0.0"));
        var updateAll = await OpenAsync(harness);
        await updateAll.UpdateCommand.ExecuteAsync(null);
        var plan = updateAll.PendingPlan!;
        var compatibility = new PlanningMessage("alpha", PlanningMessageKind.Compatibility);
        var breaksSave = new PlanningMessage("beta", PlanningMessageKind.BreaksSave) { Dependency = new ModDependency("beta", ModDependencyKind.Required), Value = "Mun Landing" };

        updateAll.PendingPlan = plan.WithWarnings([compatibility, compatibility with { ModId = "AdvancedFlightComputer" }]);

        var grouped = Assert.Single(updateAll.WarningLines);
        var name = harness.ViewModel.ContentName("AdvancedFlightComputer");
        Assert.Equal($"alpha, {name}: {PlanningText.Message(compatibility)}", grouped.Text);
        Assert.False(updateAll.HasBreakingWarning);

        updateAll.PendingPlan = plan.WithWarnings([compatibility, breaksSave]);

        Assert.Equal(
            [(PlanningText.Message(breaksSave), true), ($"alpha: {PlanningText.Message(compatibility)}", false)],
            updateAll.WarningLines.Select(line => (line.Text, line.IsBreaking)));
        Assert.True(updateAll.HasBreakingWarning);
    }

    [Fact]
    public async Task ChangeOfARecommendation_ForAModOfSpaceDock_UpdatesTheListAndTheCount_AndConfirmRunsThatPlan()
    {
        const string SpaceDockMod = ViewModelHarness.FakeSpaceDock.OwnId;
        const string SpaceDockLibrary = "5001";
        using var harness = await CreateWithGameAsync();
        harness.SpaceDock.Releases.AddRange(
        [
            Release(SpaceDockMod, "1.0.0"),
            Release(SpaceDockMod, "1.1.0", dependencies: [new ModDependency(SpaceDockLibrary, ModDependencyKind.Recommends)]),
            Release(SpaceDockLibrary, "1.0.0"),
        ]);
        var instance = await InstallAsync(harness, (SpaceDockMod, "1.0.0"));
        var updateAll = await OpenAsync(harness);
        await updateAll.UpdateCommand.ExecuteAsync(null);
        var choices = updateAll.Choices!;
        Assert.Equal([SpaceDockMod, SpaceDockLibrary], updateAll.Changes.Select(change => change.ModId));
        Assert.Equal("Update 2 mods", updateAll.ConfirmUpdateText);

        choices.Recommended[0].IsSelected = false;
        await choices.Planning;

        Assert.Equal([SpaceDockMod], updateAll.Changes.Select(change => change.ModId));
        Assert.Equal("Update 1 mod", updateAll.ConfirmUpdateText);

        await updateAll.ConfirmUpdateCommand.ExecuteAsync(null);

        Assert.Equal([SpaceDockMod], (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods.Select(mod => mod.ModId));
    }

    [Fact]
    public async Task ChangeOfARecommendation_ForAModOfTheIndex_UpdatesTheList()
    {
        using var harness = await CreateWithGameAsync(editSnapshot: json => InstallChoicesTests.WithDependencies(json, "AdvancedFlightComputer", """[{ "id": "MeasureTools", "kind": "recommends", "source": "authored" }]"""));
        await InstallAsync(harness, ("AdvancedFlightComputer", "0.7.4"));
        var updateAll = await OpenAsync(harness);
        await updateAll.UpdateCommand.ExecuteAsync(null);
        var choices = updateAll.Choices!;
        Assert.Contains("MeasureTools", updateAll.Changes.Select(change => change.ModId));

        choices.Recommended[0].IsSelected = false;
        await choices.Planning;

        Assert.DoesNotContain("MeasureTools", updateAll.Changes.Select(change => change.ModId));
        Assert.Contains("AdvancedFlightComputer", updateAll.Changes.Select(change => change.ModId));
    }

    [Fact]
    public async Task Reload_WhileTheConfirmationWaits_KeepsIt_AndConfirmAfterAChangeOfTheInstanceRefuses()
    {
        using var harness = await CreateWithGameAsync();
        harness.SpaceDock.Releases.AddRange([Release("alpha", "1.0.0"), Release("alpha", "1.1.0", changelog: "- Fixes the HUD."), Release("other", "1.0.0")]);
        var instance = await InstallAsync(harness, ("alpha", "1.0.0"));
        var viewModel = harness.ViewModel;
        var updateAll = await OpenAsync(harness);
        await updateAll.UpdateCommand.ExecuteAsync(null);

        await InstalledContent.AddAsync(harness, "other", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        Assert.Same(updateAll, viewModel.UpdateAll);
        Assert.True(updateAll.IsConfirmingUpdate);

        await updateAll.ConfirmUpdateCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.ManualInstallsInstanceChanged, viewModel.UpdateAll!.InstallError);
        Assert.Equal(ModVersion.Parse("1.0.0"), (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods.Single(mod => mod.ModId == "alpha").Version);
    }

    [Fact]
    public async Task APlanThatComesToWaitOnAnotherInstance_ShowsItsModalOnTheReturn_AndOnlyThere()
    {
        var gate = new PlanGate();
        using var harness = await CreateWithGameAsync(gate);
        harness.SpaceDock.Releases.AddRange([Release("alpha", "1.0.0"), Release("alpha", "1.1.0", changelog: "- Fixes the HUD.")]);
        var first = await InstallAsync(harness, ("alpha", "1.0.0"));
        var second = (await harness.Services.Instances.CreateAsync("Second", Borea.Core.Instances.InstanceSource.Custom.Value)).Instance;
        var viewModel = harness.ViewModel;
        var updateAll = await OpenAsync(harness);
        gate.Close();
        var update = updateAll.UpdateCommand.ExecuteAsync(null);
        await gate.Reached.WaitAsync(TimeSpan.FromSeconds(30));

        await viewModel.Instances.Single(instance => instance.InstanceId == second.InstanceId).OpenCommand.ExecuteAsync(null);
        gate.Open();
        await update;

        Assert.NotSame(updateAll, viewModel.UpdateAll);
        Assert.False(viewModel.UpdateAll!.IsConfirmingUpdate);

        await viewModel.Instances.Single(instance => instance.InstanceId == first.InstanceId).OpenCommand.ExecuteAsync(null);

        Assert.Same(updateAll, viewModel.UpdateAll);
        Assert.True(updateAll.IsConfirmingUpdate);

        updateAll.CancelUpdateCommand.Execute(null);
        await viewModel.Instances.Single(instance => instance.InstanceId == second.InstanceId).OpenCommand.ExecuteAsync(null);
        await viewModel.Instances.Single(instance => instance.InstanceId == first.InstanceId).OpenCommand.ExecuteAsync(null);

        Assert.NotSame(updateAll, viewModel.UpdateAll);
    }

    [Fact]
    public async Task Reload_WithoutAConfirmation_MakesANewItem()
    {
        using var harness = await CreateWithGameAsync();
        harness.SpaceDock.Releases.AddRange([Release("alpha", "1.0.0")]);
        await InstallAsync(harness, ("alpha", "1.0.0"));
        var updateAll = await OpenAsync(harness);

        await harness.ViewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        Assert.NotSame(updateAll, harness.ViewModel.UpdateAll);
    }

    [Fact]
    public async Task EmptyPlan_SaysSoOnTheItemThatTheReloadMakes()
    {
        using var harness = await CreateWithGameAsync();
        harness.SpaceDock.Releases.AddRange([Release("alpha", "1.0.0")]);
        await InstallAsync(harness, ("alpha", "1.0.0"));
        var updateAll = await OpenAsync(harness);

        await updateAll.UpdateCommand.ExecuteAsync(null);

        Assert.NotSame(updateAll, harness.ViewModel.UpdateAll);
        Assert.Equal(harness.Localization.UpdateNothingFound, harness.ViewModel.UpdateAll!.ProgressStatus);
        Assert.False(harness.ViewModel.UpdateAll.IsConfirmingUpdate);
    }

    [Fact]
    public async Task EmptyPlanWithAWarningAboutAKeptMod_OpensNoModal_AndSaysThatItFoundNothing()
    {
        var gate = new PlanGate { Edit = plan => plan.WithWarnings([new PlanningMessage("alpha", PlanningMessageKind.Compatibility)]) };
        using var harness = await CreateWithGameAsync(gate);
        harness.SpaceDock.Releases.AddRange([Release("alpha", "1.0.0")]);
        await InstallAsync(harness, ("alpha", "1.0.0"));
        var updateAll = await OpenAsync(harness);

        await updateAll.UpdateCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.UpdateAll!.IsConfirmingUpdate);
        Assert.Equal(harness.Localization.UpdateNothingFound, harness.ViewModel.UpdateAll.ProgressStatus);
    }

    [Fact]
    public async Task APlanThatChangesAMod_DoesNotSayThatItFoundNothing()
    {
        using var harness = await CreateWithGameAsync();
        harness.SpaceDock.Releases.AddRange([Release("alpha", "1.0.0"), Release("alpha", "1.1.0")]);
        await InstallAsync(harness, ("alpha", "1.0.0"));
        var updateAll = await OpenAsync(harness);

        await updateAll.UpdateCommand.ExecuteAsync(null);

        Assert.Null(harness.ViewModel.UpdateAll!.ProgressStatus);
    }

    [Fact]
    public async Task WhilePlanning_TheRowSaysThatItChecks_AndTheRunIsPlanning()
    {
        var gate = new PlanGate();
        using var harness = await CreateWithGameAsync(gate);
        harness.SpaceDock.Releases.AddRange([Release("alpha", "1.0.0"), Release("alpha", "1.1.0", changelog: "- Fixes the HUD.")]);
        await InstallAsync(harness, ("alpha", "1.0.0"));
        var updateAll = await OpenAsync(harness);
        gate.Close();

        var update = updateAll.UpdateCommand.ExecuteAsync(null);
        await gate.Reached.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(harness.Localization.UpdateChecking, updateAll.ProgressStatus);
        Assert.True(updateAll.Run!.IsPlanning);
        Assert.False(updateAll.IsConfirmingUpdate);

        gate.Open();
        await update;

        Assert.Null(updateAll.ProgressStatus);
        Assert.True(updateAll.IsConfirmingUpdate);
    }

    [Fact]
    public async Task StopWhileChecking_SaysSo_AndHoldsNoPlan()
    {
        var gate = new PlanGate();
        using var harness = await CreateWithGameAsync(gate);
        harness.SpaceDock.Releases.AddRange([Release("alpha", "1.0.0"), Release("alpha", "1.1.0", changelog: "- Fixes the HUD.")]);
        await InstallAsync(harness, ("alpha", "1.0.0"));
        var updateAll = await OpenAsync(harness);
        gate.Close();
        var update = updateAll.UpdateCommand.ExecuteAsync(null);
        await gate.Reached.WaitAsync(TimeSpan.FromSeconds(30));

        updateAll.Run!.StopCommand.Execute(null);
        gate.Open();
        await update;

        Assert.Equal(harness.Localization.UpdateStopped, updateAll.ProgressStatus);
        Assert.False(updateAll.IsConfirmingUpdate);
    }

    [Fact]
    public async Task ALibraryThatWasBusy_LeavesNoErrorAfterACancelOrAPressThatWorks()
    {
        using var harness = await CreateWithGameAsync();
        harness.SpaceDock.Releases.AddRange([Release("alpha", "1.0.0"), Release("alpha", "1.1.0", changelog: "- Fixes the HUD.")]);
        await InstallAsync(harness, ("alpha", "1.0.0"));
        var viewModel = harness.ViewModel;
        var updateAll = await OpenAsync(harness);
        await updateAll.UpdateCommand.ExecuteAsync(null);
        viewModel.IsChangingLibraryFolder = true;

        await updateAll.ConfirmUpdateCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.LibraryFolderBusy, updateAll.InstallError);
        Assert.True(updateAll.IsConfirmingUpdate);

        updateAll.CancelUpdateCommand.Execute(null);

        Assert.Null(updateAll.InstallError);

        viewModel.IsChangingLibraryFolder = false;
        await updateAll.UpdateCommand.ExecuteAsync(null);
        viewModel.IsChangingLibraryFolder = true;
        await updateAll.ConfirmUpdateCommand.ExecuteAsync(null);
        viewModel.IsChangingLibraryFolder = false;

        await updateAll.ConfirmUpdateCommand.ExecuteAsync(null);

        Assert.Null(viewModel.UpdateAll!.InstallError);
    }

    private static InstallPlan PlanOf(InstallPlan plan, IEnumerable<ModVersionMetadata> releases)
        => new(plan.InstanceId, plan.InstanceState, plan.Selections, releases.Select(release => new PlannedInstall(release, InstallReason.Manual, GameCompatibility.Compatible, null)).ToList(), plan.Warnings, plan.UnresolvedChoices, plan.Conflicts, plan.Choices);

    private static async Task<Borea.Core.Instances.Instance> InstallAsync(ViewModelHarness harness, params (string ModId, string Version)[] mods)
    {
        Borea.Core.Instances.Instance? instance = null;
        foreach (var (modId, version) in mods)
            instance = await InstalledContent.AddAsync(harness, modId, activate: true, ownership: ModInstallOwnership.Borea, version: version);
        return instance!;
    }

    private static async Task<UpdateAllItem> OpenAsync(ViewModelHarness harness)
    {
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await harness.ViewModel.WhenContentUpdatesCheckedAsync();
        return harness.ViewModel.UpdateAll!;
    }

    /// <summary>A harness whose game fits every release of these tests, so a plan has no warning of its own.</summary>
    internal static Task<ViewModelHarness> CreateWithGameAsync(PlanGate? gate = null, Func<string, string>? editSnapshot = null) =>
        ViewModelHarness.CreateAsync(
            services =>
            {
                var game = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Game")).FullName;
                File.Copy(Path.Combine(AppContext.BaseDirectory, "GameVersionFixture.dll"), Path.Combine(game, "KSA.dll"));
                return services.SettingsRepository.SaveAsync(services.Settings.WithGameDirectory(game));
            },
            respond: ContentUpdateTests.ServeArchive,
            editSnapshot: editSnapshot,
            wrapInstallPlanner: gate is null ? null : inner => new GatedPlanner(inner, gate));

    internal static ModVersionMetadata Release(string modId, string version, IReadOnlyList<ModDependency>? dependencies = null, string? changelog = null, long? sizeBytes = null) => new(
        specVersion: 1,
        modId: modId,
        version: ModVersion.Parse(version),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: DateTimeOffset.UnixEpoch,
        gameMin: "2026.1.1.1",
        gameMinRevision: 1,
        download: new DownloadInfo($"https://{ArchiveHost}/{modId}/{version}.zip", sha256: null, sizeBytes: sizeBytes, contentType: "application/zip"),
        installSizeBytes: null,
        dependencies: dependencies ?? [],
        changelog: changelog);

    /// <summary>Holds each plan while it is closed, so a test can look at the state during a plan.</summary>
    internal sealed class PlanGate
    {
        private TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource? _closed;

        public Task Reached => _reached.Task;

        /// <summary>Changes each plan that passes, so that a test can bring a warning.</summary>
        public Func<InstallPlan, InstallPlan>? Edit { get; set; }

        public Task Passed
        {
            get
            {
                _reached.TrySetResult();
                return _closed?.Task ?? Task.CompletedTask;
            }
        }

        public void Close()
        {
            _reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void Open() => _closed?.TrySetResult();
    }

    private sealed class GatedPlanner(IInstallPlanner inner, PlanGate gate) : IInstallPlanner
    {
        public async Task<InstallPlan> PlanAsync(InstallPlanningRequest request, CancellationToken cancellationToken = default)
        {
            await gate.Passed.WaitAsync(cancellationToken);
            var plan = await inner.PlanAsync(request, cancellationToken);
            return gate.Edit?.Invoke(plan) ?? plan;
        }
    }
}
