using Borea.Core.ModPacks;
using Borea.Core.Mods;

namespace Borea.Core.Tests.ModPacks;

public sealed class ModPackEntryTests
{
    private static ModPackEntry Entry(string contentId, string version = "1.0.0") =>
        new(contentId, ModVersion.Parse(version));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a valid id")]
    public void Constructor_InvalidContentId_ThrowsArgumentException(string? contentId)
    {
        Assert.Throws<ArgumentException>(() => new ModPackEntry(contentId!, ModVersion.Parse("1.0.0")));
    }

    [Fact]
    public void Constructor_DefaultVersion_IsTheZeroVersion()
    {
        var entry = new ModPackEntry("some-mod", default);

        Assert.Equal(ModVersion.Parse("0.0.0"), entry.Version);
    }

    [Fact]
    public void Equals_IdsDifferingOnlyByCase_AreEqual()
    {
        Assert.Equal(Entry("SomeMod"), Entry("somemod"));
        Assert.Equal(Entry("SomeMod").GetHashCode(), Entry("somemod").GetHashCode());
    }

    [Fact]
    public void Equals_SameIdDifferentVersion_AreNotEqual()
    {
        Assert.NotEqual(Entry("some-mod", "1.0.0"), Entry("some-mod", "2.0.0"));
    }
}
