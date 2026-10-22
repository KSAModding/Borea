using Borea.Core.Instances;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.UserContent;
using Borea.Storage.ModPacks;
using static Borea.Storage.Tests.ModPacks.ModPackInstallerTests;

namespace Borea.Storage.Tests.ModPacks;

public sealed class ModPackUserContentTests
{
    private readonly List<string> _log = [];

    [Fact]
    public async Task Install_PackWithAModAndAVehicle_InstallsBoth_TheModFirst()
    {
        var (instances, instance) = await TargetAsync();
        var mod = Release("Parts");
        var content = new FakeContentInstaller(_log);

        var result = await Installer(instances, content).InstallAsync(Request(instance, Pack([mod], vehicles: [Vehicle("Rover")]), mod, Vehicle("Rover")));

        Assert.True(result.IsComplete);
        Assert.Equal(["mod Parts", "vehicle Rover"], _log);
        var rover = Assert.Single(result.Content);
        Assert.Equal((ContentType.Vehicle, "Rover", ModPackContentStatus.Installed, "Rover"), (rover.Type, rover.ContentId, rover.Status, rover.FolderName));
    }

    [Fact]
    public async Task Install_ProgressNumbersTheContentAfterTheMods()
    {
        var (instances, instance) = await TargetAsync();
        var mod = Release("Parts");
        var steps = new List<(int Step, int Count)>();
        var progress = new SynchronousProgress<InstallProgress>(report => steps.Add((report.Step, report.StepCount)));

        await Installer(instances, new FakeContentInstaller(_log)).InstallAsync(Request(instance, Pack([mod], vehicles: [Vehicle("Rover")]), mod, Vehicle("Rover")), progress);
        var withoutContent = new List<(int Step, int Count)>();
        var (otherInstances, other) = await TargetAsync();
        await Installer(otherInstances, new FakeContentInstaller(_log)).InstallAsync(Request(other, Pack([mod]), mod), new SynchronousProgress<InstallProgress>(report => withoutContent.Add((report.Step, report.StepCount))));

        Assert.Equal([(1, 2), (2, 2)], steps.Distinct());
        Assert.Equal([(1, 1)], withoutContent.Distinct());
    }

    [Fact]
    public async Task Install_TakenName_IsReportedByThePlan_AndTheModsStillInstall()
    {
        var (instances, instance) = await TargetAsync();
        var mod = Release("Parts");
        var content = new FakeContentInstaller(_log) { Taken = ["Rover"] };
        var request = Request(instance, Pack([mod], vehicles: [Vehicle("Rover")]), mod, Vehicle("Rover"));

        var plan = await Installer(instances, content).PlanAsync(request);

        Assert.Equal(ModPackContentStatus.NameTaken, Assert.Single(plan.Content).Status);
        Assert.Empty(_log);

        var declined = await Installer(instances, content).InstallAsync(request);

        Assert.True(declined.IsComplete);
        Assert.Equal(ModPackContentStatus.NameTaken, Assert.Single(declined.Content).Status);
        Assert.Equal(["mod Parts"], _log);

        var named = await Installer(instances, content).InstallAsync(request with { ContentNames = new Dictionary<string, string> { ["rover"] = "Rover 2" } });

        Assert.Equal((ModPackContentStatus.Installed, "Rover 2"), (Assert.Single(named.Content).Status, Assert.Single(named.Content).FolderName));
    }

    [Fact]
    public async Task Install_YankedVehiclePin_WarnsAndTheRestInstalls()
    {
        var (instances, instance) = await TargetAsync();
        var mod = Release("Parts");
        var yanked = Vehicle("Rover", yanked: true);

        var result = await Installer(instances, new FakeContentInstaller(_log)).InstallAsync(Request(instance, Pack([mod], vehicles: [yanked]), mod, yanked));

        Assert.True(result.IsComplete);
        Assert.Equal(ModPackContentStatus.Skipped, Assert.Single(result.Content).Status);
        Assert.Contains(result.Warnings, warning => (warning.ModId, warning.Kind) == ("Rover", PlanningMessageKind.YankedPin));
        Assert.Equal(["mod Parts"], _log);
    }

    [Fact]
    public async Task Install_VehiclePinOfASaveListing_IsSkippedAsUnlisted()
    {
        var (instances, instance) = await TargetAsync();
        var mod = Release("Parts");
        var save = Vehicle("Campaign", type: ContentType.Save);

        var result = await Installer(instances, new FakeContentInstaller(_log)).InstallAsync(Request(instance, Pack([mod], vehicles: [save]), mod, save));

        Assert.Equal(ModPackContentStatus.Skipped, Assert.Single(result.Content).Status);
        Assert.Contains(result.Warnings, warning => (warning.ModId, warning.Kind) == ("Campaign", PlanningMessageKind.UnlistedPin));
    }

