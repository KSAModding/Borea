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

    [Theory]
    [InlineData("pack-a", "2.0.0")]
    [InlineData("pack-b", "1.0.0")]
    public void FromModPack_DifferentModPackIdOrVersion_AreNotEqual(string modPackId, string version)
    {
        // Borea tells an update in place from a new instance by this equality.
        var a = new InstanceSource.FromModPack("pack-a", ModVersion.Parse("1.0.0"));
        var b = new InstanceSource.FromModPack(modPackId, ModVersion.Parse(version));

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
