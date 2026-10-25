using Borea.Core.Listings;

namespace Borea.Core.Tests.Listings;

public sealed class ListingShareIdTests
{
    [Theory]
    [InlineData("Example Rover", "ExampleRover")]
    [InlineData("Mun-Base_1.0", "MunBase10")]
    [InlineData("- . _", "")]
    [InlineData(null, "")]
    public void From_KeepsTheAsciiLettersAndDigitsOfTheName(string? name, string id)
    {
        Assert.Equal(id, ListingShareId.From(name));
    }

    [Fact]
    public void From_LetterOutsideAscii_IsLeftOut()
    {
        Assert.Equal("Raketeber2", ListingShareId.From($"Rakete {(char)0xDC}ber 2"));
    }

    [Fact]
    public void From_LongName_IsCutAtTheLongestId()
    {
        Assert.Equal(new string('a', ListingShareId.MaxLength), ListingShareId.From(new string('a', 80)));
    }
}
