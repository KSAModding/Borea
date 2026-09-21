using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Storage.ModPacks;
using static Borea.Storage.Tests.ModPacks.ModPackInstallerTests;

namespace Borea.Storage.Tests.ModPacks;

public sealed class ModPackUpdaterTests
{
    [Fact]
    public async Task Update_AddsRepinsAndRemoves_KeepsThePlayersMod_AndNamesTheNewVersion()
    {
        var same = Release("Same");
        var repinned = Release("Repinned", "1.1.0");
        var added = Release("Added");
        var instance = PackInstance(Installed(same, InstallReason.ModPack), Installed(Release("Repinned"), InstallReason.ModPack), Installed(Release("Dropped"), InstallReason.ModPack), Installed(Release("Player"), InstallReason.Manual));
        var instances = new MemoryInstanceRepository(instance);
        var uninstaller = new FakeUninstaller(instances);

        var result = await Updater(instances, new FakeInstaller(instances), uninstaller).UpdateAsync(Request(instance.InstanceId, Pack("2.0.0", same, repinned, added), [same, repinned, added]));

        Assert.True(result.IsComplete);
        Assert.Equal(
            [("Added", ModPackChangeKind.Add), ("Repinned", ModPackChangeKind.Change), ("Dropped", ModPackChangeKind.Remove)],
            result.Changes.Select(change => (change.ModId, change.Kind)));
        Assert.Equal(
            [("Added", ModPackMemberStatus.Installed), ("Dropped", ModPackMemberStatus.Removed), ("Repinned", ModPackMemberStatus.Replaced), ("Same", ModPackMemberStatus.AlreadyInstalled)],
            result.Members.Select(member => (member.ModId, member.Status)));
        var updated = (await instances.GetByIdAsync(instance.InstanceId))!;
        Assert.Equal(new InstanceSource.FromModPack("Pack", ModVersion.Parse("2.0.0")), updated.Source);
        Assert.Equal(["Added", "Player", "Repinned", "Same"], updated.Mods.Select(mod => mod.ModId).Order());
        Assert.Equal(ModVersion.Parse("1.1.0"), updated.Mods.Single(mod => mod.ModId == "Repinned").Version);
        Assert.Equal(InstallReason.Manual, updated.Mods.Single(mod => mod.ModId == "Player").Reason);
        Assert.Equal(["Dropped"], uninstaller.Removed);
    }

    [Fact]
    public async Task Update_DroppedModsThatOtherModsNeed_StayAsModsOfTheInstance()
    {
        var other = Release("Other", dependencies: [new ModDependency("NeededByPin", ModDependencyKind.Required)]);
        var instance = PackInstance(
            Installed(Release("NeededByPlayer"), InstallReason.ModPack),
            Installed(Release("NeededByPin"), InstallReason.ModPack),
            Installed(Release("Player", dependencies: [new ModDependency("NeededByPlayer", ModDependencyKind.Required)]), InstallReason.Manual));
        var instances = new MemoryInstanceRepository(instance);
        var uninstaller = new FakeUninstaller(instances);
        var installer = new FakeInstaller(instances);

        var result = await Updater(instances, installer, uninstaller).UpdateAsync(Request(instance.InstanceId, Pack("2.0.0", other), [other, Release("NeededByPin")]));

        Assert.True(result.IsComplete);
        Assert.Equal(ModPackChangeKind.Keep, result.Changes.Single(change => change.ModId == "NeededByPlayer").Kind);
        Assert.Equal(ModPackChangeKind.Keep, result.Changes.Single(change => change.ModId == "NeededByPin").Kind);
        Assert.Empty(uninstaller.Removed);
        Assert.Equal(1, installer.Counts["Other"]);
        Assert.False(installer.Counts.ContainsKey("NeededByPin"));
        var updated = (await instances.GetByIdAsync(instance.InstanceId))!;
        Assert.Equal(InstallReason.Manual, updated.Mods.Single(mod => mod.ModId == "NeededByPlayer").Reason);
        Assert.Equal(InstallReason.Manual, updated.Mods.Single(mod => mod.ModId == "NeededByPin").Reason);
    }

