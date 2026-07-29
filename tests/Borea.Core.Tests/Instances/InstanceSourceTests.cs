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
}