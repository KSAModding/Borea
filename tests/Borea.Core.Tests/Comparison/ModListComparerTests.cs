using Borea.Core.Comparison;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Tests.Mods;

namespace Borea.Core.Tests.Comparison;

public sealed class ModListComparerTests
{
    private readonly ModListComparer _comparer = new();

    [Fact]
    public void Compare_IdenticalLists_AllUnchanged()
    {
        var current = new[] { TestFixtures.SampleInstalledMod("mod-a", "1.0.0") };
        var target = new[] { new ModPackEntry("mod-a", ModVersion.Parse("1.0.0")) };

        var diff = _comparer.Compare(current, target);

        Assert.Single(diff.Unchanged);
        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void Compare_ModOnlyInTarget_IsToAdd()
    {
        var diff = _comparer.Compare(Array.Empty<InstalledMod>(), new[] { new ModPackEntry("new-mod", ModVersion.Parse("1.0.0")) });

        Assert.Single(diff.ToAdd);
        Assert.Equal("new-mod", diff.ToAdd[0].ModId);
    }

    [Fact]
    public void Compare_ModOnlyInCurrent_IsToRemove()
    {
        var current = new[] { TestFixtures.SampleInstalledMod("old-mod") };

        var diff = _comparer.Compare(current, Array.Empty<ModPackEntry>());

        Assert.Single(diff.ToRemove);
        Assert.Equal("old-mod", diff.ToRemove[0]);
    }

    [Fact]
    public void Compare_SameModDifferentVersion_IsToUpdate()
    {
        var current = new[] { TestFixtures.SampleInstalledMod("mod-a", "1.0.0") };
        var target = new[] { new ModPackEntry("mod-a", ModVersion.Parse("2.0.0")) };

        var diff = _comparer.Compare(current, target);

        Assert.Single(diff.ToUpdate);
        Assert.Equal(ModVersion.Parse("1.0.0"), diff.ToUpdate[0].CurrentVersion);
        Assert.Equal(ModVersion.Parse("2.0.0"), diff.ToUpdate[0].NewVersion);
    }

    [Fact]
    public void Compare_MixedChanges_CategorizesEachCorrectly()
    {
        var current = new[]
        {
            TestFixtures.SampleInstalledMod("unchanged-mod", "1.0.0"),
            TestFixtures.SampleInstalledMod("updated-mod", "1.0.0"),
            TestFixtures.SampleInstalledMod("removed-mod", "1.0.0"),
        };
        var target = new[]
        {
            new ModPackEntry("unchanged-mod", ModVersion.Parse("1.0.0")),
            new ModPackEntry("updated-mod", ModVersion.Parse("2.0.0")),
            new ModPackEntry("added-mod", ModVersion.Parse("1.0.0")),
        };

        var diff = _comparer.Compare(current, target);

        Assert.Single(diff.Unchanged);
        Assert.Single(diff.ToUpdate);
        Assert.Single(diff.ToRemove);
        Assert.Single(diff.ToAdd);
        Assert.False(diff.IsEmpty);
    }

    [Fact]
    public void Compare_BothEmpty_IsEmpty()
    {
        Assert.True(_comparer.Compare(Array.Empty<InstalledMod>(), Array.Empty<ModPackEntry>()).IsEmpty);
    }

    [Fact]
    public void Compare_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => _comparer.Compare(null!, Array.Empty<ModPackEntry>()));
        Assert.Throws<ArgumentNullException>(() => _comparer.Compare(Array.Empty<InstalledMod>(), null!));
    }
}
