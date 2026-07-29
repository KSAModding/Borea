using Borea.Core.Game;
using Borea.Core.ModPacks;
using Borea.Core.Mods;

namespace Borea.Core.Tests.ModPacks;

public sealed class ModPackMetadataTests
{
    private static readonly ModPackEntry[] OneMod =
        { new("some-mod", ModVersion.Parse("1.0.0")) };

    private static ModPackMetadata Build(
        string modPackId = "test-pack",
        string name = "Test Pack",
        string author = "Author",
        ModPackEntry[]? mods = null) =>
        new(
            modPackId,
            name,
            author,
            ModVersion.Parse("1.0.0"),
            GameVersion.Parse("2026.7.4.2131"),
            "Description",
            DateTimeOffset.UtcNow,
            mods!);

    [Fact]
    public void Constructor_ValidInput_SetsAllProperties()
    {
        var pack = Build(mods:OneMod);

        Assert.Equal("test-pack", pack.ModPackId);
        Assert.Single(pack.Mods);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidModPackId_ThrowsArgumentException(string? modPackId)
    {
        Assert.Throws<ArgumentException>(() => Build(modPackId: modPackId!));
    }

    [Fact]
    public void Constructor_EmptyModsList_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Build(mods: Array.Empty<ModPackEntry>()));
    }

    [Fact]
    public void Constructor_NullModsList_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Build(mods: null));
    }
}