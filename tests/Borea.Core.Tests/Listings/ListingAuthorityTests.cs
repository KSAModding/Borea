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

    /// <summary>The answers of github_repository in tools/ownership.py of content-index, case by case.</summary>
    [Theory]
    [InlineData("https://github.com/Maxi/KSA-AutoStage", "Maxi/KSA-AutoStage")]
    [InlineData("https://github.com/Maxi/KSA-AutoStage.git", "Maxi/KSA-AutoStage")]
    [InlineData("https://gitlab.com/Maxi/Thing", null)]
    [InlineData("https://github.com/Maxi", null)]
    [InlineData("https://github.com/Maxi/Bad Name", null)]
    [InlineData("https://github.com/Maxi/M\u00f6d", null)]
    [InlineData("https://github.com/-Maxi/Thing", null)]
    [InlineData("https://github.com/Maxi/Thing.", null)]
    [InlineData("https://github.com/Maxi/%2e%2e", null)]
    [InlineData("https://github.com/Maxi/KSA-AutoStage/tree/main", "Maxi/KSA-AutoStage")]
    [InlineData("https://www.GitHub.com/owner/repo/tree/main", "owner/repo")]
    [InlineData("http://github.com/owner/repo.git", "owner/repo")]
    [InlineData("github.com/owner/repo", null)]
    [InlineData("https://github.com:443/owner/repo", null)]
    [InlineData("https://user@github.com/owner/repo", null)]
    [InlineData("https://github.com/owner/repo;x", "owner/repo")]
    [InlineData("https://github.com/owner;x/repo", null)]
    [InlineData("https://github.com/owner/%41repo", null)]
    [InlineData("HTTPS://GitHub.com/owner/repo", "owner/repo")]
    [InlineData("ftp://github.com/owner/repo", "owner/repo")]
    [InlineData("//github.com/owner/repo", "owner/repo")]
    [InlineData(" \u0001https://github.com/owner/repo", "owner/repo")]
    [InlineData("https://github.com/owner/repo\u000a", "owner/repo")]
    [InlineData("https://git\u0009hub.com/own\u000aer/re\u000dpo", "owner/repo")]
    [InlineData("https://github.com/owner/repo.git.git", "owner/repo.git")]
    [InlineData("https://github.com/owner/.git", null)]
    [InlineData("https://github.com//owner//repo", "owner/repo")]
    [InlineData("https://github.com/owner/repo?x=1#y", "owner/repo")]
    [InlineData("https://github.com?owner/repo", null)]
    [InlineData("https://github.com#/owner/repo", null)]
    [InlineData("https://[github.com/owner/repo", null)]
    [InlineData("https://g\u0131thub.com/owner/repo", null)]
    [InlineData("https://github.com./owner/repo", null)]
    [InlineData("mailto:github.com/owner/repo", null)]
    [InlineData("1https://github.com/owner/repo", null)]
    [InlineData("evil\u000a\u000a**Validated.** @stewards <!-- https://github.com/Maxi/KSA-AutoStage", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void GitHubRepositoryOf_ReadsOwnerAndNameAsTheIndexDoes(string? url, string? repository)
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

    [Theory]
    [InlineData("Studio/MyMod", "https://github.com/Studio/MyMod")]
    [InlineData("Studio/MyMod/extra", null)]
    [InlineData("not a repository", null)]
    [InlineData(null, null)]
    public void RepositoryUrl_LinksOnlyARepositoryName(string? repository, string? url)
    {
        Assert.Equal(url, new ListingOwnership(ListingOwnershipState.NotVerified, Repository: repository).RepositoryUrl?.AbsoluteUri);
    }

    [Theory]
    [InlineData("4253", "https://spacedock.info/mod/4253")]
    [InlineData("-5", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void SpaceDockModUrl_LinksOnlyAModNumber(string? mod, string? url)
    {
        Assert.Equal(url, new ListingOwnership(ListingOwnershipState.NotVerified, SpaceDockMod: mod).SpaceDockModUrl?.AbsoluteUri);
    }
}
