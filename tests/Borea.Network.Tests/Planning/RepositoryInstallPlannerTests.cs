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
    public async Task PlanAsync_InstalledRecommendationNeedsNoChoice()
    {
        var a = Release("A", dependencies: [new ModDependency("B", ModDependencyKind.Recommends)]);
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [Installed("B")], false);
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(new InstallPlanningRequest(instance, [new RequestedMod(a, InstallReason.Manual)], new FakeRepository([a, Release("B")])));
        Assert.True(plan.IsReady);
        Assert.Empty(plan.Choices);
    }

    [Fact]
    public async Task PlanAsync_InstalledOwnerAsksAgainAboutNoRecommendationOrSuggestion()
    {
        var dependencies = new[] { new ModDependency("B", ModDependencyKind.Recommends), new ModDependency("C", ModDependencyKind.Suggests) };
        var installed = Installed("A", dependencies: dependencies);
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [installed], false);
        var request = new InstallPlanningRequest(instance, [new RequestedMod(installed.Metadata, InstallReason.Manual, Exact: false)], new FakeRepository([installed.Metadata, Release("B"), Release("C")]));
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(request);
        Assert.True(plan.IsReady);
        Assert.Empty(plan.Operations);
        Assert.Empty(plan.Choices);
    }

    [Fact]
    public async Task PlanAsync_ReplacingReleaseAsksOnlyAboutItsNewRecommendation()
    {
        var installed = Installed("A", dependencies: [new ModDependency("B", ModDependencyKind.Recommends)]);
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [installed], false);
        var update = Release("A", "1.1.0", dependencies: [new ModDependency("B", ModDependencyKind.Recommends), new ModDependency("C", ModDependencyKind.Recommends)]);
        var request = new InstallPlanningRequest(instance, [new RequestedMod(installed.Metadata, InstallReason.Manual, Exact: false)], new FakeRepository([installed.Metadata, update, Release("B"), Release("C")]));
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(request);
        Assert.Equal("1.1.0", Assert.Single(plan.Operations).Release.Version.ToString());
        Assert.Equal("A:dependency:1:recommendation", Assert.Single(plan.Choices).Key);
    }

    [Fact]
    public async Task PlanAsync_IncludedRecommendationKeepsItsChoice()
    {
        var recommendation = new ModDependency("B", ModDependencyKind.Recommends);
        var a = Release("A", dependencies: [recommendation]);
        var request = new InstallPlanningRequest(EmptyInstance(), [new RequestedMod(a, InstallReason.Manual)], new FakeRepository([a, Release("B")]), Recommended: new HashSet<string> { "A:dependency:0:recommendation" });
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(request);
        Assert.True(plan.IsReady);
        Assert.Equal(["B", "A"], plan.Operations.Select(value => value.Release.ModId));
        var choice = Assert.Single(plan.Choices);
        Assert.Equal("include", choice.Selected);
        Assert.Same(recommendation, choice.Dependency);
    }

    [Fact]
    public async Task PlanAsync_NewestRequestWithADependency_DoesNotFallBackToAnOlderRelease()
    {
        var newest = Release("A", "2.0.0", dependencies: [Required("B")]);
        var request = new InstallPlanningRequest(EmptyInstance(), [new RequestedMod(newest, InstallReason.Manual, Exact: false)], new FakeRepository([Release("A"), newest, Release("B")]));
        var plan = await new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(request);
        Assert.True(plan.IsReady);
        Assert.Equal(["B 1.0.0", "A 2.0.0"], plan.Operations.Select(value => $"{value.Release.ModId} {value.Release.Version}"));
    }

    [Fact]
    public async Task PlanAsync_SuggestionIsListedAndNotInstalled()
    {
        var suggestion = new ModDependency("B", ModDependencyKind.Suggests);
        var a = Release("A", dependencies: [suggestion]);
        var plan = await PlanAsync([a], [a, Release("B")]);
        Assert.True(plan.IsReady);
        Assert.Equal("A", Assert.Single(plan.Operations).Release.ModId);
        var choice = Assert.Single(plan.Choices);
        Assert.Equal(PlanningChoiceKind.Suggestion, choice.Kind);
        Assert.Equal(["B"], choice.Options);
        Assert.Null(choice.Selected);
        Assert.Same(suggestion, choice.Dependency);
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

    [Fact]
    public async Task PlanAsync_StableChannel_SkipsANewerDevReleaseOfARequestedMod()
    {
        var stable = Release("A", "1.0.0");
        var dev = Release("A", "1.1.0-dev.1", status: ReleaseStatus.Dev);
        var plan = await PlanInChannelAsync(null, EmptyInstance(), [new RequestedMod(dev, InstallReason.Manual, Exact: false)], [stable, dev]);
        Assert.True(plan.IsReady);
        Assert.Equal(ModVersion.Parse("1.0.0"), Assert.Single(plan.Operations).Release.Version);
        Assert.DoesNotContain(plan.Warnings, value => value.Code == "release-channel");
    }

    [Fact]
    public async Task PlanAsync_StableChannel_UpdatesToANewerStableReleaseAndNotToANewerDevRelease()
    {
        var installed = Installed("A", "1.0.0");
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [installed], false);
        var available = new[] { Release("A", "1.0.0"), Release("A", "1.1.0"), Release("A", "2.0.0-dev.1", status: ReleaseStatus.Dev) };
        var plan = await PlanInChannelAsync(null, instance, [new RequestedMod(installed.Metadata, InstallReason.Manual, Exact: false)], available);
        Assert.True(plan.IsReady);
        Assert.Equal(ModVersion.Parse("1.1.0"), Assert.Single(plan.Operations).Release.Version);
    }

    [Fact]
    public async Task PlanAsync_TestingChannel_OffersATestingReleaseButNotADevRelease()
    {
        var available = new[] { Release("A", "1.0.0"), Release("A", "1.1.0-beta.1", status: ReleaseStatus.Testing), Release("A", "1.2.0-dev.1", status: ReleaseStatus.Dev) };
        var plan = await PlanInChannelAsync(ReleaseChannel.Testing, EmptyInstance(), [new RequestedMod(available[2], InstallReason.Manual, Exact: false)], available);
        Assert.True(plan.IsReady);
        Assert.Equal(ModVersion.Parse("1.1.0-beta.1"), Assert.Single(plan.Operations).Release.Version);
    }

    [Theory]
    [InlineData(ReleaseChannel.Stable, "1.0.0")]
    [InlineData(ReleaseChannel.Testing, "1.1.0-beta.1")]
    [InlineData(ReleaseChannel.Dev, "1.2.0-dev.1")]
    public async Task PlanAsync_Dependency_ResolvesToTheNewestReleaseInTheChannel(ReleaseChannel channel, string expected)
    {
        var a = Release("A", dependencies: [Required("B")]);
        var available = new[] { a, Release("B", "1.0.0"), Release("B", "1.1.0-beta.1", status: ReleaseStatus.Testing), Release("B", "1.2.0-dev.1", status: ReleaseStatus.Dev) };
        var plan = await PlanInChannelAsync(channel, EmptyInstance(), [new RequestedMod(a, InstallReason.Manual)], available);
        Assert.True(plan.IsReady);
        Assert.Equal(ModVersion.Parse(expected), Assert.Single(plan.Selections, value => value.Release.ModId == "B").Release.Version);
        Assert.DoesNotContain(plan.Warnings, value => value.Code == "release-channel");
    }

    [Fact]
    public async Task PlanAsync_ExactDevReleaseOnStable_IsReadyWithAWarning()
    {
        var dev = Release("A", "2.0.0-dev.1", status: ReleaseStatus.Dev);
        var plan = await PlanInChannelAsync(null, EmptyInstance(), [new RequestedMod(dev, InstallReason.Manual)], [Release("A"), dev]);
        Assert.True(plan.IsReady);
        Assert.Equal(ModVersion.Parse("2.0.0-dev.1"), Assert.Single(plan.Operations).Release.Version);
        var warning = Assert.Single(plan.Warnings, value => value.Code == "release-channel");
        Assert.Equal("A", warning.ModId);
        Assert.Contains("dev", warning.Message);
        Assert.Contains("stable channel", warning.Message);
    }

    [Fact]
    public async Task PlanAsync_PackPinWithDevStatus_InstallsWithAWarning()
    {
        var pinned = Release("A", "2.0.0-dev.1", status: ReleaseStatus.Dev);
        var other = Release("B");
        var plan = await PlanInChannelAsync(null, EmptyInstance(), [new RequestedMod(pinned, InstallReason.ModPack), new RequestedMod(other, InstallReason.ModPack)], [Release("A"), pinned, other]);
        Assert.True(plan.IsReady);
        Assert.Contains(plan.Operations, value => value.Release.ModId == "A" && value.Release.Version == ModVersion.Parse("2.0.0-dev.1"));
        var warning = Assert.Single(plan.Warnings, value => value.Code == "release-channel");
        Assert.Equal("A", warning.ModId);
    }

    [Theory]
    [InlineData(ReleaseChannel.Stable, "1.0.0")]
    [InlineData(ReleaseChannel.Testing, "1.0.0")]
    [InlineData(ReleaseChannel.Dev, "2.0.0")]
    public async Task PlanAsync_UnknownStatus_IsACandidateOnlyOnDev(ReleaseChannel channel, string expected)
    {
        var a = Release("A", dependencies: [Required("B")]);
        var available = new[] { a, Release("B", "1.0.0"), Release("B", "2.0.0", status: ReleaseStatus.Unknown) };
        var plan = await PlanInChannelAsync(channel, EmptyInstance(), [new RequestedMod(a, InstallReason.Manual)], available);
        Assert.True(plan.IsReady);
        Assert.Equal(ModVersion.Parse(expected), Assert.Single(plan.Selections, value => value.Release.ModId == "B").Release.Version);
    }

    [Fact]
    public async Task PlanAsync_RequestChannel_OverridesThePlannerChannel()
    {
        var dev = Release("A", "2.0.0-dev.1", status: ReleaseStatus.Dev);
        var repository = new FakeRepository([Release("A"), dev]);
        var planner = new RepositoryInstallPlanner(new ModDependencyResolver(), ReleaseChannel.Stable);
        var plan = await planner.PlanAsync(new InstallPlanningRequest(EmptyInstance(), [new RequestedMod(dev, InstallReason.Manual, Exact: false)], repository, Channel: ReleaseChannel.Dev));
        Assert.True(plan.IsReady);
        Assert.Equal(ModVersion.Parse("2.0.0-dev.1"), Assert.Single(plan.Operations).Release.Version);
        Assert.DoesNotContain(plan.Warnings, value => value.Code == "release-channel");
    }

    [Fact]
    public async Task PlanAsync_InstalledDevReleaseOnStable_StaysWithoutAWarning()
    {
        var installed = Installed("A", "2.0.0-dev.1", status: ReleaseStatus.Dev);
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [installed], false);
        var plan = await PlanInChannelAsync(null, instance, [new RequestedMod(installed.Metadata, InstallReason.Manual, Exact: false)], [Release("A"), installed.Metadata]);
        Assert.True(plan.IsReady);
        Assert.Empty(plan.Operations);
        Assert.DoesNotContain(plan.Warnings, value => value.Code == "release-channel");
    }

    [Fact]
    public async Task PlanAsync_InstalledDevDependencyOnStable_IsKeptWithoutAWarning()
    {
        var installed = Installed("B", "2.0.0-dev.1", status: ReleaseStatus.Dev);
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [installed], false);
        var a = Release("A", dependencies: [Required("B", "2.0.0-dev.1")]);
        var plan = await PlanInChannelAsync(null, instance, [new RequestedMod(a, InstallReason.Manual)], [a, Release("B"), installed.Metadata]);
        Assert.True(plan.IsReady);
        Assert.Equal("A", Assert.Single(plan.Operations).Release.ModId);
        Assert.DoesNotContain(plan.Warnings, value => value.Code == "release-channel");
    }

    [Fact]
    public async Task PlanAsync_NoReleaseInTheChannel_ReportsTheChannel()
    {
        var dev = Release("A", "1.0.0-dev.1", status: ReleaseStatus.Dev);
        var plan = await PlanInChannelAsync(ReleaseChannel.Testing, EmptyInstance(), [new RequestedMod(dev, InstallReason.Manual, Exact: false)], [dev]);
        Assert.False(plan.IsReady);
        Assert.Contains(plan.Conflicts, value => value.Code == "outside-channel" && value.Message.Contains("testing channel"));
        Assert.DoesNotContain(plan.Conflicts, value => value.Code == "missing-request");
        Assert.Empty(plan.Operations);
    }

    [Fact]
    public async Task PlanAsync_InstallOnStable_KeepsAnInstalledDevReleaseOfTheRequestedMod()
    {
        var installed = Installed("A", "2.0.0-dev.1", status: ReleaseStatus.Dev);
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [installed], false);
        var stable = Release("A", "1.0.0");
        var plan = await PlanInChannelAsync(null, instance, [new RequestedMod(stable, InstallReason.Manual, Exact: false)], [stable, installed.Metadata]);
        Assert.True(plan.IsReady);
        Assert.Empty(plan.Operations);
        Assert.Equal(ModVersion.Parse("2.0.0-dev.1"), Assert.Single(plan.Selections).Release.Version);
        Assert.DoesNotContain(plan.Warnings, value => value.Code == "release-channel");
    }

    [Fact]
    public async Task CheckHandover_MissingRequiredDependency_PlansOnlyTheDependency()
    {
        var owner = Foreign("A", [Required("B"), new ModDependency("C", ModDependencyKind.Optional), new ModDependency("D", ModDependencyKind.Recommends)]);
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [owner], false);

        var check = await CheckHandoverAsync(instance, owner, [owner.Metadata, Release("B"), Release("C"), Release("D")]);

        Assert.Equal(["B"], check.Missing.Select(value => value.ModId));
        Assert.Equal(["C", "D"], check.NotInstalled.Select(value => value.ModId));
        Assert.True(check.CanInstallMissing);
        Assert.Equal(["B"], check.Plan!.Operations.Select(value => value.Release.ModId));
        Assert.Equal(InstallReason.Dependency, check.Plan.Operations.Single().Reason);
    }

    [Fact]
    public async Task CheckHandover_DependenciesPresent_PlansNothing()
    {
        var owner = Foreign("A", [Required("B")]);
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [owner, Installed("B")], false);
        var repository = new FakeRepository([owner.Metadata, Release("B")]);

        var check = await HandoverDependencies.Check(instance, owner).PlanAsync(new RepositoryInstallPlanner(new ModDependencyResolver()), instance, owner, repository, null, null);

        Assert.Empty(check.Missing);
        Assert.Null(check.Plan);
        Assert.Equal(0, repository.VersionReads);
    }

    [Fact]
    public async Task CheckHandover_UnavailableRequiredDependency_CannotInstallIt()
    {
        var owner = Foreign("A", [Required("B")]);
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [owner], false);

        var check = await CheckHandoverAsync(instance, owner, [owner.Metadata]);

        Assert.Equal(["B"], check.Missing.Select(value => value.ModId));
        Assert.False(check.CanInstallMissing);
        Assert.Contains(check.Plan!.Conflicts, value => value.Code == "unsatisfied-dependency");
    }

    [Fact]
    public async Task CheckHandover_DependencyRecordedAsForeignBelowTheBound_CannotInstallIt()
    {
        var owner = Foreign("A", [Required("B", min: "2.0.0")]);
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [owner, Foreign("B", [])], false);

        var check = await CheckHandoverAsync(instance, owner, [owner.Metadata, Release("B"), Release("B", "2.0.0")]);

        Assert.Equal(["B"], check.Missing.Select(value => value.ModId));
        Assert.Equal(["B"], check.NotOwned);
        Assert.False(check.CanInstallMissing);
    }

    [Fact]
    public void Constructor_UndefinedChannel_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RepositoryInstallPlanner(new ModDependencyResolver(), (ReleaseChannel)42));
    }

    private static Instance EmptyInstance() => new("Test", InstanceSource.Custom.Value);
    private static Task<HandoverDependencies> CheckHandoverAsync(Instance instance, InstalledMod owner, IReadOnlyList<ModVersionMetadata> available) => HandoverDependencies.Check(instance, owner).PlanAsync(new RepositoryInstallPlanner(new ModDependencyResolver()), instance, owner, new FakeRepository(available), null, null);
    private static InstalledMod Foreign(string id, IReadOnlyList<ModDependency> dependencies) => new(id, ModVersion.Parse("1.0.0"), InstallReason.Manual, DateTimeOffset.UtcNow, Release(id, dependencies: dependencies), ownership: ModInstallOwnership.Foreign);
    private static Task<InstallPlan> PlanInChannelAsync(ReleaseChannel? plannerChannel, Instance instance, IReadOnlyList<RequestedMod> requested, IReadOnlyList<ModVersionMetadata> available) => (plannerChannel is { } channel ? new RepositoryInstallPlanner(new ModDependencyResolver(), channel) : new RepositoryInstallPlanner(new ModDependencyResolver())).PlanAsync(new InstallPlanningRequest(instance, requested, new FakeRepository(available)));
    private static Task<InstallPlan> PlanAsync(IReadOnlyList<ModVersionMetadata> requested, IReadOnlyList<ModVersionMetadata> available) => new RepositoryInstallPlanner(new ModDependencyResolver()).PlanAsync(new InstallPlanningRequest(new Instance("Test", InstanceSource.Custom.Value), requested.Select(value => new RequestedMod(value, InstallReason.Manual)).ToList(), new FakeRepository(available)));
    private static ModDependency Required(string id, string? min = null, string? max = null) => new(id, ModDependencyKind.Required, min is null ? null : ModVersion.Parse(min), max is null ? null : ModVersion.Parse(max));
    private static InstalledMod Installed(string id, string version = "1.0.0", IReadOnlyList<ModDependency>? dependencies = null, ReleaseStatus status = ReleaseStatus.Stable) => new(id, ModVersion.Parse(version), InstallReason.Manual, DateTimeOffset.UtcNow, Release(id, version, dependencies, status: status));
    private static ModVersionMetadata Release(string id, string version = "1.0.0", IReadOnlyList<ModDependency>? dependencies = null, bool yanked = false, int gameMinRevision = 2131, IReadOnlyList<string>? os = null, ReleaseStatus status = ReleaseStatus.Stable) => new(1, id, ModVersion.Parse(version), status, DateTimeOffset.UnixEpoch, gameMinRevision == 2131 ? "2026.7.4.2131" : $"2026.7.4.{gameMinRevision}", gameMinRevision, new DownloadInfo("https://example.com/mod.zip", new string('A', 64), 1, "application/zip"), 1, dependencies ?? [], os: os, yanked: yanked);

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
