using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Tests.Mods;

namespace Borea.Core.Tests.Dependencies;

/// <summary>
/// The dependency kinds of RFC 0031.
/// </summary>
public sealed class DependencyEvaluationTests
{
    private readonly ModDependencyResolver _resolver = new();

    private static Instance NewInstance() => new("Test", InstanceSource.Custom.Value);

    private static ModDependency Entry(string modId, ModDependencyKind kind, string? min = null, string? max = null) =>
        new(modId, kind,
            min is null ? null : ModVersion.Parse(min),
            max is null ? null : ModVersion.Parse(max));

    private DependencyEvaluation Single(Instance instance, ModDependency dependency)
    {
        var candidate = TestFixtures.SampleVersionMetadata("candidate", dependencies: new[] { dependency });
        return Assert.Single(_resolver.Evaluate(instance, candidate));
    }

    #region The four kinds a manager may install

    [Theory]
    [InlineData(ModDependencyKind.Required, DependencyOutcome.Install)]
    [InlineData(ModDependencyKind.Recommends, DependencyOutcome.SelectByDefault)]
    [InlineData(ModDependencyKind.Suggests, DependencyOutcome.Offer)]
    [InlineData(ModDependencyKind.Optional, DependencyOutcome.Offer)]
    public void Missing_ReportsTheOutcomeOfItsKind(ModDependencyKind kind, DependencyOutcome expected)
    {
        var evaluation = Single(NewInstance(), Entry("missing-mod", kind, "1.0.0"));

        Assert.Equal(expected, evaluation.Outcome);
        Assert.Null(evaluation.InstalledModId);
        Assert.Null(evaluation.DeclaredBy);
    }

    [Theory]
    [InlineData(ModDependencyKind.Required)]
    [InlineData(ModDependencyKind.Recommends)]
    [InlineData(ModDependencyKind.Suggests)]
    [InlineData(ModDependencyKind.Optional)]
    public void InstalledWithinBounds_IsSatisfied(ModDependencyKind kind)
    {
        var instance = NewInstance();
        instance.AddMod(TestFixtures.SampleInstalledMod("lib", "1.5.0"));

        var evaluation = Single(instance, Entry("lib", kind, "1.0.0", "2.0.0"));

        Assert.Equal(DependencyOutcome.Satisfied, evaluation.Outcome);
        Assert.Equal("lib", evaluation.InstalledModId);
    }

    [Theory]
    [InlineData("0.9.0")]
    [InlineData("2.0.1")]
    public void InstalledOutsideBounds_CountsAsMissing(string installedVersion)
    {
        var instance = NewInstance();
        instance.AddMod(TestFixtures.SampleInstalledMod("lib", installedVersion));

        var evaluation = Single(instance, Entry("lib", ModDependencyKind.Required, "1.0.0", "2.0.0"));

        Assert.Equal(DependencyOutcome.Install, evaluation.Outcome);
        Assert.Null(evaluation.InstalledModId);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("2.0.0")]
    public void BoundsAreInclusive(string installedVersion)
    {
        var instance = NewInstance();
        instance.AddMod(TestFixtures.SampleInstalledMod("lib", installedVersion));

        var evaluation = Single(instance, Entry("lib", ModDependencyKind.Required, "1.0.0", "2.0.0"));

        Assert.Equal(DependencyOutcome.Satisfied, evaluation.Outcome);
    }

    [Fact]
    public void AnAbsentMaximumLeavesTheUpperEndOpen()
    {
        var instance = NewInstance();
        instance.AddMod(TestFixtures.SampleInstalledMod("lib", "99.0.0"));

        var evaluation = Single(instance, Entry("lib", ModDependencyKind.Required, "1.0.0"));

        Assert.Equal(DependencyOutcome.Satisfied, evaluation.Outcome);
    }

    #endregion

    #region Conflicts

