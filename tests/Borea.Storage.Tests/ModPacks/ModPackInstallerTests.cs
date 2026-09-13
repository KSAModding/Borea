using Borea.Core.Dependencies;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.State;
using Borea.Storage.ModPacks;

namespace Borea.Storage.Tests.ModPacks;

public sealed class ModPackInstallerTests
{
    [Fact]
    public async Task CreateAndInstall_ExactPinsAndDependency_RecordsReasonsAndPackOriginInTwoInstances()
    {
        var dependency = Release("Dependency");
        var member = Release("Member", dependencies: [new ModDependency("Dependency", ModDependencyKind.Required)]);
        var repository = new FakeModRepository([member, dependency]);
        var instances = new MemoryInstanceRepository();
        var services = Services(instances);
        var pack = Pack(member);

        var first = await services.CreateAndInstallAsync("First", Request(Guid.Empty, pack, repository));
        var second = await services.CreateAndInstallAsync("Second", Request(Guid.Empty, pack, repository));

        Assert.True(first.IsComplete);
        Assert.True(second.IsComplete);
        Assert.NotEqual(first.InstanceId, second.InstanceId);
        foreach (var id in new[] { first.InstanceId, second.InstanceId })
        {
            var instance = await instances.GetByIdAsync(id);
            var source = Assert.IsType<InstanceSource.FromModPack>(instance!.Source);
            Assert.Equal("Pack", source.ModPackId);
            Assert.Equal(InstallReason.ModPack, instance.Mods.Single(value => value.ModId == "Member").Reason);
            Assert.Equal(InstallReason.Dependency, instance.Mods.Single(value => value.ModId == "Dependency").Reason);
        }
    }

    [Fact]
    public async Task Install_UnlistedPin_ReportsAuthorLocationWithoutInstalling()
    {
        var valid = Release("Valid");
        var repository = new FakeModRepository([valid], [Listing("Missing")]);
        var instances = new MemoryInstanceRepository();
        var instance = await instances.CreateAsync("Target", InstanceSource.Custom.Value);
        var services = Services(instances);

        var result = await services.InstallAsync(Request(instance.InstanceId, Pack(valid, Release("Missing")), repository));

        Assert.Equal(2, result.Members.Count);
        var member = Assert.Single(result.Members, value => value.ModId == "Missing");
        Assert.Equal(ModPackMemberStatus.Unresolved, member.Status);
        Assert.Equal("https://example.com/Missing", member.Location);
        Assert.Equal(ModPackMemberStatus.NotAttempted, Assert.Single(result.Members, value => value.ModId == "Valid").Status);
        Assert.Empty((await instances.GetByIdAsync(instance.InstanceId))!.Mods);
    }

    [Fact]
    public async Task CreateAndInstall_PreCanceled_DoesNotCreateInstance()
    {
        var release = Release("Member");
        var repository = new FakeModRepository([release]);
        var instances = new MemoryInstanceRepository();
        var services = Services(instances);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => services.CreateAndInstallAsync("Canceled", Request(Guid.Empty, Pack(release), repository), cancellation.Token));

