using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.State;
using Borea.Core.UserContent;

namespace Borea.Core.Tests.UserContent;

public sealed class UserContentInstallationTests
{
    private readonly List<string> _log = [];
    private readonly FakeModState _modState = new();
    private readonly Instance _instance = new("Test", InstanceSource.Custom.Value);

    [Fact]
    public async Task Execute_VehicleWithTwoRequiredMods_ChecksTheArchiveThenInstallsBothEnabledThenTheFolder()
    {
        var vehicle = Vehicle([Required("first"), Required("second")]);
        var plan = await Installation(Plan(Install("first"), Install("second"))).PlanInstallAsync(Request(vehicle));

        var result = await Installation(Plan(Install("first"), Install("second"))).ExecuteAsync(plan, allowMissing: false);

        Assert.Equal(UserContentOutcome.Installed, result.Outcome);
        Assert.Equal(["download", "mods first, second", "folder"], _log);
        Assert.Equal(["first", "second"], _modState.Entries.Where(entry => entry.Enabled).Select(entry => entry.ModId));
    }

    [Fact]
    public async Task Plan_InstalledAndDisabledRequiredMod_IsEnabled()
    {
        var instance = Instance.FromExisting(_instance.InstanceId, "Test", InstanceSource.Custom.Value, DateTimeOffset.UnixEpoch, [new InstalledMod("first", ModVersion.Parse("1.0.0"), InstallReason.Manual, DateTimeOffset.UnixEpoch, Mod("first"))], false);
        _modState.Entries.Add(new ModManifestEntry("first", false));
        var mods = new InstallPlan(instance.InstanceId, InstallPlanningState.Capture(instance), [new PlannedSelection(Mod("first"), InstallReason.Manual, IsAlreadyInstalled: true)], [], [], [], [], []);
        var installation = Installation(mods);

        var plan = await installation.PlanInstallAsync(Request(Vehicle([Required("first")])) with { Instance = instance });
        await installation.ExecuteAsync(plan, allowMissing: false);

        Assert.Equal(["first"], plan.Enables);
        Assert.True(Assert.Single(_modState.Entries).Enabled);
    }

    [Fact]
    public async Task Plan_DisabledForeignModThatTheVehicleNeeds_IsEnabled()
    {
        var instance = Instance.FromExisting(_instance.InstanceId, "Test", InstanceSource.Custom.Value, DateTimeOffset.UnixEpoch, [], [new ForeignMod("parts")], false);
        _modState.Entries.Add(new ModManifestEntry("parts", false));

        var plan = await Installation(Plan()).PlanInstallAsync(Request(Vehicle([Required("parts")])) with { Instance = instance });

        Assert.Equal(["parts"], plan.Enables);
    }

    [Fact]
    public async Task Execute_MissingMod_StopsUnlessThePlayerGoesOn()
    {
        var missing = new PlanningMessage("rover", PlanningMessageKind.MissingForVehicle) { Dependency = Required("gone"), Value = "unsatisfied-dependency" };
        var installation = Installation(Plan([missing], Install("first")));
        var plan = await installation.PlanInstallAsync(Request(Vehicle([Required("gone"), Required("first")])));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => installation.ExecuteAsync(plan, allowMissing: false));
        Assert.Contains("the game refuses the vehicle file", refused.Message, StringComparison.Ordinal);
        Assert.Empty(_log);

