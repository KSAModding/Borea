using Borea.Core.Dependencies;
using Borea.Core.Index;
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

        var result = await Updater(instances, uninstaller: uninstaller).UpdateAsync(Request(instance.InstanceId, Pack("2.0.0", same, repinned, added), [same, repinned, added]));

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
    public async Task Update_PinnedMod_StaysAtItsVersion_AndTheOtherModsMove()
    {
        var pinned = Installed(Release("Pinned"), InstallReason.ModPack);
        pinned.SetPinned(true);
        var repinned = Release("Repinned", "1.1.0");
        var newerPin = Release("Pinned", "1.1.0");
        var instance = PackInstance(pinned, Installed(Release("Repinned"), InstallReason.ModPack));
        var instances = new MemoryInstanceRepository(instance);
        var installer = new FakeInstaller(instances);

        var result = await Updater(instances, installer).UpdateAsync(Request(instance.InstanceId, Pack("2.0.0", newerPin, repinned), [newerPin, repinned]));

        Assert.True(result.IsComplete);
        Assert.Contains(new ModPackChange("Pinned", ModPackChangeKind.Pinned, ModVersion.Parse("1.0.0"), ModVersion.Parse("1.0.0")), result.Changes);
        Assert.DoesNotContain(result.Members, member => member.ModId == "Pinned");
        var updated = (await instances.GetByIdAsync(instance.InstanceId))!;
        Assert.Equal(new InstanceSource.FromModPack("Pack", ModVersion.Parse("2.0.0")), updated.Source);
        var kept = updated.Mods.Single(mod => mod.ModId == "Pinned");
        Assert.Equal((ModVersion.Parse("1.0.0"), InstallReason.ModPack, true), (kept.Version, kept.Reason, kept.IsPinned));
        Assert.Equal(ModVersion.Parse("1.1.0"), updated.Mods.Single(mod => mod.ModId == "Repinned").Version);
    }

    [Fact]
    public async Task Update_DetachedMod_StaysAtItsVersion_AndTheOtherModsMove()
    {
        var newerDetached = Release("Detached", "1.1.0");
        var newerMember = Release("Member", "1.1.0");
        var instance = PackInstance(Installed(Release("Detached"), InstallReason.ModPack), Installed(Release("Member"), InstallReason.ModPack));
        instance.DetachFromModPack("Detached");
        var instances = new MemoryInstanceRepository(instance);

        var result = await Updater(instances).UpdateAsync(Request(instance.InstanceId, Pack("2.0.0", newerDetached, newerMember), [newerDetached, newerMember]));

        Assert.True(result.IsComplete);
        Assert.Equal(
            [new ModPackChange("Member", ModPackChangeKind.Change, ModVersion.Parse("1.0.0"), ModVersion.Parse("1.1.0")), new ModPackChange("Detached", ModPackChangeKind.Detached, ModVersion.Parse("1.0.0"), ModVersion.Parse("1.0.0"))],
            result.Changes);
        Assert.DoesNotContain(result.Members, member => member.ModId == "Detached");
        var updated = (await instances.GetByIdAsync(instance.InstanceId))!;
        Assert.Equal(new InstanceSource.FromModPack("Pack", ModVersion.Parse("2.0.0")).WithDetached(["Detached"]), updated.Source);
        var detached = updated.Mods.Single(mod => mod.ModId == "Detached");
        Assert.Equal((ModVersion.Parse("1.0.0"), InstallReason.Manual), (detached.Version, detached.Reason));
        Assert.Equal(ModVersion.Parse("1.1.0"), updated.Mods.Single(mod => mod.ModId == "Member").Version);
    }

    [Fact]
    public async Task Update_DetachedModThatThePlayerRemoved_DoesNotComeBackWithTheNextUpdates()
    {
        var instance = PackInstance(Installed(Release("Removed"), InstallReason.ModPack), Installed(Release("Member"), InstallReason.ModPack));
        instance.DetachFromModPack("Removed");
        instance.RemoveMod("Removed");
        var instances = new MemoryInstanceRepository(instance);
        var updater = Updater(instances);

        foreach (var version in new[] { "2.0.0", "3.0.0" })
        {
            var releases = new[] { Release("Removed", version), Release("Member", version) };
            var result = await updater.UpdateAsync(Request(instance.InstanceId, Pack(version, releases), releases));

            Assert.True(result.IsComplete);
            Assert.Contains(new ModPackChange("Removed", ModPackChangeKind.Detached, null, null), result.Changes);
            var updated = (await instances.GetByIdAsync(instance.InstanceId))!;
            Assert.Equal([("Member", ModVersion.Parse(version))], updated.Mods.Select(mod => (mod.ModId, mod.Version)));
            Assert.Equal(new InstanceSource.FromModPack("Pack", ModVersion.Parse(version)).WithDetached(["Removed"]), updated.Source);
        }
    }

    [Fact]
    public async Task Update_DetachedModsThatAVersionDropsAndALaterVersionPinsAgain_StayAlone()
    {
        var instance = PackInstance(Installed(Release("Kept"), InstallReason.ModPack), Installed(Release("Removed"), InstallReason.ModPack), Installed(Release("Member"), InstallReason.ModPack));
        instance.DetachFromModPack("Kept");
        instance.DetachFromModPack("Removed");
        instance.RemoveMod("Removed");
        var instances = new MemoryInstanceRepository(instance);
        var updater = Updater(instances);
        var dropped = new[] { Release("Member", "2.0.0") };
        Assert.True((await updater.UpdateAsync(Request(instance.InstanceId, Pack("2.0.0", dropped), dropped))).IsComplete);
        var again = new[] { Release("Kept", "3.0.0"), Release("Removed", "3.0.0"), Release("Member", "3.0.0") };

        var result = await updater.UpdateAsync(Request(instance.InstanceId, Pack("3.0.0", again), again));

        Assert.True(result.IsComplete);
        Assert.Equal(
            [
                new ModPackChange("Member", ModPackChangeKind.Change, ModVersion.Parse("2.0.0"), ModVersion.Parse("3.0.0")),
                new ModPackChange("Kept", ModPackChangeKind.Detached, ModVersion.Parse("1.0.0"), ModVersion.Parse("1.0.0")),
                new ModPackChange("Removed", ModPackChangeKind.Detached, null, null),
            ],
            result.Changes);
        var updated = (await instances.GetByIdAsync(instance.InstanceId))!;
        Assert.Equal([("Kept", ModVersion.Parse("1.0.0")), ("Member", ModVersion.Parse("3.0.0"))], updated.Mods.Select(mod => (mod.ModId, mod.Version)).OrderBy(mod => mod.ModId, StringComparer.Ordinal));
        Assert.Equal(new InstanceSource.FromModPack("Pack", ModVersion.Parse("3.0.0")).WithDetached(["Kept", "Removed"]), updated.Source);
    }

    [Fact]
    public async Task Plan_UpdateThatCannotRun_NamesNoDetachedModAsAMember()
    {
        var yanked = Release("Yanked", "2.0.0", yanked: true);
        var newerDetached = Release("Detached", "2.0.0");
        var instance = PackInstance(Installed(Release("Detached"), InstallReason.ModPack));
        instance.DetachFromModPack("Detached");
        var instances = new MemoryInstanceRepository(instance);
        var updater = Updater(instances);
        var pack = Pack("2.0.0", newerDetached, yanked);
        var retracted = new ModPackResult(pack.Id, pack.Version, pack.Metadata, null, new IndexStatus(IndexStatusState.Retracted, "retracted", reason: "Broken pack."), []);

        var needsConfirmation = await updater.PlanAsync(Request(instance.InstanceId, pack, [newerDetached, yanked]));
        var retractedResult = await updater.PlanAsync(Request(instance.InstanceId, retracted, [newerDetached, yanked]));

        Assert.False(needsConfirmation.CanRun);
        Assert.Equal(["Yanked"], needsConfirmation.Members.Select(member => member.ModId));
        Assert.False(retractedResult.CanRun);
        Assert.Equal(["Yanked"], retractedResult.Members.Select(member => member.ModId));
    }

    [Fact]
    public async Task Update_DetachedModThatANewPinNeeds_IsListedAsAChange()
    {
        var newerDetached = Release("Detached", "1.1.0");
        var added = Release("Added", dependencies: [new ModDependency("Detached", ModDependencyKind.Required, minVersion: ModVersion.Parse("1.1.0"))]);
        var instance = PackInstance(Installed(Release("Detached"), InstallReason.ModPack));
        instance.DetachFromModPack("Detached");
        var instances = new MemoryInstanceRepository(instance);

        var result = await Updater(instances).PlanAsync(Request(instance.InstanceId, Pack("2.0.0", Release("Detached", "1.2.0"), added), [newerDetached, added]));

        Assert.Equal(
            [new ModPackChange("Added", ModPackChangeKind.Add, null, ModVersion.Parse("1.0.0")), new ModPackChange("Detached", ModPackChangeKind.Change, ModVersion.Parse("1.0.0"), ModVersion.Parse("1.1.0"))],
            result.Changes);
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
        var updater = Updater(instances);
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
    public async Task Update_StopDuringTheDownload_KeepsTheOldSourceAndTheDroppedMod()
    {
        var added = Release("Added");
        var instance = PackInstance(Installed(Release("Dropped"), InstallReason.ModPack));
        var instances = new MemoryInstanceRepository(instance);
        var uninstaller = new FakeUninstaller(instances);
        var stop = new InstallStop();
        var installer = new FakeInstaller(instances)
        {
            Downloading = async (_, token) =>
            {
                stop.Request();
                await Task.Delay(Timeout.Infinite, token);
            },
        };

        var result = await Updater(instances, installer, uninstaller).UpdateAsync(Request(instance.InstanceId, Pack("2.0.0", added), [added]), stop: stop).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(result.IsStopped);
        Assert.False(result.IsComplete);
        Assert.Equal(ModPackMemberStatus.NotAttempted, result.Members.Single(member => member.ModId == "Added").Status);
        Assert.Equal(ModPackMemberStatus.NotAttempted, result.Members.Single(member => member.ModId == "Dropped").Status);
        Assert.Equal(["Dropped"], result.StillInstalled);
        Assert.Empty(uninstaller.Removed);
        var stoppedInstance = (await instances.GetByIdAsync(instance.InstanceId))!;
        Assert.Equal(new InstanceSource.FromModPack("Pack", ModVersion.Parse("1.0.0")), stoppedInstance.Source);
        Assert.Equal("Dropped", Assert.Single(stoppedInstance.Mods).ModId);
    }

    [Fact]
    public async Task Update_StopAfterTheLastInstall_KeepsTheDroppedModUntilARetryCompletes()
    {
        var added = Release("Added");
        var instance = PackInstance(Installed(Release("Dropped"), InstallReason.ModPack));
        var instances = new MemoryInstanceRepository(instance);
        var uninstaller = new FakeUninstaller(instances);
        var stop = new InstallStop();
        var updater = Updater(instances, uninstaller: uninstaller);
        var request = Request(instance.InstanceId, Pack("2.0.0", added), [added]);
        // the last report of the install comes after it wrote the mod, so the stop arrives between the install and the removals
        var progress = new SynchronousProgress<InstallProgress>(report =>
        {
            if (report.Phase == InstallPhase.Finishing)
                stop.Request();
        });

        var result = await updater.UpdateAsync(request, progress, stop);

        Assert.True(result.IsStopped);
        Assert.Equal(["Dropped"], result.StillInstalled);
        Assert.Empty(uninstaller.Removed);
        Assert.Equal(["Added", "Dropped"], (await instances.GetByIdAsync(instance.InstanceId))!.Mods.Select(mod => mod.ModId).Order());

        var retried = await updater.UpdateAsync(request);

        Assert.True(retried.IsComplete);
        Assert.Empty(retried.StillInstalled);
        Assert.Equal("Added", Assert.Single((await instances.GetByIdAsync(instance.InstanceId))!.Mods).ModId);
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
        var updater = Updater(instances, new FakeInstaller(instances) { FailOnceFor = "Added" });
        var request = Request(instance.InstanceId, Pack("2.0.0", added), [added]);

        var failed = await updater.UpdateAsync(request);

        Assert.False(failed.IsComplete);
        Assert.False(failed.IsStopped);
        Assert.Equal(ModPackMemberStatus.Failed, failed.Members.Single(member => member.ModId == "Added").Status);
        Assert.Equal(ModPackMemberStatus.NotAttempted, failed.Members.Single(member => member.ModId == "Dropped").Status);
        Assert.Equal(["Dropped"], failed.StillInstalled);
        var afterFailure = (await instances.GetByIdAsync(instance.InstanceId))!;
        Assert.Equal(ModVersion.Parse("1.0.0"), Assert.IsType<InstanceSource.FromModPack>(afterFailure.Source).Version);
        Assert.Equal(["Dropped", "Kept", "Player"], afterFailure.Mods.Select(mod => mod.ModId).Order());
        Assert.Equal(InstallReason.ModPack, afterFailure.Mods.Single(mod => mod.ModId == "Kept").Reason);

        var retried = await updater.UpdateAsync(request);

        Assert.True(retried.IsComplete);
        Assert.Empty(retried.StillInstalled);
        var afterRetry = (await instances.GetByIdAsync(instance.InstanceId))!;
        Assert.DoesNotContain("Dropped", afterRetry.Mods.Select(mod => mod.ModId));
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
        var updater = Updater(instances);

        var refused = await updater.PlanAsync(Request(instance.InstanceId, Pack("2.0.0", yanked), [yanked]));
        var accepted = await updater.PlanAsync(Request(instance.InstanceId, Pack("2.0.0", yanked), [yanked]) with { ProceedWithYankedMembers = new HashSet<string> { "added" } });

        Assert.False(refused.CanRun);
        Assert.Contains(refused.Warnings, warning => warning.Kind == PlanningMessageKind.YankedPin);
        Assert.True(accepted.CanRun);
    }

    [Fact]
    public async Task Update_YankedPinThatTheCallerDidNotConfirm_NamesNoModAsStillInstalled()
    {
        var yanked = Release("Added", yanked: true);
        var instance = PackInstance(Installed(Release("Dropped"), InstallReason.ModPack));
        var instances = new MemoryInstanceRepository(instance);
        var uninstaller = new FakeUninstaller(instances);

        var result = await Updater(instances, uninstaller: uninstaller).UpdateAsync(Request(instance.InstanceId, Pack("2.0.0", yanked), [yanked]));

        Assert.False(result.CanRun);
        Assert.Empty(result.StillInstalled);
        Assert.Empty(uninstaller.Removed);
    }

    private static ModPackUpdater Updater(MemoryInstanceRepository instances, FakeInstaller? installer = null, FakeUninstaller? uninstaller = null)
        => new(instances, new FakePlanner(), new InstallPlanExecutor(instances, installer ?? new FakeInstaller(instances), new FakeReplacer(instances)), uninstaller ?? new FakeUninstaller(instances));

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
