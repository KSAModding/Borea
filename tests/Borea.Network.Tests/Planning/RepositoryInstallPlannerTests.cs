using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Network.Planning;

namespace Borea.Network.Tests.Planning;

public sealed class RepositoryInstallPlannerTests
{
    [Fact]
    public async Task PlanAsync_TransitiveChain_OrdersDependenciesFirst()
    {
        var c = Release("C");
        var b = Release("B", dependencies: [Required("C")]);
        var a = Release("A", dependencies: [Required("B")]);
        var plan = await PlanAsync([a], [a, b, c]);
        Assert.True(plan.IsReady);
        Assert.Equal(["C", "B", "A"], plan.Operations.Select(value => value.Release.ModId));
    }

    [Fact]
    public async Task PlanAsync_CombinedBounds_SelectsSharedCompatibleVersion()
    {
        var b2 = Release("B", "2.0.0");
        var b3 = Release("B", "3.0.0");
        var a = Release("A", dependencies: [Required("B", "2.0.0")]);
        var c = Release("C", dependencies: [Required("B", max: "2.0.0")]);
        var plan = await PlanAsync([a, c], [a, c, b2, b3]);
        Assert.True(plan.IsReady);
        Assert.Equal(ModVersion.Parse("2.0.0"), Assert.Single(plan.Selections, value => value.Release.ModId == "B").Release.Version);
    }

    [Fact]
    public async Task PlanAsync_PlannedConflict_IsBlocking()
    {
        var a = Release("A", dependencies: [new ModDependency("B", ModDependencyKind.Conflict)]);
        var b = Release("B");
        var plan = await PlanAsync([a, b], [a, b]);
        Assert.False(plan.IsReady);
        Assert.Contains(plan.Conflicts, value => value.Code == "dependency-conflict");
    }

