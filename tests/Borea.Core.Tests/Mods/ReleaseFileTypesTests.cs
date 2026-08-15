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

    [Fact]
    public void Constructor_AbsentTargetAndPath_LeaveTheTypeDefaultInForce()
    {
        var install = new InstallInfo("MyMod", derived: true);

        Assert.Null(install.Target);
        Assert.Null(install.Path);
    }

    [Fact]
    public void Constructor_AbsentRoot_MeansTheArchiveRoot()
    {
        var install = new InstallInfo(null, derived: true, InstallAnchor.Standalone);

        Assert.Null(install.Root);
        Assert.Equal(InstallAnchor.Standalone, install.Target);
    }

    [Fact]
    public void Constructor_TargetAndPath_AreCarried()
    {
        var install = new InstallInfo("build/Mod", derived: false, InstallAnchor.UserData, "Vehicles");

        Assert.Equal("build/Mod", install.Root);
        Assert.Equal(InstallAnchor.UserData, install.Target);
        Assert.Equal("Vehicles", install.Path);
    }

    [Fact]
    public void Constructor_AnchorThisBuildDoesNotKnow_IsCarriedRatherThanRejected()
    {
        // The entry still has to survive; only the guess is forbidden.
        var install = new InstallInfo("MyMod", derived: true, InstallAnchor.Unknown);

        Assert.Equal(InstallAnchor.Unknown, install.Target);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("nested/../../escape")]
    [InlineData("/absolute")]
    [InlineData("\\absolute")]
    [InlineData("~/home")]
    [InlineData("C:\\Windows")]
    [InlineData("C:relative")]
    // A separator on Windows, a directory name on Linux, so RFC 0035 rule 2
    // fixes it to '/'.
    [InlineData("build\\Mod")]
    public void Constructor_EscapingOrRootedRoot_ThrowsArgumentException(string root)
    {
        Assert.Throws<ArgumentException>(() => new InstallInfo(root, derived: false));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("nested/../../escape")]
    [InlineData("/absolute")]
    [InlineData("\\absolute")]
    [InlineData("~/home")]
    [InlineData("C:\\Windows")]
    [InlineData("C:relative")]
    [InlineData("build\\Mod")]
    public void Constructor_EscapingOrRootedPath_ThrowsArgumentException(string path)
    {
        Assert.Throws<ArgumentException>(() =>
            new InstallInfo("MyMod", derived: false, InstallAnchor.UserData, path));
    }

    [Fact]
    public void Constructor_NestedRelativeRoot_IsAccepted()
    {
        // The one archive layout RFC 0031 needs an authored root for.
        var install = new InstallInfo("build/AdvancedFlightComputer", derived: false);

        Assert.Equal("build/AdvancedFlightComputer", install.Root);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyRoot_ThrowsArgumentException(string root)
    {
        // Absent means the archive root; empty is a stamp that lost its value.
        Assert.Throws<ArgumentException>(() => new InstallInfo(root, derived: false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyPath_ThrowsArgumentException(string path)
    {
        Assert.Throws<ArgumentException>(() =>
            new InstallInfo("MyMod", derived: false, InstallAnchor.UserData, path));
    }
}
