using Borea.Core.Listings;

namespace Borea.Core.Tests.Listings;

public sealed class GitHubBranchAddressTests
{
    [Theory]
    [InlineData("https://raw.githubusercontent.com/elevatorctln/KSA-Public-Telemetry/refs/heads/main/images/9e-hero.png", "main", "images/9e-hero.png", true)]
    [InlineData(" https://RAW.githubusercontent.com/owner/repo/main/icon.png ", "main", "icon.png", false)]
    [InlineData("https://raw.githubusercontent.com/owner/repo/release%2F1/docs/a%20b.png", "release/1", "docs/a%20b.png", false)]
    public void Of_BranchAddress_NamesTheBranchAndThePath(string url, string branch, string path, bool isSure)
    {
        var address = GitHubBranchAddress.Of(url);

        Assert.NotNull(address);
        Assert.Equal((branch, path, isSure), (address.Branch, address.Path, address.IsSure));
        Assert.Equal($"https://raw.githubusercontent.com/{address.Owner}/{address.Repository}/0123456789abcdef0123456789abcdef01234567/{path}", address.AtCommit("0123456789abcdef0123456789abcdef01234567"));
    }

    [Theory]
    [InlineData("https://raw.githubusercontent.com/owner/repo/refs/heads/main/icon.png", "main")]
    [InlineData("https://raw.githubusercontent.com/owner/repo/refs/heads/main/images/icon.png", null)]
    [InlineData("https://raw.githubusercontent.com/owner/repo/main/icon.png", null)]
    public void CertainBranch_NamesOnlyTheOneBranchTheAddressCanName(string url, string? branch)
    {
        Assert.Equal(branch, GitHubBranchAddress.Of(url)!.CertainBranch);
    }

    [Fact]
    public void Longer_RefsHeadsAddress_MovesOneSegmentAtATimeIntoTheBranch()
    {
        var address = GitHubBranchAddress.Of("https://raw.githubusercontent.com/owner/repo/refs/heads/release/1.0/images/a.png")!;

        var longer = address.Longer()!;
        var longest = longer.Longer()!;

        Assert.Equal(("release/1.0", "images/a.png"), (longer.Branch, longer.Path));
        Assert.Equal(("release/1.0/images", "a.png"), (longest.Branch, longest.Path));
        Assert.Null(longest.Longer());
        Assert.Null(GitHubBranchAddress.Of("https://raw.githubusercontent.com/owner/repo/main/images/a.png")!.Longer());
    }

    [Theory]
    [InlineData("https://raw.githubusercontent.com/owner/repo/0123456789abcdef0123456789abcdef01234567/icon.png")]
    [InlineData("https://raw.githubusercontent.com/owner/repo/0123abc/icon.png")]
    [InlineData("https://raw.githubusercontent.com/owner/repo/refs/tags/v1.0/icon.png")]
    [InlineData("https://raw.githubusercontent.com/owner/repo/refs/heads/main")]
    [InlineData("https://raw.githubusercontent.com/owner/repo/main/icon.png?token=abc")]
    [InlineData("http://raw.githubusercontent.com/owner/repo/main/icon.png")]
    [InlineData("https://example.com/owner/repo/main/icon.png")]
    [InlineData("not an address")]
    public void Of_AddressOnNoBranch_IsNull(string url)
    {
        Assert.Null(GitHubBranchAddress.Of(url));
    }
}