    [Fact]
    public async Task Install_WithoutAContentInstaller_SkipsTheVehicleWithAMessage()
    {
        var (instances, instance) = await TargetAsync();
        var mod = Release("Parts");

        var result = await new ModPackInstaller(instances, new FakePlanner(), new FakeInstaller(instances), new FakeReplacer(instances)).InstallAsync(Request(instance, Pack([mod], vehicles: [Vehicle("Rover")]), mod, Vehicle("Rover")));

        Assert.True(result.IsComplete);
        Assert.Equal(ModPackContentStatus.Skipped, Assert.Single(result.Content).Status);
    }

    [Fact]
    public async Task Update_ChangedPinnedSave_IsReportedByThePlanBeforeAnyWrite_AndUpdatesOnlyWithTheGo()
    {
        var mod = Release("Parts");
        var oldSave = Vehicle("Campaign", type: ContentType.Save);
        var newSave = Vehicle("Campaign", "2.0.0", type: ContentType.Save);
        var content = new FakeContentInstaller(_log) { Recorded = { ["Campaign"] = new InstalledContentRecord(oldSave, "Campaign", DateTimeOffset.UnixEpoch, new Dictionary<string, string>()) }, ChangedIds = ["Campaign"] };
        (ModPackUpdater Updater, ModPackUpdateRequest Request) Fresh()
        {
            var instance = Instance.FromExisting(Guid.NewGuid(), "Target", new InstanceSource.FromModPack("Pack", ModVersion.Parse("1.0.0")), DateTimeOffset.UnixEpoch, [new InstalledMod("Parts", mod.Version, InstallReason.ModPack, DateTimeOffset.UnixEpoch, mod, ownershipToken: "token")], false);
            var instances = new MemoryInstanceRepository(instance);
            var updater = new ModPackUpdater(instances, new FakePlanner(), new InstallPlanExecutor(instances, new FakeInstaller(instances), new FakeReplacer(instances)), new NoUninstaller(), content);
            return (updater, new ModPackUpdateRequest(instance.InstanceId, Pack([mod], saves: [newSave], version: "2.0.0"), new FakeModRepository([mod]), UserContent: new FakeContentRepository([newSave])));
        }

        var first = Fresh();
        var plan = await first.Updater.PlanAsync(first.Request);
        var declined = await first.Updater.UpdateAsync(first.Request);
        var second = Fresh();
        var confirmed = await second.Updater.UpdateAsync(second.Request with { DiscardChangesOf = new HashSet<string> { "campaign" } });

        var planned = Assert.Single(plan.Content);
        Assert.Equal((ModPackContentStatus.Changed, "universe.xml"), (planned.Status, Assert.Single(planned.Changes!.Changed)));
        Assert.Equal(ModPackContentStatus.Changed, Assert.Single(declined.Content).Status);
        Assert.True(declined.IsComplete);
        Assert.Equal(ModPackContentStatus.Updated, Assert.Single(confirmed.Content).Status);
        Assert.Equal(["update Campaign discard"], _log);
    }

    private static async Task<(MemoryInstanceRepository Instances, Instance Instance)> TargetAsync()
    {
        var instances = new MemoryInstanceRepository();
        return (instances, (await instances.CreateAsync("Target", InstanceSource.Custom.Value)).Instance);
    }

    private ModPackInstaller Installer(MemoryInstanceRepository instances, FakeContentInstaller content)
    {
        var mods = new FakeInstaller(instances)
        {
            Downloading = (release, _) =>
            {
                _log.Add("mod " + release.ModId);
                return Task.CompletedTask;
            },
        };
        return new ModPackInstaller(instances, new FakePlanner(), mods, new FakeReplacer(instances), userContent: content);
    }

    private static ModPackInstallRequest Request(Instance instance, ModPackResult pack, ModVersionMetadata mod, params ModVersionMetadata[] content) =>
        new(instance.InstanceId, pack, new FakeModRepository([mod]), UserContent: new FakeContentRepository(content));

