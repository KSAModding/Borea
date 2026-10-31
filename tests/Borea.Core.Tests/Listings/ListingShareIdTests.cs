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

    [Theory]
    [InlineData("Rakete \x00DCber 2", "RaketeUeber2")]
    [InlineData("\x00C4rger \x00D6l \x00FCbel", "AergerOeluebel")]
    [InlineData("Gr\x00F6\x00DFe Fl\x00E4che", "GroesseFlaeche")]
    [InlineData("GRO\x1E9E", "GROSS")]
    [InlineData("Caf\x00E9 Fa\x00E7ade", "CafeFacade")]
    [InlineData("Cafe\x0301", "Cafe")]
    [InlineData("\x0414\x043E\x043C", "")]
    public void From_MapsGermanLettersAndKeepsTheBaseLetterOfAnAccent(string name, string id)
    {
        Assert.Equal(id, ListingShareId.From(name));
    }

    [Theory]
    [InlineData("a", 80)]
    [InlineData("\x00E4", 40)]
    public void From_LongName_IsCutAtTheLongestIdAfterTheMapping(string letter, int count)
    {
        var id = ListingShareId.From(string.Concat(Enumerable.Repeat(letter, count)));

        Assert.Equal(ListingShareId.MaxLength, id.Length);
        Assert.True(id.All(char.IsAsciiLetterOrDigit));
    }
}
