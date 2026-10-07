using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.Core.Tests.Instances;

public sealed class InstanceSourceTests
{
    [Fact]
    public void Custom_Value_IsConsistentAcrossAccesses()
    {
        Assert.Same(InstanceSource.Custom.Value, InstanceSource.Custom.Value);
    }

    [Fact]
    public void FromModPack_SameIdAndVersion_AreEqual()
    {
        var a = new InstanceSource.FromModPack("pack-a", ModVersion.Parse("1.0.0"));
        var b = new InstanceSource.FromModPack("pack-a", ModVersion.Parse("1.0.0"));

        Assert.Equal(a, b);
    }

    [Fact]
    public void FromModPack_DifferentVersion_AreNotEqual()
    {
        // This is the exact distinction "update in place" vs "new instance"
        // depends on — worth pinning down explicitly, not just incidentally
        // covered by other tests.
        var a = new InstanceSource.FromModPack("pack-a", ModVersion.Parse("1.0.0"));
        var b = new InstanceSource.FromModPack("pack-a", ModVersion.Parse("2.0.0"));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void FromModPack_DifferentModPackId_AreNotEqual()
    {
        var a = new InstanceSource.FromModPack("pack-a", ModVersion.Parse("1.0.0"));
        var b = new InstanceSource.FromModPack("pack-b", ModVersion.Parse("1.0.0"));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void FromModPack_AndCustom_AreNeverEqual()
    {
        InstanceSource fromModPack = new InstanceSource.FromModPack("pack-a", ModVersion.Parse("1.0.0"));
        InstanceSource custom = InstanceSource.Custom.Value;

        Assert.NotEqual(fromModPack, custom);
    }

    [Fact]
    public void FromModPack_SameDetachedModsInOtherSetsAndLetterCase_AreEqualWithTheSameHashCode()
    {
        var a = new InstanceSource.FromModPack("pack-a", ModVersion.Parse("1.0.0")).WithDetached(["KSArmory", "MeasureTools"]);
        var b = new InstanceSource.FromModPack("pack-a", ModVersion.Parse("1.0.0")).WithDetached(["measuretools", "ksarmory"]);

        Assert.NotSame(a.Detached, b.Detached);
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void FromModPack_DifferentDetachedMods_AreNotEqual()
    {
        var none = new InstanceSource.FromModPack("pack-a", ModVersion.Parse("1.0.0"));
        var one = none.WithDetached(["KSArmory"]);
        var other = none.WithDetached(["MeasureTools"]);

        Assert.NotEqual(none, one);
        Assert.NotEqual(one, other);
        Assert.NotEqual(one, none);
    }

    [Fact]
    public void FromModPack_ToString_NamesTheDetachedMods()
    {
        var source = new InstanceSource.FromModPack("pack-a", ModVersion.Parse("1.0.0")).WithDetached(["MeasureTools", "KSArmory"]);

        Assert.Equal("FromModPack { ModPackId = pack-a, Version = 1.0.0, Detached = [KSArmory, MeasureTools] }", source.ToString());
        Assert.Equal("FromModPack { ModPackId = pack-a, Version = 1.0.0, Detached = [] }", new InstanceSource.FromModPack("pack-a", ModVersion.Parse("1.0.0")).ToString());
    }
}