    private static ModPackResult Pack(IReadOnlyList<ModVersionMetadata> mods, IReadOnlyList<ModVersionMetadata>? vehicles = null, IReadOnlyList<ModVersionMetadata>? saves = null, string version = "1.0.0")
    {
        static List<ModPackEntry> Pins(IReadOnlyList<ModVersionMetadata>? releases) => (releases ?? []).Select(release => new ModPackEntry(release.ModId, release.Version)).ToList();
        var metadata = new ModPackMetadata(1, "Pack", "test", "Pack", ["Author"], "Pack.", "CC0-1.0", new Dictionary<string, string> { ["forums"] = "https://example.com/pack" }, "2026.7", ModVersion.Parse(version), DateTimeOffset.UnixEpoch, Pins(mods), vehicles: Pins(vehicles), saves: Pins(saves));
        return new ModPackResult(metadata.ModPackId, metadata.Version.ToString(), metadata, null, null, []);
    }

    private static ModVersionMetadata Vehicle(string id, string version = "1.0.0", bool yanked = false, ContentType type = ContentType.Vehicle) =>
        new(1, id, ModVersion.Parse(version), ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, "2026.7.4.2131", 2131, new DownloadInfo($"https://example.com/{id}.zip", new string('A', 64), 1, "application/zip"), 1, [], type, install: new InstallInfo(null, derived: false, folder: id), yanked: yanked);

    private sealed class FakeContentRepository(IReadOnlyList<ModVersionMetadata> releases) : IUserContentRepository
    {
        public Task<ModMetadata?> FindAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ModVersionMetadata?> GetReleaseAsync(string id, ModVersion version, CancellationToken cancellationToken = default) =>
            Task.FromResult(releases.FirstOrDefault(release => ModIds.Equals(release.ModId, id) && release.Version == version));

        public Task<ModVersionMetadata?> GetLatestReleaseAsync(string id, ReleaseChannel channel, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ModMetadata>> SearchAsync(ContentType type, string query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeContentInstaller(List<string> log) : IUserContentInstaller
    {
        public HashSet<string> Taken { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, InstalledContentRecord> Recorded { get; } = new(ModIds.Comparer);

        public HashSet<string> ChangedIds { get; init; } = new(ModIds.Comparer);

        public Task<UserContentResult> CheckInstallAsync(Guid instanceId, ModVersionMetadata release, string? name, CancellationToken cancellationToken = default)
        {
            if (Recorded.TryGetValue(release.ModId, out var record))
                return Task.FromResult(new UserContentResult(UserContentOutcome.AlreadyInstalled, record.FolderName, record));

            var folder = name ?? release.Install!.Folder!;
            return Task.FromResult(new UserContentResult(Taken.Contains(folder) ? UserContentOutcome.NameTaken : UserContentOutcome.Ready, folder));
        }

        public Task<UserContentResult> CheckUpdateAsync(Guid instanceId, InstalledContentRecord installed, ModVersionMetadata release, string? name, bool discardChanges, CancellationToken cancellationToken = default) =>
            Task.FromResult(ChangedIds.Contains(installed.ListingId) && !discardChanges
                ? new UserContentResult(UserContentOutcome.Changed, installed.FolderName, installed, new InstalledContentChanges([], [], ["universe.xml"]))
                : new UserContentResult(UserContentOutcome.Ready, name ?? installed.FolderName));

        public async Task<UserContentResult> InstallAsync(Guid instanceId, ModVersionMetadata release, string? name, UserContentStep? beforeMove = null, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var check = await CheckInstallAsync(instanceId, release, name, cancellationToken);
            if (check.Outcome != UserContentOutcome.Ready)
                return check;

            progress.Report(release, InstallPhase.Downloading);
            log.Add($"{(release.Type == ContentType.Save ? "save" : "vehicle")} {release.ModId}");
            progress.Report(release, InstallPhase.Finishing);
            return new UserContentResult(UserContentOutcome.Installed, check.FolderName);
        }

        public async Task<UserContentResult> UpdateAsync(Guid instanceId, InstalledContentRecord installed, ModVersionMetadata release, string? name, bool discardChanges, UserContentStep? beforeMove = null, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var check = await CheckUpdateAsync(instanceId, installed, release, name, discardChanges, cancellationToken);
            if (check.Outcome != UserContentOutcome.Ready)
                return check;

            log.Add($"update {release.ModId}{(discardChanges ? " discard" : "")}");
            return new UserContentResult(UserContentOutcome.Updated, check.FolderName);
        }

        public Task<UserContentResult> RemoveAsync(Guid instanceId, InstalledContentRecord installed, bool discardChanges, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoUninstaller : IModUninstaller
    {
        public Task UninstallAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