    [Fact]
    public void Conflict_NothingInstalled_IsSatisfied()
    {
        var evaluation = Single(NewInstance(), Entry("rival", ModDependencyKind.Conflict, "1.0.0"));

        Assert.Equal(DependencyOutcome.Satisfied, evaluation.Outcome);
        Assert.Null(evaluation.InstalledModId);
    }

    [Fact]
    public void Conflict_InstalledWithinTheConflictingRange_Conflicts()
    {
        var instance = NewInstance();
        instance.AddMod(TestFixtures.SampleInstalledMod("rival", "1.5.0"));

        var evaluation = Single(instance, Entry("rival", ModDependencyKind.Conflict, "1.0.0", "2.0.0"));

        Assert.Equal(DependencyOutcome.Conflict, evaluation.Outcome);
        Assert.Equal("rival", evaluation.InstalledModId);
        Assert.Null(evaluation.DeclaredBy);
    }

    [Theory]
    [InlineData("0.9.0")]
    [InlineData("2.0.1")]
    public void Conflict_InstalledOutsideTheConflictingRange_IsSatisfied(string installedVersion)
    {
        var instance = NewInstance();
        instance.AddMod(TestFixtures.SampleInstalledMod("rival", installedVersion));

        var evaluation = Single(instance, Entry("rival", ModDependencyKind.Conflict, "1.0.0", "2.0.0"));

        Assert.Equal(DependencyOutcome.Satisfied, evaluation.Outcome);
    }

    [Theory]
    [InlineData("0.0.1")]
    [InlineData("1.5.0")]
    [InlineData("99.0.0")]
    public void Conflict_WithoutBounds_ConflictsWithEveryVersion(string installedVersion)
    {
        var instance = NewInstance();
        instance.AddMod(TestFixtures.SampleInstalledMod("rival", installedVersion));

        var evaluation = Single(instance, Entry("rival", ModDependencyKind.Conflict));

        Assert.Equal(DependencyOutcome.Conflict, evaluation.Outcome);
    }

    [Fact]
    public void Conflict_DeclaredByAnInstalledMod_IsReportedAgainstTheCandidate()
    {
        var instance = NewInstance();
        instance.AddMod(TestFixtures.SampleInstalledMod(
            "incumbent",
            dependencies: new[] { Entry("candidate", ModDependencyKind.Conflict, "1.0.0") }));
        var candidate = TestFixtures.SampleVersionMetadata("candidate", "1.2.0");

        var evaluation = Assert.Single(_resolver.Evaluate(instance, candidate));

        Assert.Equal(DependencyOutcome.Conflict, evaluation.Outcome);
        Assert.Equal("incumbent", evaluation.DeclaredBy);
        Assert.Equal("incumbent", evaluation.InstalledModId);
    }

    [Fact]
    public void Conflict_DeclaredByAnInstalledModAgainstAnotherVersion_IsNotReported()
    {
        var instance = NewInstance();
        instance.AddMod(TestFixtures.SampleInstalledMod(
            "incumbent",
            dependencies: new[] { Entry("candidate", ModDependencyKind.Conflict, "1.0.0", "1.1.0") }));
        var candidate = TestFixtures.SampleVersionMetadata("candidate", "1.2.0");

        Assert.Empty(_resolver.Evaluate(instance, candidate));
    }

    [Fact]
    public void Conflict_DeclaredByTheCopyTheCandidateReplaces_IsNotReported()
    {
        var instance = NewInstance();
        instance.AddMod(TestFixtures.SampleInstalledMod(
            "candidate",
            "1.0.0",
            dependencies: new[] { Entry("candidate", ModDependencyKind.Conflict) }));
        var candidate = TestFixtures.SampleVersionMetadata("candidate", "1.2.0");

        Assert.Empty(_resolver.Evaluate(instance, candidate));
    }

    [Fact]
    public void Conflict_DeclaredByAnInstalledMod_ComparesIdsCaseInsensitively()
    {
        var instance = NewInstance();
        instance.AddMod(TestFixtures.SampleInstalledMod(
            "incumbent",
            dependencies: new[] { Entry("Candidate", ModDependencyKind.Conflict) }));
        var candidate = TestFixtures.SampleVersionMetadata("candidate", "1.2.0");

        Assert.Equal(DependencyOutcome.Conflict, Assert.Single(_resolver.Evaluate(instance, candidate)).Outcome);
    }

