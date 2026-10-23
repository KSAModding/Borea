using Borea.Network.SpaceDock;

namespace Borea.Network.Tests.SpaceDock;

public sealed class SpaceDockResolverTests
{
    [Fact]
    public void TryResolve_UnregisteredModId_ReturnsFalse()
    {
        var resolver = new SpaceDockResolver();

        var success = resolver.TryResolve("never-registered", out _);

        Assert.False(success);
    }

    [Theory]
    [InlineData("some-mod", "some-mod", 12345)]
    // Ids compare case-insensitively everywhere (ModIds.Comparer), so a lookup must not miss over casing.
    [InlineData("MyMod", "mymod", 42)]
    public void TryResolve_RegisteredModId_ReturnsItsSpaceDockId(string registered, string lookedUp, int spaceDockId)
    {
        var resolver = new SpaceDockResolver();

        resolver.Register(registered, spaceDockId);
        var success = resolver.TryResolve(lookedUp, out var resolved);

        Assert.True(success);
        Assert.Equal(spaceDockId, resolved);
    }

    [Fact]
    public void Register_SameModIdTwice_OverwritesRatherThanThrowing()
    {
        // Models re-registering the same mod for a later version. Per
        // design, a mod's ModId never changes between its own versions,
        // so this should be a safe, silent no-op-in-effect, not an error.
        var resolver = new SpaceDockResolver();

        resolver.Register("some-mod", 111);
        resolver.Register("some-mod", 111); // Same version's mod re-registered.

        resolver.TryResolve("some-mod", out var spaceDockId);
        Assert.Equal(111, spaceDockId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_InvalidModId_ThrowsArgumentException(string? modId)
    {
        var resolver = new SpaceDockResolver();

        Assert.Throws<ArgumentException>(() => resolver.Register(modId!, 123));
    }

    [Fact]
    public void MultipleMods_ResolveIndependently()
    {
        var resolver = new SpaceDockResolver();

        resolver.Register("mod-a", 1);
        resolver.Register("mod-b", 2);

        resolver.TryResolve("mod-a", out var idA);
        resolver.TryResolve("mod-b", out var idB);

        Assert.Equal(1, idA);
        Assert.Equal(2, idB);
    }
}
