using Borea.Core.Mods;
using Borea.Core.Instances;
using Borea.Core.Planning;
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
        Assert.Empty(instance.LaunchArguments);
    }

    [Fact]
    public void SetLaunchArguments_KeepsACopyInOrder()
    {
        var instance = new Instance("My Instance", InstanceSource.Custom.Value);
        var arguments = new List<string> { "-windowed", "a b", "" };

        instance.SetLaunchArguments(arguments);
        arguments.Add("-later");

        Assert.Equal(["-windowed", "a b", ""], instance.LaunchArguments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("a\0b")]
    public void SetLaunchArguments_NullOrNullCharacter_ThrowsArgumentException(string? argument)
    {
        var instance = new Instance("My Instance", InstanceSource.Custom.Value);

        Assert.Throws<ArgumentException>(() => instance.SetLaunchArguments(["-windowed", argument!]));
        Assert.Empty(instance.LaunchArguments);
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
    public void SetPinned_PinsAndUnpinsTheMod_AndSaysWhetherItChanged()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod());

        Assert.True(instance.SetPinned("TEST-MOD", pinned: true));
        Assert.True(instance.Mods.Single().IsPinned);
        Assert.False(instance.SetPinned("test-mod", pinned: true));
        Assert.True(instance.SetPinned("test-mod", pinned: false));
        Assert.False(instance.Mods.Single().IsPinned);
    }

    [Fact]
    public void SetPinned_ModBoreaDoesNotOwnOrDoesNotHave_Throws()
    {
        var foreign = TestFixtures.SampleInstalledMod();
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", InstanceSource.Custom.Value, DateTimeOffset.UnixEpoch, [new InstalledMod(foreign.ModId, foreign.Version, foreign.Reason, foreign.InstalledAt, foreign.Metadata, ownership: ModInstallOwnership.Foreign)], false);

        Assert.Throws<InvalidOperationException>(() => instance.SetPinned("test-mod", pinned: true));
        Assert.False(instance.Mods.Single().IsPinned);
        Assert.Throws<InvalidOperationException>(() => instance.SetPinned("other-mod", pinned: true));
    }

    [Fact]
    public void DetachFromModPack_PackMod_BecomesAModThePlayerChose_AndTheSourceNamesIt()
    {
        var instance = new Instance("Test", PackSource());
        instance.AddMod(TestFixtures.SampleInstalledMod(reason: InstallReason.ModPack));

        Assert.True(instance.DetachFromModPack("TEST-MOD"));

        Assert.Equal(InstallReason.Manual, instance.Mods.Single().Reason);
        Assert.Equal(PackSource().WithDetached(["test-mod"]), instance.Source);
        Assert.False(instance.DetachFromModPack("test-mod"));
        Assert.True(InstallPlanningState.Capture(instance) != InstallPlanningState.Capture(Instance.FromExisting(instance.InstanceId, instance.Name, PackSource(), instance.CreatedAt, instance.Mods, false)));
    }

    [Fact]
    public void DetachFromModPack_ModThatIsNotOfThePack_Throws()
    {
        var custom = new Instance("Custom", InstanceSource.Custom.Value);
        custom.AddMod(TestFixtures.SampleInstalledMod(reason: InstallReason.ModPack));
        var pack = new Instance("Pack", PackSource());
        pack.AddMod(TestFixtures.SampleInstalledMod(reason: InstallReason.Dependency));

        Assert.Throws<InvalidOperationException>(() => custom.DetachFromModPack("test-mod"));
        Assert.Throws<InvalidOperationException>(() => pack.DetachFromModPack("test-mod"));
        Assert.Throws<InvalidOperationException>(() => pack.DetachFromModPack("other-mod"));
        Assert.Equal(InstallReason.Dependency, pack.Mods.Single().Reason);
        Assert.Empty(((InstanceSource.FromModPack)pack.Source).Detached);
    }

    [Fact]
    public void AttachToModPack_DetachedMod_FollowsThePackAgain()
    {
        var instance = new Instance("Test", PackSource());
        instance.AddMod(TestFixtures.SampleInstalledMod(reason: InstallReason.ModPack));
        instance.DetachFromModPack("test-mod");

        Assert.True(instance.AttachToModPack("TEST-MOD"));

        Assert.Equal(InstallReason.ModPack, instance.Mods.Single().Reason);
        Assert.Equal(PackSource(), instance.Source);
        Assert.False(instance.AttachToModPack("test-mod"));
    }

    [Fact]
    public void AttachToModPack_DetachedModThatThePlayerRemoved_LeavesTheSet()
    {
        var source = PackSource().WithDetached(["test-mod", "other-mod"]);
        var instance = Instance.FromExisting(Guid.NewGuid(), "Test", source, DateTimeOffset.UnixEpoch, [], false);

        Assert.True(instance.AttachToModPack("test-mod"));

        Assert.Equal(PackSource().WithDetached(["other-mod"]), instance.Source);
    }

    [Fact]
    public void AttachToModPack_ModThatIsNotDetached_Throws()
    {
        var custom = new Instance("Custom", InstanceSource.Custom.Value);
        custom.AddMod(TestFixtures.SampleInstalledMod(reason: InstallReason.Manual));
        var pack = new Instance("Pack", PackSource());
        pack.AddMod(TestFixtures.SampleInstalledMod(reason: InstallReason.Manual));

        Assert.Throws<InvalidOperationException>(() => custom.AttachToModPack("test-mod"));
        Assert.Throws<InvalidOperationException>(() => pack.AttachToModPack("test-mod"));
        Assert.Throws<InvalidOperationException>(() => pack.AttachToModPack("other-mod"));
        Assert.Equal(InstallReason.Manual, pack.Mods.Single().Reason);
    }

    [Theory]
    [InlineData("dup-mod")]
    [InlineData("Dup-Mod")]
    public void AddMod_DuplicateModIdInAnyCase_ThrowsInvalidOperationException(string duplicateId)
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod("dup-mod"));

        Assert.Throws<InvalidOperationException>(() => instance.AddMod(TestFixtures.SampleInstalledMod(duplicateId)));
    }

    [Fact]
    public void AddMod_Null_ThrowsArgumentNullException()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);

        Assert.Throws<ArgumentNullException>(() => instance.AddMod(null!));
    }

    [Theory]
    [InlineData("mod-a")]
    [InlineData("Mod-A")]
    public void RemoveMod_ExistingModInAnyCase_RemovesAndReturnsTrue(string modId)
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.AddMod(TestFixtures.SampleInstalledMod("mod-a"));

        var removed = instance.RemoveMod(modId);

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
    public void LastPlayedWith_GameLogNewerThanTheRecord_ReturnsTheLogTime()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        instance.RecordPlayed(new DateTimeOffset(2026, 9, 14, 20, 0, 0, TimeSpan.Zero));
        var logWrittenAt = new DateTimeOffset(2026, 9, 15, 9, 58, 0, TimeSpan.Zero);

        Assert.Equal(logWrittenAt, instance.LastPlayedWith(logWrittenAt));
    }

    [Fact]
    public void LastPlayedWith_RecordNewerOrNoGameLog_ReturnsTheRecord()
    {
        var instance = new Instance("Test", InstanceSource.Custom.Value);
        var playedAt = new DateTimeOffset(2026, 9, 15, 9, 58, 0, TimeSpan.Zero);
        instance.RecordPlayed(playedAt);

        Assert.Equal(playedAt, instance.LastPlayedWith(playedAt.AddHours(-1)));
        Assert.Equal(playedAt, instance.LastPlayedWith(null));
    }

    [Fact]
    public void LastPlayedWith_NeverPlayedAndNoGameLog_ReturnsNull()
    {
        Assert.Null(new Instance("Test", InstanceSource.Custom.Value).LastPlayedWith(null));
    }

    private static InstanceSource.FromModPack PackSource() => new("pack", ModVersion.Parse("1.0.0"));
}