    [Fact]
    public void Conflict_DeclaredByBothSides_IsReportedOncePerDeclaringSide()
    {
        var instance = NewInstance();
        instance.AddMod(TestFixtures.SampleInstalledMod(
            "rival",
            dependencies: new[] { Entry("candidate", ModDependencyKind.Conflict) }));
        var candidate = TestFixtures.SampleVersionMetadata("candidate", dependencies: new[]
        {
            Entry("rival", ModDependencyKind.Conflict),
        });

        var evaluations = _resolver.Evaluate(instance, candidate);

        Assert.Equal(2, evaluations.Count);
        Assert.All(evaluations, e => Assert.Equal(DependencyOutcome.Conflict, e.Outcome));
        // Both name the same installed mod, so grouping collapses them.
        Assert.Single(evaluations.Select(e => e.InstalledModId).Distinct());
        Assert.Equal(new string?[] { null, "rival" }, evaluations.Select(e => e.DeclaredBy));
    }

    [Fact]
    public void Conflict_NonConflictEntriesOfInstalledModsAreNotReadBackwards()
    {
        var instance = NewInstance();
        instance.AddMod(TestFixtures.SampleInstalledMod(
            "incumbent",
            dependencies: new[] { Entry("candidate", ModDependencyKind.Required, "1.0.0") }));
        var candidate = TestFixtures.SampleVersionMetadata("candidate", "1.2.0");

        Assert.Empty(_resolver.Evaluate(instance, candidate));
    }

    #endregion

    #region any_of

    private static ModDependency AnyOf(ModDependencyKind kind) =>
        ModDependency.OfAlternatives(kind, new[]
        {
            new ModDependencyAlternative("lib-a", ModVersion.Parse("2.0.0")),
            new ModDependencyAlternative("lib-b", ModVersion.Parse("1.1.0")),
        });

    [Fact]
    public void AnyOf_OneAlternativeInstalled_IsSatisfiedAndNamesIt()
    {
        var instance = NewInstance();
        instance.AddMod(TestFixtures.SampleInstalledMod("lib-b", "1.2.0"));

        var evaluation = Single(instance, AnyOf(ModDependencyKind.Required));

        Assert.Equal(DependencyOutcome.Satisfied, evaluation.Outcome);
        Assert.Equal("lib-b", evaluation.InstalledModId);
    }

    [Theory]
    [InlineData(ModDependencyKind.Required, DependencyOutcome.Install)]
    [InlineData(ModDependencyKind.Recommends, DependencyOutcome.SelectByDefault)]
    public void AnyOf_NoAlternativeInstalled_ReportsTheOutcomeOfItsKind(ModDependencyKind kind, DependencyOutcome expected)
    {
        var evaluation = Single(NewInstance(), AnyOf(kind));

        Assert.Equal(expected, evaluation.Outcome);
        Assert.Null(evaluation.InstalledModId);
    }

    [Fact]
    public void AnyOf_AlternativeInstalledBelowItsOwnMinimum_CountsAsMissing()
    {
        var instance = NewInstance();
        instance.AddMod(TestFixtures.SampleInstalledMod("lib-b", "1.0.0"));

        var evaluation = Single(instance, AnyOf(ModDependencyKind.Required));

        Assert.Equal(DependencyOutcome.Install, evaluation.Outcome);
    }

    #endregion

    #region A kind this build does not know

    [Fact]
    public void UnknownKind_IsReportedAndNothingIsClaimed()
    {
        var evaluation = Single(NewInstance(), Entry("mystery", ModDependencyKind.Unknown));

        Assert.Equal(DependencyOutcome.Unknown, evaluation.Outcome);
    }

