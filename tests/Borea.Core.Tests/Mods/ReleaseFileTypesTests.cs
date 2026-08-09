using Borea.Core.Mods;

namespace Borea.Core.Tests.Mods;

public sealed class DownloadInfoTests
{
    [Fact]
    public void Constructor_NormalizesTheHashToUppercase()
    {
        var download = new DownloadInfo("https://example.com/mod.zip", new string('a', 64), 100, "application/zip");

        Assert.Equal(new string('A', 64), download.Sha256);
    }

    [Fact]
    public void HashMatches_ComparesCaseInsensitively()
    {
        var download = TestFixtures.SampleDownload();

        Assert.True(download.HashMatches(new string('a', 64)));
        Assert.False(download.HashMatches(new string('b', 64)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zz")]
    public void Constructor_InvalidHash_ThrowsArgumentException(string sha256)
    {
        Assert.Throws<ArgumentException>(() =>
            new DownloadInfo("https://example.com/mod.zip", sha256, 100, "application/zip"));
    }

    [Fact]
    public void Constructor_NegativeSize_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DownloadInfo("https://example.com/mod.zip", new string('A', 64), -1, "application/zip"));
    }
}

public sealed class ReleaseSourceTests
{
    [Fact]
    public void Constructor_SingleHost_IsItsOwnAuthority()
    {
        var source = new ReleaseSource(new[] { new ReleaseHost("github", "owner/repo") });

        Assert.Equal("github", source.Authority);
        Assert.Equal("owner/repo", source.AuthorityHost.Reference);
    }

    [Fact]
    public void Constructor_MultipleHostsWithoutAuthority_ThrowsArgumentException()
    {
        var hosts = new[] { new ReleaseHost("github", "owner/repo"), new ReleaseHost("spacedock", "4253") };

        Assert.Throws<ArgumentException>(() => new ReleaseSource(hosts));
    }

    [Fact]
    public void Constructor_AuthorityTakesTheCasingOfTheHostItNames()
    {
        var hosts = new[] { new ReleaseHost("github", "owner/repo"), new ReleaseHost("spacedock", "4253") };

        var source = new ReleaseSource(hosts, "GitHub");

        Assert.Equal("github", source.Authority);
    }

    [Fact]
    public void Constructor_AuthorityNamingNoHost_ThrowsArgumentException()
    {
        var hosts = new[] { new ReleaseHost("github", "owner/repo") };

        Assert.Throws<ArgumentException>(() => new ReleaseSource(hosts, "spacedock"));
    }

    [Fact]
    public void Constructor_DuplicateHostKeys_ThrowsArgumentException()
    {
        var hosts = new[] { new ReleaseHost("github", "a/b"), new ReleaseHost("GitHub", "c/d") };

        Assert.Throws<ArgumentException>(() => new ReleaseSource(hosts, "github"));
    }
}

public sealed class InstallInfoTests
{
    [Fact]
    public void Constructor_RelativeRoot_IsAccepted()
    {
        var install = new InstallInfo("MyMod", derived: true);

        Assert.Equal("MyMod", install.Root);
        Assert.True(install.Derived);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("nested/../../escape")]
    [InlineData("/absolute")]
    [InlineData("\\absolute")]
    [InlineData("C:\\Windows")]
    [InlineData("C:relative")]
    public void Constructor_EscapingOrRootedRoot_ThrowsArgumentException(string root)
    {
        Assert.Throws<ArgumentException>(() => new InstallInfo(root, derived: false));
    }
}