        Assert.Empty(await instances.GetAllAsync());
    }

    [Fact]
    public async Task Install_RetractedPackAndYankedMember_RequireCallerChoices()
    {
        var yanked = Release("Member", yanked: true);
        var repository = new FakeModRepository([yanked]);
        var instances = new MemoryInstanceRepository();
        var instance = await instances.CreateAsync("Target", InstanceSource.Custom.Value);
        var services = Services(instances);
        var status = new IndexStatus(IndexStatusState.Retracted, "retracted", reason: "Broken pack.");
        var pack = Pack(yanked, status);

        var retracted = await services.InstallAsync(Request(instance.InstanceId, pack, repository));
        var yankedResult = await services.InstallAsync(Request(instance.InstanceId, pack, repository) with { ProceedWithRetractedPack = true });
        var accepted = await services.InstallAsync(Request(instance.InstanceId, pack, repository) with { ProceedWithRetractedPack = true, ProceedWithYankedMembers = new HashSet<string> { "member" } });

        Assert.Contains(retracted.Warnings, value => value.Code == "retracted-pack");
        Assert.Contains(yankedResult.Warnings, value => value.Code == "yanked");
        Assert.True(accepted.IsComplete);
    }

    [Fact]
    public async Task Install_PartialFailureAndRetry_SkipsCompletedMember()
    {
        var first = Release("First");
        var second = Release("Second");
        var repository = new FakeModRepository([first, second]);
        var instances = new MemoryInstanceRepository();
        var instance = await instances.CreateAsync("Target", InstanceSource.Custom.Value);
        var installer = new FakeInstaller(instances) { FailOnceFor = "Second" };
        var service = new ModPackInstaller(instances, new FakePlanner(), installer, new FakeReplacer(instances));
        var request = Request(instance.InstanceId, Pack(first, second), repository);

        var failed = await service.InstallAsync(request);
        var retried = await service.InstallAsync(request);

        Assert.False(failed.IsComplete);
        Assert.True(retried.IsComplete);
        Assert.Equal(1, installer.Counts["First"]);
        Assert.Equal(2, installer.Counts["Second"]);
    }

    [Fact]
    public async Task Install_ChangeDuringDownload_GuardedWriteRefusesMember()
    {
        var member = Release("Member");
        var repository = new FakeModRepository([member]);
        var instances = new MemoryInstanceRepository();
        var instance = await instances.CreateAsync("Target", InstanceSource.Custom.Value);
        var installer = new FakeInstaller(instances) { AddConcurrentMod = Release("Concurrent") };
        var service = new ModPackInstaller(instances, new FakePlanner(), installer, new FakeReplacer(instances));

        var result = await service.InstallAsync(Request(instance.InstanceId, Pack(member), repository));

        Assert.False(result.IsComplete);
        Assert.Equal(ModPackMemberStatus.Failed, Assert.Single(result.Members).Status);
        var final = await instances.GetByIdAsync(instance.InstanceId);
        Assert.DoesNotContain(final!.Mods, value => value.ModId == "Member");
        Assert.Contains(final.Mods, value => value.ModId == "Concurrent");
    }

    [Fact]
    public async Task Install_ExternalDeltaAfterFirstResult_RefusesNextOperation()
    {
        var first = Release("First");
        var second = Release("Second");
        var repository = new FakeModRepository([first, second]);
        var instances = new MemoryInstanceRepository();
        var instance = await instances.CreateAsync("Target", InstanceSource.Custom.Value);
        var installer = new FakeInstaller(instances) { AddAfterGuardedResultFor = "First", AddConcurrentMod = Release("Concurrent") };
        var service = new ModPackInstaller(instances, new FakePlanner(), installer, new FakeReplacer(instances));

        var result = await service.InstallAsync(Request(instance.InstanceId, Pack(first, second), repository));

        Assert.False(result.IsComplete);
        Assert.Equal(ModPackMemberStatus.Installed, Assert.Single(result.Members, value => value.ModId == "First").Status);
        Assert.Equal(ModPackMemberStatus.NotAttempted, Assert.Single(result.Members, value => value.ModId == "Second").Status);
        Assert.Equal(1, installer.Counts["First"]);
        Assert.False(installer.Counts.ContainsKey("Second"));
    }

    [Fact]
    public async Task Install_ExistingManualAndForeignContent_PreservesOwnershipAndReason()
    {
        var member = Release("Member");
        var oldMember = Release("Member", version: "0.9.0");
        var manual = new InstalledMod("Member", oldMember.Version, InstallReason.Manual, DateTimeOffset.UnixEpoch, oldMember);
        var instance = Instance.FromExisting(Guid.NewGuid(), "Target", InstanceSource.Custom.Value, DateTimeOffset.UnixEpoch, [manual], [new ForeignMod("Foreign", [])], false);
        var instances = new MemoryInstanceRepository(instance);
        var repository = new FakeModRepository([member, Release("Foreign")]);
        var service = Services(instances);

        var manualResult = await service.InstallAsync(Request(instance.InstanceId, Pack(member), repository));
        var foreignResult = await service.InstallAsync(Request(instance.InstanceId, Pack(Release("Foreign")), repository));

        Assert.True(manualResult.IsComplete);
        Assert.Equal(InstallReason.Manual, Assert.Single(manualResult.Members).Reason);
        Assert.False(foreignResult.IsComplete);
        Assert.Single((await instances.GetByIdAsync(instance.InstanceId))!.ForeignMods);
    }

    [Fact]
    public async Task Install_NotReadyPlan_ReportsInstalledPinTruthfullyWithoutWrites()
    {
        var installedRelease = Release("Installed");
        var installed = new InstalledMod("Installed", installedRelease.Version, InstallReason.Manual, DateTimeOffset.UnixEpoch, installedRelease);
        var instance = Instance.FromExisting(Guid.NewGuid(), "Target", InstanceSource.Custom.Value, DateTimeOffset.UnixEpoch, [installed], [new ForeignMod("Foreign", [])], false);
        var instances = new MemoryInstanceRepository(instance);
        var foreignRelease = Release("Foreign");
        var repository = new FakeModRepository([installedRelease, foreignRelease]);
        var installer = new FakeInstaller(instances);
        var service = new ModPackInstaller(instances, new FakePlanner(), installer, new FakeReplacer(instances));

        var result = await service.InstallAsync(Request(instance.InstanceId, Pack(installedRelease, foreignRelease), repository));

        Assert.False(result.IsComplete);
        Assert.Equal(ModPackMemberStatus.AlreadyInstalled, Assert.Single(result.Members, value => value.ModId == "Installed").Status);
        Assert.Equal(InstallReason.Manual, Assert.Single(result.Members, value => value.ModId == "Installed").Reason);
        Assert.Equal(ModPackMemberStatus.Unresolved, Assert.Single(result.Members, value => value.ModId == "Foreign").Status);
        Assert.Empty(installer.Counts);
    }

    private static ModPackInstaller Services(MemoryInstanceRepository instances) => new(instances, new FakePlanner(), new FakeInstaller(instances), new FakeReplacer(instances));

    private static ModPackInstallRequest Request(Guid instanceId, ModPackResult pack, IModRepository repository) => new(instanceId, pack, repository);

    private static ModPackResult Pack(params ModVersionMetadata[] releases) => Pack(releases, null);

    private static ModPackResult Pack(ModVersionMetadata release, IndexStatus status) => Pack([release], status);

    private static ModPackResult Pack(IReadOnlyList<ModVersionMetadata> releases, IndexStatus? status)
    {
        var metadata = Metadata(releases.Select(value => new ModPackEntry(value.ModId, value.Version)).ToList());
        return new ModPackResult(metadata.ModPackId, metadata.Version.ToString(), metadata, null, status, []);
    }

    private static ModPackMetadata Metadata(IReadOnlyList<ModPackEntry> entries) => new(1, "Pack", "test", "Pack", ["Author"], "Pack.", "CC0-1.0", new Dictionary<string, string> { ["forums"] = "https://example.com/pack" }, "2026.7", ModVersion.Parse("1.0.0"), DateTimeOffset.UnixEpoch, entries);

    private static ModVersionMetadata Release(string id, string version = "1.0.0", IReadOnlyList<ModDependency>? dependencies = null, bool yanked = false) => new(1, id, ModVersion.Parse(version), ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, "2026.7.4.2131", 2131, new DownloadInfo($"https://example.com/{id}.zip", new string('A', 64), 1, "application/zip"), 1, dependencies ?? [], yanked: yanked, yankedReason: yanked ? "Broken release." : null);

    private static ModMetadata Listing(string id) => new(1, id, "test", id, ["Author"], "Listing.", "MIT", new Dictionary<string, string> { ["forums"] = "https://example.com/forum", ["repository"] = $"https://example.com/{id}" }, "2026.7.4.2131");

    private sealed class FakeModRepository(IReadOnlyList<ModVersionMetadata> releases, IReadOnlyList<ModMetadata>? listings = null) : IModRepository
    {
        public Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default) => Task.FromResult(listings ?? (IReadOnlyList<ModMetadata>)[]);
        public Task<ModVersionMetadata?> GetLatestReleaseAsync(string modId, CancellationToken cancellationToken = default) => Task.FromResult(releases.Where(value => ModIds.Equals(value.ModId, modId) && !value.Yanked).OrderByDescending(value => value.Version).FirstOrDefault());
        public Task<ModVersionMetadata?> GetReleaseAsync(string modId, ModVersion version, CancellationToken cancellationToken = default) => Task.FromResult(releases.FirstOrDefault(value => ModIds.Equals(value.ModId, modId) && value.Version == version));
        public Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ModVersion>>(releases.Where(value => ModIds.Equals(value.ModId, modId)).Select(value => value.Version).ToList());
        public Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default) => Task.FromResult(listings ?? (IReadOnlyList<ModMetadata>)[]);
    }

    private sealed class FakePlanner : IInstallPlanner
    {
        public async Task<InstallPlan> PlanAsync(InstallPlanningRequest request, CancellationToken cancellationToken = default)
        {
            var selected = new Dictionary<string, RequestedMod>(ModIds.Comparer);
            var pending = new Queue<RequestedMod>(request.Requested);
            var conflicts = new List<PlanningMessage>();
            while (pending.Count > 0)
            {
                var item = pending.Dequeue();
                if (!selected.TryAdd(item.Release.ModId, item)) continue;
                if (request.Instance.ForeignMods.Any(value => ModIds.Equals(value.ModId, item.Release.ModId)))
                    conflicts.Add(new PlanningMessage(item.Release.ModId, "foreign-owned", "A managed install cannot replace foreign content."));
                foreach (var dependency in item.Release.Dependencies.Where(value => value.Kind == ModDependencyKind.Required && !value.IsAnyOf))
                {
                    var release = await request.Repository.GetReleaseAsync(dependency.ModId!, ModVersion.Parse("1.0.0"), cancellationToken);
                    if (release is not null) pending.Enqueue(new RequestedMod(release, InstallReason.Dependency));
                }
            }

            var selections = selected.Values.Select(value => new PlannedSelection(value.Release, value.Reason, request.Instance.Mods.Any(installed => ModIds.Equals(installed.ModId, value.Release.ModId) && installed.Version == value.Release.Version))).ToList();
            var operations = selections.Where(value => !value.IsAlreadyInstalled).Select(value => new PlannedInstall(value.Release, value.Reason, Core.Game.GameCompatibility.Compatible, null)).ToList();
            return new InstallPlan(request.Instance.InstanceId, InstallPlanningState.Capture(request.Instance), selections, operations, [], [], conflicts, []);
        }
    }

    private sealed class FakeInstaller(MemoryInstanceRepository instances) : IModInstaller
    {
        public string? FailOnceFor { get; init; }
        public ModVersionMetadata? AddConcurrentMod { get; init; }
        public string? AddAfterGuardedResultFor { get; init; }
        public Dictionary<string, int> Counts { get; } = new(ModIds.Comparer);

        public async Task<InstallResult> InstallAsync(Guid instanceId, ModVersionMetadata release, InstallReason reason, bool enable, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            return (await InstallCoreAsync(instanceId, release, reason, enable, null, cancellationToken)).Result;
        }

        public async Task<GuardedInstallResult> InstallGuardedAsync(Guid instanceId, ModVersionMetadata release, InstallReason reason, bool enable, InstallPlanningState expectedState, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            return await InstallCoreAsync(instanceId, release, reason, enable, expectedState, cancellationToken);
        }

        private async Task<GuardedInstallResult> InstallCoreAsync(Guid instanceId, ModVersionMetadata release, InstallReason reason, bool enable, InstallPlanningState? expectedState, CancellationToken cancellationToken)
        {
            Counts[release.ModId] = Counts.GetValueOrDefault(release.ModId) + 1;
            if (ModIds.Equals(FailOnceFor ?? string.Empty, release.ModId) && Counts[release.ModId] == 1) throw new IOException("Injected failure.");
            if (AddConcurrentMod is not null && AddAfterGuardedResultFor is null)
                await AddExternalAsync(instanceId, AddConcurrentMod, cancellationToken);
            var installed = new InstalledMod(release.ModId, release.Version, reason, DateTimeOffset.UnixEpoch, release);
            var state = await instances.UpdateAsync(instanceId, value =>
            {
                if (expectedState is not null && !expectedState.Matches(value))
                    throw new InvalidOperationException("The instance changed after planning.");
                value.AddMod(installed);
                return InstallPlanningState.Capture(value);
            }, cancellationToken);
            var result = new InstallResult(installed, new DownloadResult(release.Download.Url, 1, release.Download.Sha256!), ModEntryAddResult.Added);
            if (AddConcurrentMod is not null && ModIds.Equals(AddAfterGuardedResultFor ?? string.Empty, release.ModId))
                await AddExternalAsync(instanceId, AddConcurrentMod, cancellationToken);
            return new GuardedInstallResult(result, state);
        }

        private Task AddExternalAsync(Guid instanceId, ModVersionMetadata release, CancellationToken cancellationToken) => instances.UpdateAsync(instanceId, value =>
        {
            value.AddMod(new InstalledMod(release.ModId, release.Version, InstallReason.Manual, DateTimeOffset.UnixEpoch, release));
            return true;
        }, cancellationToken);
    }

    private sealed class FakeReplacer(MemoryInstanceRepository instances) : IModReplacer
    {
        public async Task<ModReplacementResult> ReplaceAsync(Guid instanceId, InstalledMod expectedCurrent, ModVersionMetadata replacement, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var installed = new InstalledMod(replacement.ModId, replacement.Version, expectedCurrent.Reason, DateTimeOffset.UnixEpoch, replacement);
            await instances.UpdateAsync(instanceId, value => { value.ReplaceMod(installed); return true; }, cancellationToken);
            return new ModReplacementResult(expectedCurrent, installed, new DownloadResult(replacement.Download.Url, 1, replacement.Download.Sha256!), null);
        }

        public async Task<GuardedModReplacementResult> ReplaceGuardedAsync(Guid instanceId, InstalledMod expectedCurrent, ModVersionMetadata replacement, InstallPlanningState expectedState, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var result = await instances.UpdateAsync(instanceId, value =>
            {
                if (!expectedState.Matches(value))
                    throw new InvalidOperationException("The instance changed after planning.");
                var installed = new InstalledMod(replacement.ModId, replacement.Version, expectedCurrent.Reason, DateTimeOffset.UnixEpoch, replacement);
                value.ReplaceMod(installed);
                return new GuardedModReplacementResult(new ModReplacementResult(expectedCurrent, installed, new DownloadResult(replacement.Download.Url, 1, replacement.Download.Sha256!), null), InstallPlanningState.Capture(value));
            }, cancellationToken);
            return result;
        }
    }

    private sealed class MemoryInstanceRepository : IInstanceRepository
    {
        private readonly Dictionary<Guid, Instance> _values = [];
        public MemoryInstanceRepository() { }
        public MemoryInstanceRepository(Instance instance) => _values[instance.InstanceId] = instance;
        public Task<IReadOnlyList<Instance>> GetAllAsync() => Task.FromResult<IReadOnlyList<Instance>>(_values.Values.ToList());
        public Task<Instance?> GetByIdAsync(Guid instanceId) => Task.FromResult(_values.GetValueOrDefault(instanceId));
        public Task<Guid?> GetActiveInstanceIdAsync() => Task.FromResult<Guid?>(null);
        public Task SetActiveInstanceAsync(Guid instanceId) => Task.CompletedTask;
        public Task<bool> IsNameAvailableAsync(string name, Guid? excludingInstanceId = null) => Task.FromResult(true);
        public Task<Instance> CreateAsync(string name, InstanceSource source) { var value = new Instance(name, source); _values[value.InstanceId] = value; return Task.FromResult(value); }
        public Task RenameAsync(Guid instanceId, string newName) { _values[instanceId].Rename(newName); return Task.CompletedTask; }
        public Task DeleteAsync(Guid instanceId) { _values.Remove(instanceId); return Task.CompletedTask; }
        public Task SaveAsync(Instance instance) { _values[instance.InstanceId] = instance; return Task.CompletedTask; }
        public Task<TResult> UpdateAsync<TResult>(Guid instanceId, Func<Instance, TResult> update, CancellationToken cancellationToken = default) => Task.FromResult(update(_values[instanceId]));
    }
}