        await installation.ExecuteAsync(plan, allowMissing: true);
        Assert.Equal(["download", "mods first", "folder"], _log);
    }

    [Fact]
    public async Task Execute_IncompatibleRelease_IsRefusedBeforeAnythingInstalls()
    {
        var incompatible = new PlanningMessage("rover", PlanningMessageKind.Incompatible) { Value = "2026.12.1.6000" };
        var mods = new InstallPlan(_instance.InstanceId, InstallPlanningState.Capture(_instance), [], [Install("first")], [], [], [incompatible], []);
        var installation = Installation(mods);
        var plan = await installation.PlanInstallAsync(Request(Vehicle([Required("first")])));

        Assert.False(plan.IsReady);
        await Assert.ThrowsAsync<InvalidOperationException>(() => installation.ExecuteAsync(plan, allowMissing: true));
        Assert.Empty(_log);
    }

    [Fact]
    public async Task Execute_TakenName_ChangesNothing()
    {
        var installer = new FakeInstaller(_log) { CheckOutcome = UserContentOutcome.NameTaken };
        var installation = new UserContentInstallation(new FakePlanner(Plan(Install("first"))), new FakeExecutor(_log), _modState, installer);
        var plan = await installation.PlanInstallAsync(Request(Vehicle([Required("first")])));

        var result = await installation.ExecuteAsync(plan, allowMissing: false);

        Assert.Equal(UserContentOutcome.NameTaken, result.Outcome);
        Assert.Empty(_log);
    }

    [Fact]
    public async Task Execute_FolderStepFailsAfterTheMods_SaysTheModsStay()
    {
        var installer = new FakeInstaller(_log) { FailAfterStep = new IOException("The disk is full.") };
        var installation = new UserContentInstallation(new FakePlanner(Plan(Install("first"))), new FakeExecutor(_log), _modState, installer);
        var plan = await installation.PlanInstallAsync(Request(Vehicle([Required("first")])));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => installation.ExecuteAsync(plan, allowMissing: false));

        Assert.Contains("the mods it needs stay installed", failure.Message, StringComparison.Ordinal);
        Assert.Contains("The disk is full.", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_PassesTheRecordAndTheGoToDiscardChanges()
    {
        var installer = new FakeInstaller(_log);
        var installation = new UserContentInstallation(new FakePlanner(Plan()), new FakeExecutor(_log), _modState, installer);
        var record = new InstalledContentRecord(Vehicle([]), "Rover", DateTimeOffset.UnixEpoch, new Dictionary<string, string>());

        var plan = await installation.PlanUpdateAsync(record, Request(Vehicle([], "2.0.0")), discardChanges: true);
        var result = await installation.ExecuteAsync(plan, allowMissing: false);

        Assert.Equal(UserContentOutcome.Updated, result.Outcome);
        Assert.Equal(["download", "mods ", "update discard"], _log);
    }

    private UserContentInstallation Installation(InstallPlan mods) => new(new FakePlanner(mods), new FakeExecutor(_log), _modState, new FakeInstaller(_log));

    private UserContentRequest Request(ModVersionMetadata release) => new(_instance, release, new EmptyRepository());

    private InstallPlan Plan(params PlannedInstall[] operations) => Plan([], operations);

    private InstallPlan Plan(IReadOnlyList<PlanningMessage> warnings, params PlannedInstall[] operations) =>
        new(_instance.InstanceId, InstallPlanningState.Capture(_instance), operations.Select(operation => new PlannedSelection(operation.Release, operation.Reason, false)).ToList(), operations, warnings, [], [], []);

    private static PlannedInstall Install(string id) => new(Mod(id), InstallReason.Dependency, Borea.Core.Game.GameCompatibility.Compatible, null);

    private static ModDependency Required(string id) => new(id, ModDependencyKind.Required);

    private static ModVersionMetadata Mod(string id) => new(1, id, ModVersion.Parse("1.0.0"), ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, "2026.10.19.5638", 5638, new DownloadInfo("https://example.com/mod.zip", new string('A', 64), 1, "application/zip"), 1, []);

    private static ModVersionMetadata Vehicle(IReadOnlyList<ModDependency> dependencies, string version = "1.0.0") => new(1, "rover", ModVersion.Parse(version), ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, "2026.10.19.5638", 5638, new DownloadInfo("https://example.com/rover.zip", new string('A', 64), 1, "application/zip"), 1, dependencies, ContentType.Vehicle, install: new InstallInfo(null, derived: false, folder: "Rover"));

    private sealed class FakePlanner(InstallPlan plan) : IInstallPlanner
    {
        public Task<InstallPlan> PlanAsync(InstallPlanningRequest request, CancellationToken cancellationToken = default) => Task.FromResult(plan);
    }

    private sealed class FakeExecutor(List<string> log) : IInstallPlanExecutor
    {
        public Task ExecuteAsync(InstallPlan plan, bool enable, IProgress<InstallProgress>? progress = null, InstallStop? stop = null, CancellationToken cancellationToken = default)
        {
            Assert.True(enable);
            log.Add("mods " + string.Join(", ", plan.Operations.Select(operation => operation.Release.ModId)));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInstaller(List<string> log) : IUserContentInstaller
    {
        public UserContentOutcome CheckOutcome { get; init; } = UserContentOutcome.Ready;

        public Exception? FailAfterStep { get; init; }

        public Task<UserContentResult> CheckInstallAsync(Guid instanceId, ModVersionMetadata release, string? name, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UserContentResult(CheckOutcome, name ?? release.Install!.Folder));

        public Task<UserContentResult> CheckUpdateAsync(Guid instanceId, InstalledContentRecord installed, ModVersionMetadata release, string? name, bool discardChanges, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UserContentResult(CheckOutcome, name ?? installed.FolderName));

        public async Task<UserContentResult> InstallAsync(Guid instanceId, ModVersionMetadata release, string? name, UserContentStep? beforeMove = null, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            await StepAsync(beforeMove, cancellationToken);
            log.Add("folder");
            return new UserContentResult(UserContentOutcome.Installed, name);
        }

        public async Task<UserContentResult> UpdateAsync(Guid instanceId, InstalledContentRecord installed, ModVersionMetadata release, string? name, bool discardChanges, UserContentStep? beforeMove = null, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            await StepAsync(beforeMove, cancellationToken);
            log.Add(discardChanges ? "update discard" : "update");
            return new UserContentResult(UserContentOutcome.Updated, name);
        }

        public Task<UserContentResult> RemoveAsync(Guid instanceId, InstalledContentRecord installed, bool discardChanges, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private async Task StepAsync(UserContentStep? beforeMove, CancellationToken cancellationToken)
        {
            log.Add("download");
            if (beforeMove is not null)
                await beforeMove(null, cancellationToken);
            if (FailAfterStep is not null)
                throw FailAfterStep;
        }
    }

    private sealed class FakeModState : IModStateRepository
    {
        public List<ModManifestEntry> Entries { get; } = [];

        public Task<IReadOnlyList<ModManifestEntry>> GetEntriesAsync(Guid instanceId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ModManifestEntry>>(Entries.ToList());

        public Task<bool> IsActiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default) => Task.FromResult(Entries.Any(entry => entry.Enabled && entry.ModId == modId));

        public Task<IReadOnlyList<string>> GetAllActiveModIdsAsync(Guid instanceId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(Entries.Where(entry => entry.Enabled).Select(entry => entry.ModId).ToList());

        public Task<ModEntryAddResult> AddEntryAsync(Guid instanceId, string modId, bool enabled, CancellationToken cancellationToken = default)
        {
            if (Entries.Any(entry => entry.ModId == modId))
                return Task.FromResult(ModEntryAddResult.AlreadyListed);
            Entries.Add(new ModManifestEntry(modId, enabled));
            return Task.FromResult(ModEntryAddResult.Added);
        }

        public Task<bool> SetActiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default)
        {
            var index = Entries.FindIndex(entry => entry.ModId == modId);
            Entries[index] = Entries[index] with { Enabled = true };
            return Task.FromResult(true);
        }

        public Task<bool> SetInactiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> ReorderAsync(Guid instanceId, IReadOnlyList<string> modIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> PutGameContentFirstAsync(Guid instanceId, string gameDirectory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class EmptyRepository : IModRepository
    {
        public Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ModMetadata>>([]);

        public Task<ModVersionMetadata?> GetLatestReleaseAsync(string modId, CancellationToken cancellationToken = default) => Task.FromResult<ModVersionMetadata?>(null);

        public Task<ModVersionMetadata?> GetReleaseAsync(string modId, ModVersion version, CancellationToken cancellationToken = default) => Task.FromResult<ModVersionMetadata?>(null);

        public Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ModVersion>>([]);

        public Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ModMetadata>>([]);
    }
}
