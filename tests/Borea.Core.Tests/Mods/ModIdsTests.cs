using Borea.Core.Mods;

namespace Borea.Core.Tests.Mods;

public sealed class ModIdsTests
{
    [Theory]
    [InlineData("MyMod")]
    [InlineData("my-mod")]
    [InlineData("my_mod")]
    [InlineData("my.mod")]
    [InlineData("a")]
    [InlineData("Mod2")]
    public void IsValid_ValidIds_ReturnsTrue(string id)
    {
        Assert.True(ModIds.IsValid(id));
    }

    [Fact]
    public void IsValid_SixtyFourCharacters_IsTheUpperBound()
    {
        Assert.True(ModIds.IsValid(new string('a', 64)));
        Assert.False(ModIds.IsValid(new string('a', 65)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".MyMod")]
    [InlineData("MyMod.")]
    [InlineData("-MyMod")]
    [InlineData("MyMod-")]
    [InlineData("My Mod")]
    [InlineData("My\"Mod")]
    [InlineData("My/Mod")]
    public void IsValid_InvalidShapes_ReturnsFalse(string? id)
    {
        Assert.False(ModIds.IsValid(id));
    }

    [Theory]
    [InlineData("Core")]
    [InlineData("core")]
    [InlineData("CON")]
    [InlineData("CON.mod")]
    [InlineData("nul.foo")]
    [InlineData("COM3")]
    [InlineData("lpt9.bar")]
    public void IsValid_ReservedNames_ReturnsFalse(string id)
    {
        Assert.False(ModIds.IsValid(id));
    }

    [Theory]
    [InlineData("corekit")]
    [InlineData("Console")]
    [InlineData("COM10")]
    public void IsValid_NamesThatOnlyResembleReservedOnes_ReturnsTrue(string id)
    {
        Assert.True(ModIds.IsValid(id));
    }

    [Fact]
    public void Equals_ComparesCaseInsensitively()
    {
        Assert.True(ModIds.Equals("MyMod", "mymod"));
        Assert.False(ModIds.Equals("MyMod", "MyOtherMod"));
    }

    [Fact]
    public void Validate_InvalidId_ThrowsWithParamName()
    {
        var exception = Assert.Throws<ArgumentException>(() => ModIds.Validate("CON", "id"));
        Assert.Equal("id", exception.ParamName);
    }
}
