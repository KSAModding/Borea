using Borea.Core.Listings;

namespace Borea.Core.Tests.Listings;

public sealed class ListingAuthorityTests
{
    [Theory]
    [InlineData("owner/repo", null, null, "github", "owner/repo")]
    [InlineData(null, 4253L, null, "spacedock", "4253")]
    [InlineData("owner/repo", 4253L, "spacedock", "spacedock", "4253")]
    [InlineData("owner/repo", 4253L, "github", "github", "owner/repo")]
    public void Of_Releases_BindsToTheOneHostOrTheNamedAuthority(string? github, long? spaceDock, string? authority, string kind, string target)
    {
        var draft = new ListingDraft { Releases = new ListingReleases(github, spaceDock, authority) };

        Assert.Equal(new ListingAuthority(kind, target), ListingAuthority.Of(draft));
    }

    [Theory]
    [InlineData("owner/repo", 4253L, null)]
    [InlineData("owner/repo", 4253L, "gitlab")]
    [InlineData(null, null, "github")]
    public void Of_ReleasesWithoutAUsableHost_BindsToNothing(string? github, long? spaceDock, string? authority)
    {
        var draft = new ListingDraft
        {
            Releases = new ListingReleases(github, spaceDock, authority),
            Links = [new ListingLink("repository", "https://github.com/owner/repo")],
        };

        Assert.Null(ListingAuthority.Of(draft));
    }

    [Fact]
    public void Of_NoReleases_BindsToTheRepositoryLink()
    {
        var draft = new ListingDraft { Links = [new ListingLink("repository", "https://github.com/Owner/Repo.git")] };

        Assert.Equal(new ListingAuthority("github", "Owner/Repo"), ListingAuthority.Of(draft));
    }

    [Theory]
    [InlineData("https://github.com/owner/repo", "owner/repo")]
    [InlineData("https://www.GitHub.com/owner/repo/tree/main", "owner/repo")]
    [InlineData("http://github.com/owner/repo.git", "owner/repo")]
    [InlineData("https://github.com/owner", null)]
    [InlineData("https://gitlab.com/owner/repo", null)]
    [InlineData("https://github.com/-owner/repo", null)]
    [InlineData("github.com/owner/repo", null)]
    [InlineData(null, null)]
    public void GitHubRepositoryOf_ReadsOwnerAndName(string? url, string? repository)
    {
        Assert.Equal(repository, ListingAuthority.GitHubRepositoryOf(url));
    }

    [Fact]
    public void IsSameHost_IgnoresCase()
    {
        Assert.True(new ListingAuthority("github", "Owner/Repo").IsSameHost(new ListingAuthority("github", "owner/repo")));
        Assert.False(new ListingAuthority("github", "owner/repo").IsSameHost(new ListingAuthority("github", "owner/other")));
        Assert.False(new ListingAuthority("github", "owner/repo").IsSameHost(null));
    }

    [Fact]
    public void TopicFor_LowercasesTheLogin()
    {
        Assert.Equal("ksa-index-maximilian-nesslauer", ListingOwnership.TopicFor("Maximilian-Nesslauer"));
    }
}
