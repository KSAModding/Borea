using Borea.Core.Listings;

namespace Borea.Core.Tests.Listings;

public sealed class ListingFoldersTests
{
    [Theory]
    [InlineData("vehicle", "vehicles")]
    [InlineData("save", "saves")]
    [InlineData("mod", "listings")]
    [InlineData("mod-loader", "listings")]
    [InlineData("Vehicle", "listings")]
    [InlineData(null, "listings")]
    public void Of_TheTypeOfAListing_GivesItsFolder(string? type, string folder)
    {
        Assert.Equal(folder, ListingFolders.Of(type));
    }

    [Theory]
    [InlineData("listings/MyMod.toml", true)]
    [InlineData("vehicles/ExampleRover.toml", true)]
    [InlineData("saves/ExampleSave.toml", true)]
    [InlineData("vehicles/ExampleRover.TOML", false)]
    [InlineData("vehicles/.toml", false)]
    [InlineData("vehicles/a/b.toml", false)]
    [InlineData("Vehicles/ExampleRover.toml", false)]
    [InlineData("packs/my-pack/1.0.0.toml", false)]
    [InlineData("ExampleRover.toml", false)]
    [InlineData(null, false)]
    public void IsDocumentPath_TakesOneTomlDirectlyInAListingFolder(string? path, bool expected)
    {
        Assert.Equal(expected, ListingFolders.IsDocumentPath(path));
    }
}
