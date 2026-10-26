using Borea.Core.Comparison;
using Borea.Core.ModPacks;
using Borea.Core.Mods;

namespace Borea.Core.Tests.Comparison;

public sealed class ModListDiffTests
{
    private static readonly ModPackEntry[] EmptyEntries = Array.Empty<ModPackEntry>();
    private static readonly string[] EmptyIds = Array.Empty<string>();
    private static readonly ModVersionChange[] EmptyChanges = Array.Empty<ModVersionChange>();

    [Fact]
    public void Constructor_NullArgument_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ModListDiff(null!, EmptyIds, EmptyChanges, EmptyIds));
        Assert.Throws<ArgumentNullException>(() => new ModListDiff(EmptyEntries, null!, EmptyChanges, EmptyIds));
        Assert.Throws<ArgumentNullException>(() => new ModListDiff(EmptyEntries, EmptyIds, null!, EmptyIds));
        Assert.Throws<ArgumentNullException>(() => new ModListDiff(EmptyEntries, EmptyIds, EmptyChanges, null!));
    }

    [Fact]
    public void IsEmpty_AllActionsEmpty_IsTrue()
    {
        // Unchanged holds a mod on purpose, because a mod that stays at its version needs no action and IsEmpty ignores it.
        var diff = new ModListDiff(EmptyEntries, EmptyIds, EmptyChanges, new[] { "same-mod" });

        Assert.True(diff.IsEmpty);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void IsEmpty_AnyActionPopulated_IsFalse(bool hasAdd, bool hasRemove, bool hasUpdate)
    {
        var toAdd = hasAdd ? new[] { new ModPackEntry("mod", ModVersion.Parse("1.0.0")) } : EmptyEntries;
        var toRemove = hasRemove ? new[] { "mod" } : EmptyIds;
        var toUpdate = hasUpdate ? new[] { new ModVersionChange("mod", ModVersion.Parse("1.0.0"), ModVersion.Parse("2.0.0")) } : EmptyChanges;

        var diff = new ModListDiff(toAdd, toRemove, toUpdate, EmptyIds);

        Assert.False(diff.IsEmpty);
    }
}
