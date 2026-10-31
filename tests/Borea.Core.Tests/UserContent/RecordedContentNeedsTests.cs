using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.Tests.Mods;
using Borea.Core.UserContent;
using static Borea.Core.Tests.UserContent.RecordedContentFixtures;

namespace Borea.Core.Tests.UserContent;

public sealed class RecordedContentNeedsTests
{
    private readonly ModDependencyResolver _resolver = new();

    [Fact]
    public void CheckUninstall_ModARecordedVehicleRequires_NamesTheVehicleAndStillAllowsTheUninstall()
    {
        var instance = InstanceWith(("parts", "1.0.0"));
        var rover = Record(ContentType.Vehicle, "rover", "Rover", Required("parts", "1.0.0"));

        var check = _resolver.CheckUninstall(instance, "parts", ModVersion.Parse("1.0.0"), isActive: true, [rover]);

        Assert.Same(rover, Assert.Single(check.DependentContent));
        Assert.True(check.CanUninstall);
    }

    [Fact]
    public void CheckUninstall_WithoutRecords_NamesNoContent()
    {
        var check = _resolver.CheckUninstall(InstanceWith(("parts", "1.0.0")), "parts", ModVersion.Parse("1.0.0"), isActive: true);

        Assert.Empty(check.DependentContent);
    }

    [Fact]
    public void CheckUninstall_AnyOfWithAnotherInstalledAlternative_NamesNoContent()
    {
        var instance = InstanceWith(("parts", "1.0.0"), ("other-parts", "1.0.0"));
        var dependency = ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("parts"), new ModDependencyAlternative("other-parts")]);
        var save = Record(ContentType.Save, "career", "Career", dependency);

        var check = _resolver.CheckUninstall(instance, "parts", ModVersion.Parse("1.0.0"), isActive: true, [save]);

        Assert.Empty(check.DependentContent);
    }

    [Fact]
    public void CheckUninstall_AnyOfWhoseOtherAlternativeIsDisabled_NamesTheSave()
    {
        var instance = InstanceWith(("parts", "1.0.0"), ("other-parts", "1.0.0"));
        var dependency = ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("parts"), new ModDependencyAlternative("other-parts")]);
        var save = Record(ContentType.Save, "career", "Career", dependency);

        var check = _resolver.CheckUninstall(instance, "parts", ModVersion.Parse("1.0.0"), isActive: true, [save], inactive: ["other-parts"]);

        Assert.Same(save, Assert.Single(check.DependentContent));
    }

    [Fact]
    public void Needing_ModThatIsDisabledAlready_NamesNothing()
    {
        var instance = InstanceWith(("parts", "1.0.0"));
        var rover = Record(ContentType.Vehicle, "rover", "Rover", Required("parts"));

        Assert.Empty(RecordedContentNeeds.Needing([rover], instance, "parts", inactive: ["parts"]));
    }

    [Fact]
    public void CheckUninstall_LastAlternativeOfAnyOf_NamesTheSave()
    {
        var instance = InstanceWith(("parts", "1.0.0"));
        var dependency = ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("parts"), new ModDependencyAlternative("other-parts")]);
        var save = Record(ContentType.Save, "career", "Career", dependency);

        var check = _resolver.CheckUninstall(instance, "parts", ModVersion.Parse("1.0.0"), isActive: true, [save]);

        Assert.Same(save, Assert.Single(check.DependentContent));
    }

    [Fact]
    public void CheckUninstall_RecommendedMod_NamesNoContent()
    {
        var rover = Record(ContentType.Vehicle, "rover", "Rover", new ModDependency("parts", ModDependencyKind.Recommends));

        var check = _resolver.CheckUninstall(InstanceWith(("parts", "1.0.0")), "parts", ModVersion.Parse("1.0.0"), isActive: true, [rover]);

        Assert.Empty(check.DependentContent);
    }

    [Fact]
    public void CheckUninstall_ForeignModWithoutBounds_NamesTheVehicle()
    {
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UnixEpoch, [], [new ForeignMod("parts")], false);
        var rover = Record(ContentType.Vehicle, "rover", "Rover", Required("parts"));

        var check = _resolver.CheckUninstall(instance, "parts", ModVersion.Parse("0.0.0"), isActive: true, [rover]);

        Assert.Same(rover, Assert.Single(check.DependentContent));
    }

    [Fact]
    public void Broken_UpdatePastTheMax_NamesTheSaveAndTheEntry()
    {
        var instance = InstanceWith(("parts", "1.0.0"));
        var entry = Required("parts", "1.0.0", "1.9.0");
        var save = Record(ContentType.Save, "career", "Career", entry);
        var view = RecordedContentNeeds.InstanceView(instance);

        var broken = RecordedContentNeeds.Broken([save], view, RecordedContentNeeds.With(view, [Update("parts", "2.0.0")]));

        Assert.Equal(new BrokenContent(save, entry), Assert.Single(broken));
    }

    [Fact]
    public void Broken_UpdateInsideTheBounds_NamesNothing()
    {
        var view = RecordedContentNeeds.InstanceView(InstanceWith(("parts", "1.0.0")));
        var save = Record(ContentType.Save, "career", "Career", Required("parts", "1.0.0", "1.9.0"));

        Assert.Empty(RecordedContentNeeds.Broken([save], view, RecordedContentNeeds.With(view, [Update("parts", "1.5.0")])));
    }

    [Fact]
    public void Broken_EntryNotMetBefore_NamesNothing()
    {
        var view = RecordedContentNeeds.InstanceView(InstanceWith(("parts", "0.5.0")));
        var save = Record(ContentType.Save, "career", "Career", Required("parts", "1.0.0", "1.9.0"));

        Assert.Empty(RecordedContentNeeds.Broken([save], view, RecordedContentNeeds.With(view, [Update("parts", "2.0.0")])));
    }

    [Fact]
    public void Broken_UpdateBelowTheMin_NamesTheVehicle()
    {
        var view = RecordedContentNeeds.InstanceView(InstanceWith(("parts", "1.5.0")));
        var rover = Record(ContentType.Vehicle, "rover", "Rover", Required("parts", "1.2.0"));

        Assert.Single(RecordedContentNeeds.Broken([rover], view, RecordedContentNeeds.With(view, [Update("parts", "1.0.0")])));
    }

    private static Instance InstanceWith(params (string Id, string Version)[] mods)
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        foreach (var (id, version) in mods)
            instance.AddMod(TestFixtures.SampleInstalledMod(id, version));
        return instance;
    }

    private static PlannedInstall Update(string id, string version) =>
        new(TestFixtures.SampleVersionMetadata(id, version), InstallReason.Manual, GameCompatibility.Compatible, null);
}