    [Fact]
    public async Task PlanAsync_SatisfyingInstalledDependency_DoesNotPlanWrite()
    {
        var installed = Installed("B", "1.0.0");
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [installed], false);
        var a = Release("A", dependencies: [Required("B")]);
        var repository = new FakeRepository([a]);
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(new InstallPlanningRequest(instance, [new RequestedMod(a, InstallReason.Manual)], repository));
        Assert.True(plan.IsReady);
        Assert.DoesNotContain(plan.Operations, value => value.Release.ModId == "B");
        Assert.Equal(0, repository.ReleaseReads);
    }

    [Fact]
    public async Task PlanAsync_UnboundedForeignDependency_IsSatisfiedWithoutRepositoryRead()
    {
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [], [new ForeignMod("B")], false);
        var a = Release("A", dependencies: [Required("B")]);
        var repository = new FakeRepository([a]);
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(new InstallPlanningRequest(instance, [new RequestedMod(a, InstallReason.Manual)], repository));
        Assert.True(plan.IsReady);
        Assert.Equal(0, repository.VersionReads);
    }

    [Fact]
    public async Task PlanAsync_Cycle_IsStableAndDoesNotWriteInstance()
    {
        var a = Release("A", dependencies: [Required("B")]);
        var b = Release("B", dependencies: [Required("A")]);
        var repository = new FakeRepository([a, b]);
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(new InstallPlanningRequest(instance, [new RequestedMod(a, InstallReason.Manual)], repository));
        Assert.True(plan.IsReady);
        Assert.Empty(instance.Mods);
        Assert.Equal(2, plan.Operations.Count);
    }

    [Fact]
    public async Task PlanAsync_RetainedInstalledDependencyConstrainsUpdate()
    {
        var x = Installed("X", dependencies: [Required("B", max: "1.0.0")]);
        var b1 = Release("B");
        var b2 = Release("B", "2.0.0");
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [x, Installed("B")], false);
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(new InstallPlanningRequest(instance, [new RequestedMod(b2, InstallReason.Manual)], new FakeRepository([b1, b2])));
        Assert.False(plan.IsReady);
        Assert.Contains(plan.Conflicts, value => value.Code == "retained-dependent");
    }

    [Fact]
    public async Task PlanAsync_NonExactRequestCanSelectCompatibleVersion()
    {
        var b1 = Release("B");
        var b2 = Release("B", "2.0.0");
        var a = Release("A", dependencies: [Required("B", max: "1.0.0")]);
        var request = new InstallPlanningRequest(new Instance("Test", InstanceSource.Custom.Value), [new RequestedMod(a, InstallReason.Manual), new RequestedMod(b2, InstallReason.Manual, Exact: false)], new FakeRepository([a, b1, b2]));
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(request);
        Assert.True(plan.IsReady);
        Assert.Contains(plan.Selections, value => value.Release.ModId == "B" && value.Release.Version == ModVersion.Parse("1.0.0"));
    }

    [Fact]
    public async Task PlanAsync_VersionDependentGraphBacktracksWithoutOscillation()
    {
        var c = Release("C", dependencies: [Required("B", max: "1.0.0")]);
        var b1 = Release("B");
        var b2 = Release("B", "2.0.0", [Required("C")]);
        var a = Release("A", dependencies: [Required("B")]);
        var plan = await PlanAsync([a], [a, b1, b2, c]);
        Assert.True(plan.IsReady);
        Assert.Equal(["B", "A"], plan.Operations.Select(value => value.Release.ModId));
    }

    [Fact]
    public async Task PlanAsync_DirectForeignIdRequest_IsBlocking()
    {
        var b = Release("B");
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [], [new ForeignMod("B")], false);
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(new InstallPlanningRequest(instance, [new RequestedMod(b, InstallReason.Manual)], new FakeRepository([b])));
        Assert.False(plan.IsReady);
        Assert.Contains(plan.Conflicts, value => value.Code == "foreign-owned");
    }

    [Fact]
    public async Task PlanAsync_InstalledAlternativeNeedsNoChoice()
    {
        var dependency = ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("B"), new ModDependencyAlternative("C")]);
        var a = Release("A", dependencies: [dependency]);
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [Installed("C")], false);
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(new InstallPlanningRequest(instance, [new RequestedMod(a, InstallReason.Manual)], new FakeRepository([a])));
        Assert.True(plan.IsReady);
        Assert.Equal("C", Assert.Single(plan.Choices).Selected);
    }

    [Fact]
    public async Task PlanAsync_SelectedAlternative_IsOrderedBeforeDependent()
    {
        var dependency = ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("B"), new ModDependencyAlternative("C")]);
        var a = Release("A", dependencies: [dependency]);
        var b = Release("B");
        var choices = new Dictionary<string, string> { ["A:dependency:0:alternative"] = "B" };
        var request = new InstallPlanningRequest(new Instance("Test", InstanceSource.Custom.Value), [new RequestedMod(a, InstallReason.Manual)], new FakeRepository([a, b]), Alternatives: choices);
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(request);
        Assert.Equal(["B", "A"], plan.Operations.Select(value => value.Release.ModId));
    }

    [Fact]
    public async Task PlanAsync_BoundedConflictWithForeignMod_IsUnknown()
    {
        var a = Release("A", dependencies: [new ModDependency("B", ModDependencyKind.Conflict, ModVersion.Parse("2.0.0"))]);
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [], [new ForeignMod("B")], false);
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(new InstallPlanningRequest(instance, [new RequestedMod(a, InstallReason.Manual)], new FakeRepository([a])));
        Assert.False(plan.IsReady);
        Assert.Empty(plan.Conflicts);
        Assert.Contains(plan.UnresolvedChoices, value => value.Code == "foreign-conflict-unknown");
    }

    [Fact]
    public async Task PlanAsync_RecommendationExposesStableCallerChoice()
    {
        var a = Release("A", dependencies: [new ModDependency("B", ModDependencyKind.Recommends)]);
        var plan = await PlanAsync([a], [a, Release("B")]);
        var choice = Assert.Single(plan.Choices);
        Assert.Equal("A:dependency:0:recommendation", choice.Key);
        Assert.Equal(["include", "exclude"], choice.Options);
        Assert.Equal("exclude", choice.Selected);
    }

    [Fact]
    public async Task PlanAsync_ReplacedInstalledVersionDoesNotCauseStaleConflict()
    {
        var a = Release("A", dependencies: [new ModDependency("B", ModDependencyKind.Conflict, maxVersion: ModVersion.Parse("1.0.0"))]);
        var b2 = Release("B", "2.0.0");
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [Installed("B")], false);
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(new InstallPlanningRequest(instance, [new RequestedMod(a, InstallReason.Manual), new RequestedMod(b2, InstallReason.Manual)], new FakeRepository([a, b2])));
        Assert.True(plan.IsReady);
    }

    [Fact]
    public async Task PlanAsync_UnsatisfiedInstalledDependencyCanBeUpdated()
    {
        var a = Release("A", dependencies: [Required("B", "2.0.0")]);
        var b2 = Release("B", "2.0.0");
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [Installed("B")], false);
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(new InstallPlanningRequest(instance, [new RequestedMod(a, InstallReason.Manual)], new FakeRepository([a, b2])));
        Assert.True(plan.IsReady);
        Assert.Contains(plan.Operations, value => value.Release.ModId == "B" && value.Release.Version == ModVersion.Parse("2.0.0"));
    }

    [Fact]
    public async Task PlanAsync_UnselectedAlternativeReturnsChoice()
    {
        var dependency = ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("B"), new ModDependencyAlternative("C")]);
        var a = Release("A", dependencies: [dependency]);
        var plan = await PlanAsync([a], [a, Release("B"), Release("C")]);
        Assert.False(plan.IsReady);
        Assert.Contains(plan.UnresolvedChoices, value => value.Code == "alternative-choice");
        Assert.Null(Assert.Single(plan.Choices).Selected);
    }

    [Fact]
    public async Task PlanAsync_TruncatedSearchIsBlocking()
    {
        var available = new List<ModVersionMetadata>();
        var requested = new List<RequestedMod>();
        for (var id = 0; id < 6; id++)
        {
            for (var version = 0; version < 10; version++) available.Add(Release($"M{id}", $"1.0.{version}"));
            requested.Add(new RequestedMod(available[^1], InstallReason.Manual, Exact: false));
        }
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(new InstallPlanningRequest(new Instance("Test", InstanceSource.Custom.Value), requested, new FakeRepository(available)));
        Assert.False(plan.IsReady);
        Assert.Contains(plan.Conflicts, value => value.Code == "search-limit");
    }

    [Fact]
    public async Task PlanAsync_RetainedInstalledAlternativeConstrainsReplacement()
    {
        var dependency = ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("B", maxVersion: ModVersion.Parse("1.0.0")), new ModDependencyAlternative("C")]);
        var x = Installed("X", dependencies: [dependency]);
        var b2 = Release("B", "2.0.0");
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [x, Installed("B")], false);
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(new InstallPlanningRequest(instance, [new RequestedMod(b2, InstallReason.Manual)], new FakeRepository([b2])));
        Assert.False(plan.IsReady);
        Assert.Contains(plan.Conflicts, value => value.Code == "retained-alternative");
    }

    [Fact]
    public async Task PlanAsync_NonExactYankedRootUsesUsableRelease()
    {
        var b1 = Release("B");
        var b2 = Release("B", "2.0.0", yanked: true);
        var request = new InstallPlanningRequest(new Instance("Test", InstanceSource.Custom.Value), [new RequestedMod(b2, InstallReason.Manual, Exact: false)], new FakeRepository([b1, b2]));
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(request);
        Assert.True(plan.IsReady);
        Assert.Equal(ModVersion.Parse("1.0.0"), Assert.Single(plan.Selections).Release.Version);
    }

    [Fact]
    public async Task PlanAsync_BoundedForeignAlternativeIsUnknown()
    {
        var dependency = ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("B", minVersion: ModVersion.Parse("2.0.0")), new ModDependencyAlternative("C")]);
        var a = Release("A", dependencies: [dependency]);
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [], [new ForeignMod("B")], false);
        var alternatives = new Dictionary<string, string> { ["A:dependency:0:alternative"] = "B" };
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(new InstallPlanningRequest(instance, [new RequestedMod(a, InstallReason.Manual)], new FakeRepository([a]), Alternatives: alternatives));
        Assert.False(plan.IsReady);
        Assert.Empty(plan.Conflicts);
        Assert.Contains(plan.UnresolvedChoices, value => value.Code == "foreign-alternative-unknown");
    }

    [Fact]
    public async Task PlanAsync_InstalledDependencyIsSelectedAndItsDependencyIsPlanned()
    {
        var b = Installed("B", dependencies: [Required("C")]);
        var a = Release("A", dependencies: [Required("B")]);
        var c = Release("C");
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [b], false);
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(new InstallPlanningRequest(instance, [new RequestedMod(a, InstallReason.Manual)], new FakeRepository([a, c])));
        Assert.True(plan.IsReady);
        Assert.Contains(plan.Selections, value => value.Release.ModId == "B" && value.IsAlreadyInstalled);
        Assert.Contains(plan.Operations, value => value.Release.ModId == "C");
    }

    [Fact]
    public async Task PlanAsync_ReplacedInstalledAlternativeUsesProposedVersion()
    {
        var dependency = ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("B", maxVersion: ModVersion.Parse("1.0.0")), new ModDependencyAlternative("C")]);
        var a = Release("A", dependencies: [dependency]);
        var b2 = Release("B", "2.0.0");
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [Installed("B")], false);
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(new InstallPlanningRequest(instance, [new RequestedMod(a, InstallReason.Manual), new RequestedMod(b2, InstallReason.Manual)], new FakeRepository([a, b2])));
        Assert.False(plan.IsReady);
        Assert.Contains(plan.UnresolvedChoices, value => value.Code == "alternative-choice");
    }

    [Fact]
    public async Task PlanAsync_ExplicitRootSatisfiesAlternativeWithoutChoice()
    {
        var dependency = ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("B"), new ModDependencyAlternative("C")]);
        var a = Release("A", dependencies: [dependency]);
        var b = Release("B");
        var plan = await PlanAsync([a, b], [a, b, Release("C")]);
        Assert.True(plan.IsReady);
        Assert.Equal("B", Assert.Single(plan.Choices).Selected);
    }

    [Fact]
    public async Task PlanAsync_RetainedBoundedForeignAlternativeIsUnknown()
    {
        var dependency = ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("B", minVersion: ModVersion.Parse("2.0.0")), new ModDependencyAlternative("C")]);
        var x = Installed("X", dependencies: [dependency]);
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [x], [new ForeignMod("B")], false);
        var a = Release("A");
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(new InstallPlanningRequest(instance, [new RequestedMod(a, InstallReason.Manual)], new FakeRepository([a])));
        Assert.False(plan.IsReady);
        Assert.Empty(plan.Conflicts);
        Assert.Contains(plan.UnresolvedChoices, value => value.Code == "foreign-alternative-unknown");
    }

    [Fact]
    public async Task PlanAsync_RecommendedAlternativeHasUniqueChoiceKeys()
    {
        var dependency = ModDependency.OfAlternatives(ModDependencyKind.Recommends, [new ModDependencyAlternative("B"), new ModDependencyAlternative("C")]);
        var a = Release("A", dependencies: [dependency]);
        var recommended = new HashSet<string> { "A:dependency:0:recommendation" };
        var request = new InstallPlanningRequest(new Instance("Test", InstanceSource.Custom.Value), [new RequestedMod(a, InstallReason.Manual)], new FakeRepository([a, Release("B"), Release("C")]), Recommended: recommended);
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(request);
        Assert.Equal(2, plan.Choices.Select(value => value.Key).Distinct().Count());
        Assert.Contains(plan.Choices, value => value.Key == "A:dependency:0:recommendation");
        Assert.Contains(plan.Choices, value => value.Key == "A:dependency:0:alternative");
    }

    [Fact]
    public async Task PlanAsync_CircularAlternativesDoNotChooseEachOther()
    {
        var choice = ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("B"), new ModDependencyAlternative("C")]);
        var a = Release("A", dependencies: [choice]);
        var c = Release("C", dependencies: [Required("B")]);
        var plan = await PlanAsync([a], [a, Release("B"), c]);
        Assert.False(plan.IsReady);
        Assert.Contains(plan.UnresolvedChoices, value => value.Code == "alternative-choice");
    }

    [Fact]
    public async Task PlanAsync_RetainedBoundedForeignDependencyIsUnknown()
    {
        var x = Installed("X", dependencies: [Required("B", "2.0.0")]);
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [x], [new ForeignMod("B")], false);
        var a = Release("A");
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(new InstallPlanningRequest(instance, [new RequestedMod(a, InstallReason.Manual)], new FakeRepository([a])));
        Assert.False(plan.IsReady);
        Assert.Empty(plan.Conflicts);
        Assert.Contains(plan.UnresolvedChoices, value => value.Code == "foreign-version-unknown");
    }

    [Fact]
    public async Task PlanAsync_IncompatibleAvailableDependencyKeepsCompatibilityDecision()
    {
        var a = Release("A", dependencies: [Required("B")]);
        var b = Release("B", gameMinRevision: 5000);
        var request = new InstallPlanningRequest(new Instance("Test", InstanceSource.Custom.Value), [new RequestedMod(a, InstallReason.Manual)], new FakeRepository([a, b]), new Borea.Core.Game.GameVersion(2026, 7, 4, 2131));
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(request);
        Assert.Contains(plan.Conflicts, value => value.Code == "incompatible" && value.ModId == "B");
        Assert.DoesNotContain(plan.Conflicts, value => value.Code == "unsatisfied-dependency");
    }

    [Fact]
    public async Task PlanAsync_YankedNonExactRootWithoutReplacementKeepsWarning()
    {
        var b = Release("B", "2.0.0", yanked: true);
        var request = new InstallPlanningRequest(new Instance("Test", InstanceSource.Custom.Value), [new RequestedMod(b, InstallReason.Manual, Exact: false)], new FakeRepository([]));
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(request);
        Assert.Contains(plan.Selections, value => value.Release.Version == b.Version);
        Assert.Contains(plan.Warnings, value => value.Code == "yanked");
    }

    [Fact]
    public async Task PlanAsync_InstalledFallbackKeepsExplicitReason()
    {
        var a = Release("A", dependencies: [Required("B", max: "1.0.0")]);
        var b2 = Release("B", "2.0.0");
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [Installed("B")], false);
        var requested = new[] { new RequestedMod(a, InstallReason.Manual), new RequestedMod(b2, InstallReason.ModPack, Exact: false) };
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(new InstallPlanningRequest(instance, requested, new FakeRepository([a, b2])));
        Assert.Equal(InstallReason.ModPack, Assert.Single(plan.Selections, value => value.Release.ModId == "B").Reason);
    }

    [Fact]
    public async Task PlanAsync_UnsupportedPlatformIsExposed()
    {
        var a = Release("A", os: ["windows"]);
        var request = new InstallPlanningRequest(new Instance("Test", InstanceSource.Custom.Value), [new RequestedMod(a, InstallReason.Manual)], new FakeRepository([a]), TargetPlatform: Borea.Core.Game.OsPlatform.Linux);
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(request);
        Assert.True(plan.IsReady);
        Assert.Contains(plan.Warnings, value => value.Code == "platform");
        Assert.False(Assert.Single(plan.Operations).PlatformSupport!.IsSupported);
    }

    [Fact]
    public void PlanningState_OwnershipChange_DoesNotMatch()
    {
        var release = Release("A");
        var owned = new InstalledMod("A", release.Version, InstallReason.Manual, DateTimeOffset.UnixEpoch, release, ownership: ModInstallOwnership.Borea, ownershipToken: "token");
        var foreignOwned = new InstalledMod("A", release.Version, InstallReason.Manual, DateTimeOffset.UnixEpoch, release, ownership: ModInstallOwnership.Foreign);
        var before = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UnixEpoch, [owned], false);
        var after = Instance.FromExisting(before.InstanceId, "Test", InstanceSource.Custom.Value, DateTimeOffset.UnixEpoch, [foreignOwned], false);
        var state = InstallPlanningState.Capture(before);
        Assert.False(state.Matches(after));
    }

    [Fact]
    public void PlanningState_ForeignDependencyChange_DoesNotMatch()
    {
        var beforeMod = new ForeignMod("A", [new LocalModDependency("B", optional: false)]);
        var afterMod = new ForeignMod("A", [new LocalModDependency("B", optional: true)]);
        var before = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UnixEpoch, [], [beforeMod], false);
        var after = Instance.FromExisting(before.InstanceId, "Test", InstanceSource.Custom.Value, DateTimeOffset.UnixEpoch, [], [afterMod], false);
        var state = InstallPlanningState.Capture(before);
        Assert.False(state.Matches(after));
    }

    [Fact]
    public void PlanningState_InstalledReleaseDecisionChange_DoesNotMatch()
    {
        var available = Release("A");
        var yanked = Release("A", yanked: true);
        var beforeMod = new InstalledMod("A", available.Version, InstallReason.Manual, DateTimeOffset.UnixEpoch, available);
        var afterMod = new InstalledMod("A", yanked.Version, InstallReason.Manual, DateTimeOffset.UnixEpoch, yanked);
        var before = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UnixEpoch, [beforeMod], false);
        var after = Instance.FromExisting(before.InstanceId, "Test", InstanceSource.Custom.Value, DateTimeOffset.UnixEpoch, [afterMod], false);
        Assert.False(InstallPlanningState.Capture(before).Matches(after));
    }

    [Fact]
    public void PlanningState_AlternativeOrderChange_DoesNotMatch()
    {
        var first = ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("B"), new ModDependencyAlternative("C")]);
        var second = ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("C"), new ModDependencyAlternative("B")]);
        var before = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UnixEpoch, [Installed("A", dependencies: [first])], false);
        var after = Instance.FromExisting(before.InstanceId, "Test", InstanceSource.Custom.Value, DateTimeOffset.UnixEpoch, [Installed("A", dependencies: [second])], false);
        Assert.False(InstallPlanningState.Capture(before).Matches(after));
    }

    [Fact]
    public void PlanningState_NullAndEmptyYankReasons_DoNotMatch()
    {
        var nullReason = Release("A", yanked: true);
        var emptyReason = new ModVersionMetadata(1, "A", nullReason.Version, ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, "2026.7.4.2131", 2131, nullReason.Download, 1, [], yanked: true, yankedReason: string.Empty);
        var before = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UnixEpoch, [new InstalledMod("A", nullReason.Version, InstallReason.Manual, DateTimeOffset.UnixEpoch, nullReason)], false);
        var after = Instance.FromExisting(before.InstanceId, "Test", InstanceSource.Custom.Value, DateTimeOffset.UnixEpoch, [new InstalledMod("A", emptyReason.Version, InstallReason.Manual, DateTimeOffset.UnixEpoch, emptyReason)], false);
        Assert.False(InstallPlanningState.Capture(before).Matches(after));
    }

    [Fact]
    public void PlanningState_ForeignCollectionBoundaries_DoNotCollide()
    {
        var firstMods = new[]
        {
            new ForeignMod("A", [new LocalModDependency("foreign", optional: false)]),
            new ForeignMod("X", dependencyReadError: "required"),
        };
        var secondMods = new[]
        {
            new ForeignMod("A"),
            new ForeignMod("required", [new LocalModDependency("X", optional: false)], "foreign"),
        };
        var first = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UnixEpoch, [], firstMods, false);
        var second = Instance.FromExisting(first.InstanceId, "Test", InstanceSource.Custom.Value, DateTimeOffset.UnixEpoch, [], secondMods, false);
        Assert.NotEqual(InstallPlanningState.Capture(first), InstallPlanningState.Capture(second));
    }

    private static Task<InstallPlan> PlanAsync(IReadOnlyList<ModVersionMetadata> requested, IReadOnlyList<ModVersionMetadata> available) => new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(new InstallPlanningRequest(new Instance("Test", InstanceSource.Custom.Value), requested.Select(value => new RequestedMod(value, InstallReason.Manual)).ToList(), new FakeRepository(available)));
    private static ModDependency Required(string id, string? min = null, string? max = null) => new(id, ModDependencyKind.Required, min is null ? null : ModVersion.Parse(min), max is null ? null : ModVersion.Parse(max));
    private static InstalledMod Installed(string id, string version = "1.0.0", IReadOnlyList<ModDependency>? dependencies = null) => new(id, ModVersion.Parse(version), InstallReason.Manual, DateTimeOffset.UtcNow, Release(id, version, dependencies));
    private static ModVersionMetadata Release(string id, string version = "1.0.0", IReadOnlyList<ModDependency>? dependencies = null, bool yanked = false, int gameMinRevision = 2131, IReadOnlyList<string>? os = null) => new(1, id, ModVersion.Parse(version), ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, gameMinRevision == 2131 ? "2026.7.4.2131" : $"2026.7.4.{gameMinRevision}", gameMinRevision, new DownloadInfo("https://example.com/mod.zip", new string('A', 64), 1, "application/zip"), 1, dependencies ?? [], os: os, yanked: yanked);

    private sealed class FakeRepository(IReadOnlyList<ModVersionMetadata> releases) : IModRepository
    {
        public int VersionReads { get; private set; }
        public int ReleaseReads { get; private set; }
        public Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default) { VersionReads++; return Task.FromResult<IReadOnlyList<ModVersion>>(releases.Where(value => ModIds.Equals(value.ModId, modId)).Select(value => value.Version).ToList()); }
        public Task<ModVersionMetadata?> GetReleaseAsync(string modId, ModVersion version, CancellationToken cancellationToken = default) { ReleaseReads++; return Task.FromResult(releases.FirstOrDefault(value => ModIds.Equals(value.ModId, modId) && value.Version == version)); }
        public Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ModMetadata>>([]);
        public Task<ModVersionMetadata?> GetLatestReleaseAsync(string modId, CancellationToken cancellationToken = default) => Task.FromResult<ModVersionMetadata?>(null);
        public Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ModMetadata>>([]);
    }
}
