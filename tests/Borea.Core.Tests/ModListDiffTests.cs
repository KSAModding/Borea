using Borea.Core.Comparison;
using Borea.Core.ModPacks;
using Borea.Core.Mods;

namespace Borea.Core.Tests;

public sealed class ModListDiffTests
{
    private static readonly ModPackEntry[] EmptyEntries = Array.Empty<ModPackEntry>();
    private static readonly string[] EmptyIds = Array.Empty<string>();
    private static readonly ModVersionChange[] EmptyChanges = Array.Empty<ModVersionChange>();

    [Fact]
    public void Constructor_ValidInput_SetsAllProperties()
    {
        var toAdd = new[] { new ModPackEntry("new-mod", ModVersion.Parse("1.0.0")) };
        var toRemove = new[] { "removed-mod" };
        var toUpdate = new[] { new ModVersionChange("updated-mod", ModVersion.Parse("1.0.0"), ModVersion.Parse("2.0.0")) };
        var unchanged = new[] { "same-mod" };

        var diff = new ModListDiff(toAdd, toRemove, toUpdate, unchanged);

        Assert.Single(diff.ToAdd);
        Assert.Single(diff.ToRemove);
        Assert.Single(diff.ToUpdate);
        Assert.Single(diff.Unchanged);
    }

    [Fact]
    public void Constructor_NullToAdd_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ModListDiff(null!, EmptyIds, EmptyChanges, EmptyIds));
    }

    [Fact]
    public void Constructor_NullToRemove_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ModListDiff(EmptyEntries, null!, EmptyChanges, EmptyIds));
    }

    [Fact]
    public void Constructor_NullToUpdate_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ModListDiff(EmptyEntries, EmptyIds, null!, EmptyIds));
    }

    [Fact]
    public void Constructor_NullUnchanged_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ModListDiff(EmptyEntries, EmptyIds, EmptyChanges, null!));
    }

    [Fact]
    public void IsEmpty_AllActionsEmpty_IsTrue()
    {
        // Unchanged deliberately populated — IsEmpty should ignore it, since
        // it drives no action (per the type's own doc comment).
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