using Borea.Core.Index;
using Borea.Core.Mods;

namespace Borea.Core.Tests.Index;

public sealed class DownloadCountsTests
{
    [Fact]
    public void Constructor_ValidCounts_CopiesHostsInOrdinalOrderAndFindsReleases()
    {
        var hosts = new Dictionary<string, long> { ["spacedock"] = 721, ["github"] = 479 };
        var release = new ReleaseDownloadCounts(ModVersion.Parse("0.7.5"), 40, Hosts(("github", 17), ("spacedock", 23)));

        var counts = new ListingDownloadCounts(1200, hosts, new[] { release });
        hosts["github"] = 1;

        Assert.Equal(1200, counts.Total);
        Assert.Equal(["github", "spacedock"], counts.Hosts.Keys);
        Assert.Equal(479, counts.Hosts["github"]);
        Assert.Same(release, counts.FindRelease(ModVersion.Parse("0.7.5")));
        Assert.Null(counts.FindRelease(ModVersion.Parse("0.7.4")));
    }

    [Fact]
    public void Constructor_UnknownHostKey_KeepsTheValueInsideTheTotal()
    {
        var counts = new ListingDownloadCounts(15, Hosts(("github", 5), ("future-host", 10)), Array.Empty<ReleaseDownloadCounts>());

        Assert.Equal(10, counts.Hosts["future-host"]);
    }

    [Fact]
    public void Constructor_TotalIsNotTheSumOfHosts_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new ListingDownloadCounts(1000, Hosts(("github", 479), ("spacedock", 721)), Array.Empty<ReleaseDownloadCounts>()));

        Assert.Contains("not the sum", exception.Message);
    }

    [Fact]
    public void Constructor_NegativeTotal_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ReleaseDownloadCounts(ModVersion.Parse("1.0.0"), -1, Hosts(("github", -1))));
    }

    [Fact]
    public void Constructor_NegativeHostValue_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ReleaseDownloadCounts(ModVersion.Parse("1.0.0"), 0, Hosts(("github", 1), ("spacedock", -1))));
    }

    [Fact]
    public void Constructor_NoHosts_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ListingDownloadCounts(0, new Dictionary<string, long>(), Array.Empty<ReleaseDownloadCounts>()));
    }

    [Fact]
    public void Constructor_BlankHostKey_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ReleaseDownloadCounts(ModVersion.Parse("1.0.0"), 3, Hosts((" ", 3))));
    }

    [Fact]
    public void Constructor_HostValuesTooLargeToAdd_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new ListingDownloadCounts(long.MaxValue, Hosts(("github", long.MaxValue), ("spacedock", 1)), Array.Empty<ReleaseDownloadCounts>()));

        Assert.IsType<OverflowException>(exception.InnerException);
    }

    [Fact]
    public void Constructor_DuplicateReleaseVersion_Throws()
    {
        var first = new ReleaseDownloadCounts(ModVersion.Parse("1.0.0"), 1, Hosts(("github", 1)));
        var second = new ReleaseDownloadCounts(ModVersion.Parse("1.0.0"), 2, Hosts(("spacedock", 2)));

        Assert.Throws<ArgumentException>(() =>
            new ListingDownloadCounts(3, Hosts(("github", 1), ("spacedock", 2)), new[] { first, second }));
    }

    private static Dictionary<string, long> Hosts(params (string Host, long Count)[] values) =>
        values.ToDictionary(value => value.Host, value => value.Count, StringComparer.Ordinal);
}
