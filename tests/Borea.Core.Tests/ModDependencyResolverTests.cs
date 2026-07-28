using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.Core.Tests;

public sealed class ModDependencyResolverTests
{
    private readonly ModDependencyResolver _resolver = new();

    [Fact]
    public void GetUnsatisfiedDependencies_NoDependencies_ReturnsEmpty()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        var candidate = TestFixtures.SampleModMetadata("candidate");

        Assert.Empty(_resolver.GetUnsatisfiedDependencies(instance, candidate));
    }

    [Fact]
    public void GetUnsatisfiedDependencies_RequiredDependencyMissing_IsReported()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        var dependency = new ModDependency("missing-mod", VersionRange.Parse(">=1.0.0"));
        var candidate = TestFixtures.SampleModMetadata("candidate", dependencies: new[] { dependency });

        var result = _resolver.GetUnsatisfiedDependencies(instance, candidate);

        Assert.Single(result);
        Assert.Equal("missing-mod", result[0].ModId);
    }

    [Fact]
    public void GetUnsatisfiedDependencies_RequiredDependencySatisfied_IsNotReported()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod("required-mod", "1.5.0"));
        var dependency = new ModDependency("required-mod", VersionRange.Parse(">=1.0.0"));
        var candidate = TestFixtures.SampleModMetadata("candidate", dependencies: new[] { dependency });

        Assert.Empty(_resolver.GetUnsatisfiedDependencies(instance, candidate));
    }

    [Fact]
    public void GetUnsatisfiedDependencies_RequiredDependencyWrongVersion_IsReported()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod("required-mod", "0.5.0"));
        var dependency = new ModDependency("required-mod", VersionRange.Parse(">=1.0.0"));
        var candidate = TestFixtures.SampleModMetadata("candidate", dependencies: new[] { dependency });

        Assert.Single(_resolver.GetUnsatisfiedDependencies(instance, candidate));
    }

    [Fact]
    public void GetUnsatisfiedDependencies_OptionalDependencyMissing_IsNotReported()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        var dependency = new ModDependency("missing-optional", VersionRange.Parse(">=1.0.0"), isOptional: true);
        var candidate = TestFixtures.SampleModMetadata("candidate", dependencies: new[] { dependency });

        Assert.Empty(_resolver.GetUnsatisfiedDependencies(instance, candidate));
    }

    [Fact]
    public void GetUnsatisfiedDependencies_NullArguments_Throw()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        var candidate = TestFixtures.SampleModMetadata();

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

        var dependency = new ModDependency("library-mod", VersionRange.Parse(">=1.0.0"));
        var dependentMetadata = TestFixtures.SampleModMetadata("dependent-mod", dependencies: new[] { dependency });
        instance.AddMod(new InstalledMod("dependent-mod", ModVersion.Parse("1.0.0"), InstallReason.Manual, DateTimeOffset.UtcNow, dependentMetadata));

        var check = _resolver.CheckUninstall(instance, "library-mod", ModVersion.Parse("1.0.0"), isActive: true);

        Assert.False(check.CanUninstall);
        Assert.Contains("dependent-mod", check.DependentModIds);
    }

    [Fact]
    public void CheckUninstall_OnlyOptionalDependents_CanUninstall()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod("library-mod"));

        var dependency = new ModDependency("library-mod", VersionRange.Parse(">=1.0.0"), isOptional: true);
        var dependentMetadata = TestFixtures.SampleModMetadata("dependent-mod", dependencies: new[] { dependency });
        instance.AddMod(new InstalledMod("dependent-mod", ModVersion.Parse("1.0.0"), InstallReason.Manual, DateTimeOffset.UtcNow, dependentMetadata));

        var check = _resolver.CheckUninstall(instance, "library-mod", ModVersion.Parse("1.0.0"), isActive: true);

        Assert.True(check.CanUninstall); // Confirms the optional-dependent exclusion decision above.
    }

    [Fact]
    public void CheckUninstall_OtherModRequiresIncompatibleVersion_IsNotReportedAsDependent()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod("library-mod", "1.0.0"));

        // dependent-mod requires >=2.0.0 — the installed 1.0.0 never satisfied it,
        // so removing 1.0.0 doesn't newly break anything.
        var dependency = new ModDependency("library-mod", VersionRange.Parse(">=2.0.0"));
        var dependentMetadata = TestFixtures.SampleModMetadata("dependent-mod", dependencies: new[] { dependency });
        instance.AddMod(new InstalledMod("dependent-mod", ModVersion.Parse("1.0.0"), InstallReason.Manual, DateTimeOffset.UtcNow, dependentMetadata));

        var check = _resolver.CheckUninstall(instance, "library-mod", ModVersion.Parse("1.0.0"), isActive: true);

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