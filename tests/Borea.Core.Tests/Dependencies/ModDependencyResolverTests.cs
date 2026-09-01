using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Tests.Mods;

namespace Borea.Core.Tests.Dependencies;

public sealed class ModDependencyResolverTests
{
    private readonly ModDependencyResolver _resolver = new();

    private static ModDependency Required(string modId, string? min = null, string? max = null) =>
        new(modId, ModDependencyKind.Required,
            min is null ? null : ModVersion.Parse(min),
            max is null ? null : ModVersion.Parse(max));

    [Fact]
    public void GetUnsatisfiedDependencies_NoDependencies_ReturnsEmpty()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        var candidate = TestFixtures.SampleVersionMetadata("candidate");

        Assert.Empty(_resolver.GetUnsatisfiedDependencies(instance, candidate));
    }

    [Fact]
    public void GetUnsatisfiedDependencies_RequiredDependencyMissing_IsReported()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        var candidate = TestFixtures.SampleVersionMetadata("candidate", dependencies: new[] { Required("missing-mod", "1.0.0") });

        var result = _resolver.GetUnsatisfiedDependencies(instance, candidate);

        Assert.Single(result);
        Assert.Equal("missing-mod", result[0].ModId);
    }

    [Fact]
    public void GetUnsatisfiedDependencies_RequiredDependencySatisfied_IsNotReported()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod("required-mod", "1.5.0"));
        var candidate = TestFixtures.SampleVersionMetadata("candidate", dependencies: new[] { Required("required-mod", "1.0.0") });

        Assert.Empty(_resolver.GetUnsatisfiedDependencies(instance, candidate));
    }

    [Fact]
    public void GetUnsatisfiedDependencies_RequiredDependencyBelowMinimum_IsReported()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod("required-mod", "0.5.0"));
        var candidate = TestFixtures.SampleVersionMetadata("candidate", dependencies: new[] { Required("required-mod", "1.0.0") });

        Assert.Single(_resolver.GetUnsatisfiedDependencies(instance, candidate));
    }

    [Fact]
    public void GetUnsatisfiedDependencies_IdsCompareCaseInsensitively()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod("Required-Mod", "1.5.0"));
        var candidate = TestFixtures.SampleVersionMetadata("candidate", dependencies: new[] { Required("required-mod", "1.0.0") });

        Assert.Empty(_resolver.GetUnsatisfiedDependencies(instance, candidate));
    }

    [Theory]
    [InlineData(ModDependencyKind.Optional)]
    [InlineData(ModDependencyKind.Recommends)]
    [InlineData(ModDependencyKind.Suggests)]
    public void GetUnsatisfiedDependencies_NonRequiredKindsMissing_AreNotReported(ModDependencyKind kind)
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        var dependency = new ModDependency("missing-mod", kind, ModVersion.Parse("1.0.0"));
        var candidate = TestFixtures.SampleVersionMetadata("candidate", dependencies: new[] { dependency });

        Assert.Empty(_resolver.GetUnsatisfiedDependencies(instance, candidate));
    }

    [Fact]
    public void GetUnsatisfiedDependencies_AnyOfWithOneAlternativeInstalled_IsSatisfied()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod("lib-b", "1.2.0"));
        var dependency = ModDependency.OfAlternatives(ModDependencyKind.Required, new[]
        {
            new ModDependencyAlternative("lib-a", ModVersion.Parse("2.0.0")),
            new ModDependencyAlternative("lib-b", ModVersion.Parse("1.1.0")),
        });
        var candidate = TestFixtures.SampleVersionMetadata("candidate", dependencies: new[] { dependency });

        Assert.Empty(_resolver.GetUnsatisfiedDependencies(instance, candidate));
    }

    [Fact]
    public void GetUnsatisfiedDependencies_AnyOfWithNoAlternativeInstalled_IsReported()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        var dependency = ModDependency.OfAlternatives(ModDependencyKind.Required, new[]
        {
            new ModDependencyAlternative("lib-a", ModVersion.Parse("2.0.0")),
            new ModDependencyAlternative("lib-b", ModVersion.Parse("1.1.0")),
        });
        var candidate = TestFixtures.SampleVersionMetadata("candidate", dependencies: new[] { dependency });

        Assert.Single(_resolver.GetUnsatisfiedDependencies(instance, candidate));
    }

    [Fact]
    public void GetUnsatisfiedDependencies_ConflictTheCandidateDeclares_IsNotReported()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod("rival", "1.5.0"));
        var dependency = new ModDependency("rival", ModDependencyKind.Conflict);
        var candidate = TestFixtures.SampleVersionMetadata("candidate", dependencies: new[] { dependency });

        Assert.Empty(_resolver.GetUnsatisfiedDependencies(instance, candidate));
    }

    [Fact]
    public void GetUnsatisfiedDependencies_NullArguments_Throw()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        var candidate = TestFixtures.SampleVersionMetadata();

        Assert.Throws<ArgumentNullException>(() => _resolver.GetUnsatisfiedDependencies(null!, candidate));
        Assert.Throws<ArgumentNullException>(() => _resolver.GetUnsatisfiedDependencies(instance, null!));
    }

    [Fact]
    public void CheckUninstall_NoDependents_CanUninstall()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod("standalone-mod"));

        var check = _resolver.CheckUninstall(instance, "standalone-mod", ModVersion.Parse("1.0.0"), isActive: true);

        Assert.True(check.CanUninstall);
        Assert.Empty(check.DependentModIds);
    }

    [Fact]
    public void CheckUninstall_RequiredByAnotherMod_IsReportedAsDependent()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod("library-mod"));
        instance.AddMod(TestFixtures.SampleInstalledMod("dependent-mod", dependencies: new[] { new ModDependency("library-mod", ModDependencyKind.Required, ModVersion.Parse("1.0.0")) }));

        var check = _resolver.CheckUninstall(instance, "library-mod", ModVersion.Parse("1.0.0"), isActive: true);

        Assert.False(check.CanUninstall);
        Assert.Contains("dependent-mod", check.DependentModIds);
    }

    [Fact]
    public void CheckUninstall_DependentIdsCompareCaseInsensitively()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod("Library-Mod"));
        instance.AddMod(TestFixtures.SampleInstalledMod("dependent-mod", dependencies: new[] { new ModDependency("Library-Mod", ModDependencyKind.Required, ModVersion.Parse("1.0.0")) }));

        var check = _resolver.CheckUninstall(instance, "library-mod", ModVersion.Parse("1.0.0"), isActive: true);

        Assert.False(check.CanUninstall);
    }

    [Theory]
    [InlineData(ModDependencyKind.Optional)]
    [InlineData(ModDependencyKind.Recommends)]
    [InlineData(ModDependencyKind.Suggests)]
    public void CheckUninstall_OnlyNonRequiredDependents_CanUninstall(ModDependencyKind kind)
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod("library-mod"));
        var dependency = new ModDependency("library-mod", kind, ModVersion.Parse("1.0.0"));
        instance.AddMod(TestFixtures.SampleInstalledMod("dependent-mod", dependencies: new[] { dependency }));

        var check = _resolver.CheckUninstall(instance, "library-mod", ModVersion.Parse("1.0.0"), isActive: true);

        Assert.True(check.CanUninstall);
    }

    [Fact]
    public void CheckUninstall_RemovedVersionNeverSatisfiedTheDependent_CanUninstall()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod("library-mod", "1.0.0"));
        var dependency = new ModDependency("library-mod", ModDependencyKind.Required, ModVersion.Parse("2.0.0"));
        instance.AddMod(TestFixtures.SampleInstalledMod("dependent-mod", dependencies: new[] { dependency }));

        var check = _resolver.CheckUninstall(instance, "library-mod", ModVersion.Parse("1.0.0"), isActive: true);

        Assert.True(check.CanUninstall);
    }

    [Fact]
    public void CheckUninstall_LastInstalledAlternativeOfAnyOf_IsReportedAsDependent()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod("lib-a", "2.0.0"));
        var dependency = ModDependency.OfAlternatives(ModDependencyKind.Required, new[]
        {
            new ModDependencyAlternative("lib-a", ModVersion.Parse("2.0.0")),
            new ModDependencyAlternative("lib-b", ModVersion.Parse("1.1.0")),
        });
        instance.AddMod(TestFixtures.SampleInstalledMod("dependent-mod", dependencies: new[] { dependency }));

        var check = _resolver.CheckUninstall(instance, "lib-a", ModVersion.Parse("2.0.0"), isActive: true);

        Assert.False(check.CanUninstall);
    }

    [Fact]
    public void CheckUninstall_AnotherAlternativeStillSatisfies_CanUninstall()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod("lib-a", "2.0.0"));
        instance.AddMod(TestFixtures.SampleInstalledMod("lib-b", "1.2.0"));
        var dependency = ModDependency.OfAlternatives(ModDependencyKind.Required, new[]
        {
            new ModDependencyAlternative("lib-a", ModVersion.Parse("2.0.0")),
            new ModDependencyAlternative("lib-b", ModVersion.Parse("1.1.0")),
        });
        instance.AddMod(TestFixtures.SampleInstalledMod("dependent-mod", dependencies: new[] { dependency }));

        var check = _resolver.CheckUninstall(instance, "lib-a", ModVersion.Parse("2.0.0"), isActive: true);

        Assert.True(check.CanUninstall);
    }

    [Fact]
    public void CheckUninstall_PropagatesInstanceIdAndIsActive()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod("standalone-mod"));

        var check = _resolver.CheckUninstall(instance, "standalone-mod", ModVersion.Parse("1.0.0"), isActive: true);

        Assert.Equal(instance.InstanceId, check.InstanceId);
        Assert.True(check.IsActive);
    }
}
