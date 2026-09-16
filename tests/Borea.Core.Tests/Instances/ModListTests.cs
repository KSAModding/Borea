using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.State;
using Borea.Core.Tests.Mods;

namespace Borea.Core.Tests.Instances;

public sealed class ModListTests
{
    [Fact]
    public void FromInstance_FollowsTheLoadOrderAndTheEnabledFlags()
    {
        var instance = InstanceWith(TestFixtures.SampleInstalledMod("alpha", "1.0.0"), TestFixtures.SampleInstalledMod("beta", "2.1.0"));

        var modList = ModList.FromInstance(instance, [new ModManifestEntry("Beta", Enabled: false), new ModManifestEntry("alpha", Enabled: true)]);

        Assert.Equal("Main", modList.Name);
        Assert.Equal(
            [new ModListEntry("beta", ModVersion.Parse("2.1.0"), enabled: false), new ModListEntry("alpha", ModVersion.Parse("1.0.0"), enabled: true)],
            modList.Mods);
    }

    [Fact]
    public void FromInstance_ModWithoutAManifestEntry_IsDisabledAndLast()
    {
        var instance = InstanceWith(TestFixtures.SampleInstalledMod("alpha"), TestFixtures.SampleInstalledMod("beta"));

        var modList = ModList.FromInstance(instance, [new ModManifestEntry("beta", Enabled: true)]);

        Assert.Equal(["beta", "alpha"], modList.Mods.Select(entry => entry.ModId));
        Assert.False(modList.Mods[1].Enabled);
    }

    [Fact]
    public void FromInstance_LeavesOutFoldersBoreaDidNotRecord()
    {
        var instance = Instance.FromExisting(
            Guid.NewGuid(), "Main", InstanceSource.Custom.Value, DateTimeOffset.UtcNow,
            [TestFixtures.SampleInstalledMod("alpha")], [new ForeignMod("LocalOnly")], isFavorite: false);

        var modList = ModList.FromInstance(instance, [new ModManifestEntry("alpha", Enabled: true), new ModManifestEntry("LocalOnly", Enabled: true)]);

        Assert.Equal("alpha", Assert.Single(modList.Mods).ModId);
    }

    [Fact]
    public void Constructor_SameIdInAnotherCase_Throws()
    {
        var version = ModVersion.Parse("1.0.0");

        Assert.Throws<ArgumentException>(() => new ModList("Main", [new ModListEntry("alpha", version, enabled: true), new ModListEntry("ALPHA", version, enabled: true)]));
    }

    [Fact]
    public void Entry_InvalidId_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ModListEntry("alpha beta", ModVersion.Parse("1.0.0"), enabled: true));
    }

    private static Instance InstanceWith(params InstalledMod[] mods) =>
        Instance.FromExisting(Guid.NewGuid(), "Main", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, mods, isFavorite: false);
}
