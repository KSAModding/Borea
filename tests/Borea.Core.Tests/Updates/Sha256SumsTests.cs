using Borea.Core.Updates;

namespace Borea.Core.Tests.Updates;

public sealed class Sha256SumsTests
{
    private const string ArchiveHash = "3b1f2c4d5e6a7b8c9d0e1f2a3b4c5d6e7f8091a2b3c4d5e6f708192a3b4c5d6e";

    private const string OtherHash = "0000111122223333444455556666777788889999aaaabbbbccccddddeeeeffff";

    private static readonly string Listing =
        $"{ArchiveHash}  Borea-0.2.0-win-x64.zip\n{OtherHash}  Borea-Cli-0.2.0-win-x64.zip\n";

    [Fact]
    public void Find_ANamedFile_IsItsHashInUppercase()
        => Assert.Equal(ArchiveHash.ToUpperInvariant(), Sha256Sums.Find(Listing, "Borea-0.2.0-win-x64.zip"));

    [Fact]
    public void Find_ALineWithWindowsEndingsAndTheBinaryMarker_IsRead()
    {
        var listing = $"{ArchiveHash} *Borea-0.2.0-win-x64.zip\r\n";

        Assert.Equal(ArchiveHash.ToUpperInvariant(), Sha256Sums.Find(listing, "Borea-0.2.0-win-x64.zip"));
    }

    [Fact]
    public void Find_AFileTheListingDoesNotName_IsNull()
        => Assert.Null(Sha256Sums.Find(Listing, "Borea-0.2.0-linux-x64.tar.gz"));

    [Fact]
    public void Find_ALineThatIsNoChecksum_IsSkipped()
    {
        var listing = $"not a hash  Borea-0.2.0-win-x64.zip\nzzzz{ArchiveHash[4..]}  Borea-0.2.0-win-x64.zip\n";

        Assert.Null(Sha256Sums.Find(listing, "Borea-0.2.0-win-x64.zip"));
    }

    [Fact]
    public void Find_NoListing_IsNull()
    {
        Assert.Null(Sha256Sums.Find(null, "Borea-0.2.0-win-x64.zip"));
        Assert.Null(Sha256Sums.Find(string.Empty, "Borea-0.2.0-win-x64.zip"));
    }
}