    [Fact]
    public void UnknownKind_StaysUnknownEvenWhenItsIdIsInstalled()
    {
        var instance = NewInstance();
        instance.AddMod(TestFixtures.SampleInstalledMod("mystery", "1.5.0"));

        var evaluation = Single(instance, Entry("mystery", ModDependencyKind.Unknown, "1.0.0"));

        Assert.Equal(DependencyOutcome.Unknown, evaluation.Outcome);
        Assert.Null(evaluation.InstalledModId);
    }

    [Fact]
    public void UnknownKind_DeclaredByAnInstalledMod_IsReportedRatherThanDropped()
    {
        var instance = NewInstance();
        instance.AddMod(TestFixtures.SampleInstalledMod(
            "incumbent",
            dependencies: new[] { Entry("candidate", ModDependencyKind.Unknown, "1.0.0") }));
        var candidate = TestFixtures.SampleVersionMetadata("candidate", "1.2.0");

        var evaluation = Assert.Single(_resolver.Evaluate(instance, candidate));

        Assert.Equal(DependencyOutcome.Unknown, evaluation.Outcome);
        Assert.Equal("incumbent", evaluation.DeclaredBy);
        Assert.Null(evaluation.InstalledModId);
    }

    [Fact]
    public void UnknownKind_DeclaredByAnInstalledMod_IsReportedWhateverItsBoundsSay()
    {
        var instance = NewInstance();
        instance.AddMod(TestFixtures.SampleInstalledMod(
            "incumbent",
            dependencies: new[] { Entry("candidate", ModDependencyKind.Unknown, "9.0.0") }));
        var candidate = TestFixtures.SampleVersionMetadata("candidate", "1.2.0");

        // Bounds of an unreadable kind say nothing, so they go unread.
        Assert.Equal(DependencyOutcome.Unknown, Assert.Single(_resolver.Evaluate(instance, candidate)).Outcome);
    }

    #endregion

    #region The list as a whole

    [Fact]
    public void Evaluate_NoDependencies_ReturnsEmpty()
    {
        Assert.Empty(_resolver.Evaluate(NewInstance(), TestFixtures.SampleVersionMetadata("candidate")));
    }

    [Fact]
    public void Evaluate_KeepsDocumentOrderAndPutsIncomingConflictsLast()
    {
        var instance = NewInstance();
        instance.AddMod(TestFixtures.SampleInstalledMod(
            "incumbent",
            dependencies: new[] { Entry("candidate", ModDependencyKind.Conflict) }));
        var candidate = TestFixtures.SampleVersionMetadata("candidate", dependencies: new[]
        {
            Entry("first", ModDependencyKind.Required, "1.0.0"),
            Entry("second", ModDependencyKind.Suggests),
        });

        var evaluations = _resolver.Evaluate(instance, candidate);

        Assert.Equal(3, evaluations.Count);
        Assert.Equal("first", evaluations[0].Dependency.ModId);
        Assert.Equal("second", evaluations[1].Dependency.ModId);
        Assert.Equal("incumbent", evaluations[2].DeclaredBy);
    }

    [Fact]
    public void Evaluate_ComparesIdsCaseInsensitively()
    {
        var instance = NewInstance();
        instance.AddMod(TestFixtures.SampleInstalledMod("Lib", "1.5.0"));

        var evaluation = Single(instance, Entry("lib", ModDependencyKind.Required, "1.0.0"));

        Assert.Equal(DependencyOutcome.Satisfied, evaluation.Outcome);
        Assert.Equal("Lib", evaluation.InstalledModId);
    }

    [Fact]
    public void Evaluate_NullArguments_Throw()
    {
        var instance = NewInstance();
        var candidate = TestFixtures.SampleVersionMetadata();

        Assert.Throws<ArgumentNullException>(() => _resolver.Evaluate(null!, candidate));
        Assert.Throws<ArgumentNullException>(() => _resolver.Evaluate(instance, null!));
    }

    [Fact]
    public void Evaluation_WithoutADependency_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DependencyEvaluation(null!, DependencyOutcome.Satisfied));
    }

    #endregion
}