    [Fact]
    public async Task Update_KeptModThatANewPinNeedsNewer_ListsItsNewVersion()
    {
        var other = Release("Other", dependencies: [new ModDependency("Needed", ModDependencyKind.Required, ModVersion.Parse("2.0.0"))]);
        var newer = Release("Needed", "2.0.0");
        var instance = PackInstance(Installed(Release("Needed"), InstallReason.ModPack));
        var instances = new MemoryInstanceRepository(instance);
        var updater = Updater(instances, new FakeInstaller(instances), new FakeUninstaller(instances));
        var request = Request(instance.InstanceId, Pack("2.0.0", other), [other, Release("Needed"), newer]);

        var planned = await updater.PlanAsync(request);
        var result = await updater.UpdateAsync(request);

        var expected = new ModPackChange("Needed", ModPackChangeKind.Keep, ModVersion.Parse("1.0.0"), ModVersion.Parse("2.0.0"));
        Assert.Contains(expected, planned.Changes);
        Assert.Contains(expected, result.Changes);
        Assert.True(result.IsComplete);
        Assert.Equal((ModPackMemberStatus.Replaced, InstallReason.Manual), result.Members.Where(member => member.ModId == "Needed").Select(member => (member.Status, member.Reason)).Single());
        var needed = (await instances.GetByIdAsync(instance.InstanceId))!.Mods.Single(mod => mod.ModId == "Needed");
        Assert.Equal((ModVersion.Parse("2.0.0"), InstallReason.Manual), (needed.Version, needed.Reason));
    }

    [Fact]
    public async Task Update_StopDuringTheDownload_KeepsTheOldSource()
    {
        var added = Release("Added");
        var instance = PackInstance(Installed(Release("Dropped"), InstallReason.ModPack));
        var instances = new MemoryInstanceRepository(instance);
        var stop = new InstallStop();
        var installer = new FakeInstaller(instances)
        {
            Downloading = async (_, token) =>
            {
                stop.Request();
                await Task.Delay(Timeout.Infinite, token);
            },
        };

        var result = await Updater(instances, installer, new FakeUninstaller(instances)).UpdateAsync(Request(instance.InstanceId, Pack("2.0.0", added), [added]), stop: stop).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(result.IsStopped);
        Assert.False(result.IsComplete);
        Assert.Equal(ModPackMemberStatus.NotAttempted, result.Members.Single(member => member.ModId == "Added").Status);
        Assert.Equal(new InstanceSource.FromModPack("Pack", ModVersion.Parse("1.0.0")), (await instances.GetByIdAsync(instance.InstanceId))!.Source);
    }

    [Fact]
    public async Task Update_FailedInstall_KeepsTheOldSourceAndThePackModsUntilARetryCompletes()
    {
        var added = Release("Added");
        var instance = PackInstance(
            Installed(Release("Dropped"), InstallReason.ModPack),
            Installed(Release("Kept"), InstallReason.ModPack),
            Installed(Release("Player", dependencies: [new ModDependency("Kept", ModDependencyKind.Required)]), InstallReason.Manual));
        var instances = new MemoryInstanceRepository(instance);
        var updater = Updater(instances, new FakeInstaller(instances) { FailOnceFor = "Added" }, new FakeUninstaller(instances));
        var request = Request(instance.InstanceId, Pack("2.0.0", added), [added]);

        var failed = await updater.UpdateAsync(request);

        Assert.False(failed.IsComplete);
        Assert.False(failed.IsStopped);
        Assert.Equal(ModPackMemberStatus.Failed, failed.Members.Single(member => member.ModId == "Added").Status);
        Assert.Equal(ModPackMemberStatus.Removed, failed.Members.Single(member => member.ModId == "Dropped").Status);
        var afterFailure = (await instances.GetByIdAsync(instance.InstanceId))!;
        Assert.Equal(ModVersion.Parse("1.0.0"), Assert.IsType<InstanceSource.FromModPack>(afterFailure.Source).Version);
        Assert.Equal(InstallReason.ModPack, afterFailure.Mods.Single(mod => mod.ModId == "Kept").Reason);

        var retried = await updater.UpdateAsync(request);

        Assert.True(retried.IsComplete);
        var afterRetry = (await instances.GetByIdAsync(instance.InstanceId))!;
        Assert.Equal(ModVersion.Parse("2.0.0"), Assert.IsType<InstanceSource.FromModPack>(afterRetry.Source).Version);
        Assert.Equal(InstallReason.Manual, afterRetry.Mods.Single(mod => mod.ModId == "Kept").Reason);
    }

