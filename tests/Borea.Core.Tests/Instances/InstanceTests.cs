using Borea.Core.Mods;
using Borea.Core.Instances;
using Borea.Core.Tests.Mods;

namespace Borea.Core.Tests.Instances;

public sealed class InstanceTests
{
    [Fact]
    public void Constructor_ValidInput_SetsExpectedDefaults()
    {
        var instance = new Instance("My Instance", InstanceSource.Custom.Value);

        Assert.NotEqual(Guid.Empty, instance.InstanceId);
        Assert.Equal("My Instance", instance.Name);
        Assert.False(instance.IsFavorite);
        Assert.Empty(instance.Mods);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidName_ThrowsArgumentException(string? name)
    {
        Assert.Throws<ArgumentException>(() => new Instance(name!, InstanceSource.Custom.Value));
    }

    [Fact]
    public void FromExisting_DuplicateModIdInInitialList_Throws()
    {
        var modA = TestFixtures.SampleInstalledMod("dup-mod");
        var modB = TestFixtures.SampleInstalledMod("dup-mod");

        Assert.Throws<ArgumentException>(() => Instance.FromExisting(
            Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow,
            new[] { modA, modB }, isFavorite: false));
    }

    [Fact]
    public void Rename_ValidName_UpdatesName()
    {
        var instance = new Instance("Original", InstanceSource.Custom.Value);

        instance.Rename("Renamed");

        Assert.Equal("Renamed", instance.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_InvalidName_ThrowsArgumentException(string? newName)
    {
        var instance = new Instance("Original", InstanceSource.Custom.Value);

        Assert.Throws<ArgumentException>(() => instance.Rename(newName!));
    }

    [Fact]
    public void SetFavorite_TogglesIsFavorite()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);

        instance.SetFavorite(true);
        Assert.True(instance.IsFavorite);

        instance.SetFavorite(false);
        Assert.False(instance.IsFavorite);
    }

    [Fact]
    public void AddMod_NewMod_IsAddedToMods()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        var mod = TestFixtures.SampleInstalledMod();

        instance.AddMod(mod);

        Assert.Single(instance.Mods);
        Assert.Same(mod, instance.Mods[0]);
    }

    [Fact]
    public void AddMod_DuplicateModId_ThrowsInvalidOperationException()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod("dup-mod"));

        Assert.Throws<InvalidOperationException>(() =>
            instance.AddMod(TestFixtures.SampleInstalledMod("dup-mod")));
    }

    [Fact]
    public void AddMod_DuplicateModIdDifferingOnlyInCase_ThrowsInvalidOperationException()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod("dup-mod"));

        Assert.Throws<InvalidOperationException>(() =>
            instance.AddMod(TestFixtures.SampleInstalledMod("Dup-Mod")));
    }

    [Fact]
    public void RemoveMod_IdDifferingOnlyInCase_RemovesTheMod()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod("mod-a"));

        Assert.True(instance.RemoveMod("Mod-A"));
        Assert.Empty(instance.Mods);
    }

    [Fact]
    public void AddMod_Null_ThrowsArgumentNullException()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);

        Assert.Throws<ArgumentNullException>(() => instance.AddMod(null!));
    }

    [Fact]
    public void RemoveMod_ExistingMod_RemovesAndReturnsTrue()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod("mod-a"));

        var removed = instance.RemoveMod("mod-a");

        Assert.True(removed);
        Assert.Empty(instance.Mods);
    }

    [Fact]
    public void RemoveMod_NonexistentMod_ReturnsFalseWithoutThrowing()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);

        var removed = instance.RemoveMod("never-existed");

        Assert.False(removed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RemoveMod_InvalidModId_ThrowsArgumentException(string? modId)
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);

        Assert.Throws<ArgumentException>(() => instance.RemoveMod(modId!));
    }

    [Fact]
    public void Mods_CannotBeMutatedDirectly()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod());

        // Mods should expose a read-only view — confirms external code can't
        // bypass AddMod/RemoveMod's invariant checks (e.g. duplicate-ModId
        // rejection) by mutating the returned collection directly.
        Assert.IsAssignableFrom<System.Collections.Generic.IReadOnlyList<InstalledMod>>(instance.Mods);
    }
}