    [Fact]
    public async Task Plan_ListsTheChangesWithoutWriting()
    {
        var added = Release("Added");
        var instance = PackInstance(Installed(Release("Dropped"), InstallReason.ModPack));
        var instances = new MemoryInstanceRepository(instance);
        var installer = new FakeInstaller(instances);
        var uninstaller = new FakeUninstaller(instances);

        var result = await Updater(instances, installer, uninstaller).PlanAsync(Request(instance.InstanceId, Pack("2.0.0", added), [added]));

        Assert.True(result.CanRun);
        Assert.False(result.IsComplete);
        Assert.Equal([("Added", ModPackChangeKind.Add), ("Dropped", ModPackChangeKind.Remove)], result.Changes.Select(change => (change.ModId, change.Kind)));
        Assert.All(result.Members, member => Assert.Equal(ModPackMemberStatus.NotAttempted, member.Status));
        Assert.Empty(installer.Counts);
        Assert.Empty(uninstaller.Removed);
        var unchanged = (await instances.GetByIdAsync(instance.InstanceId))!;
        Assert.Equal("Dropped", Assert.Single(unchanged.Mods).ModId);
        Assert.Equal(ModVersion.Parse("1.0.0"), Assert.IsType<InstanceSource.FromModPack>(unchanged.Source).Version);
    }

    [Fact]
    public async Task Plan_YankedPinThatTheUpdateInstalls_NeedsTheCallersConfirmation()
    {
        var yanked = Release("Added", yanked: true);
        var instance = PackInstance();
        var instances = new MemoryInstanceRepository(instance);
        var updater = Updater(instances, new FakeInstaller(instances), new FakeUninstaller(instances));

        var refused = await updater.PlanAsync(Request(instance.InstanceId, Pack("2.0.0", yanked), [yanked]));
        var accepted = await updater.PlanAsync(Request(instance.InstanceId, Pack("2.0.0", yanked), [yanked]) with { ProceedWithYankedMembers = new HashSet<string> { "added" } });

        Assert.False(refused.CanRun);
        Assert.Contains(refused.Warnings, warning => warning.Kind == PlanningMessageKind.YankedPin);
        Assert.True(accepted.CanRun);
    }

    private static ModPackUpdater Updater(MemoryInstanceRepository instances, FakeInstaller installer, FakeUninstaller uninstaller)
        => new(instances, new FakePlanner(), new InstallPlanExecutor(instances, installer, new FakeReplacer(instances)), uninstaller);

    private static Instance PackInstance(params InstalledMod[] mods)
        => Instance.FromExisting(Guid.NewGuid(), "Target", new InstanceSource.FromModPack("Pack", ModVersion.Parse("1.0.0")), DateTimeOffset.UnixEpoch, mods, false);

    private static InstalledMod Installed(ModVersionMetadata release, InstallReason reason)
        => new(release.ModId, release.Version, reason, DateTimeOffset.UnixEpoch, release, ownershipToken: "token");

    private static ModPackUpdateRequest Request(Guid instanceId, ModPackResult pack, IReadOnlyList<ModVersionMetadata> releases)
        => new(instanceId, pack, new FakeModRepository(releases));

    private static ModPackResult Pack(string version, params ModVersionMetadata[] releases)
    {
        var metadata = new ModPackMetadata(1, "Pack", "test", "Pack", ["Author"], "Pack.", "CC0-1.0", new Dictionary<string, string> { ["forums"] = "https://example.com/pack" }, "2026.7", ModVersion.Parse(version), DateTimeOffset.UnixEpoch, releases.Select(release => new ModPackEntry(release.ModId, release.Version)).ToList());
        return new ModPackResult(metadata.ModPackId, metadata.Version.ToString(), metadata, null, null, []);
    }

    private sealed class FakeUninstaller(MemoryInstanceRepository instances) : IModUninstaller
    {
        public List<string> Removed { get; } = [];

        public async Task UninstallAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default)
        {
            await instances.UpdateAsync(instanceId, value => value.RemoveMod(modId), cancellationToken);
            Removed.Add(modId);
        }
    }
}
