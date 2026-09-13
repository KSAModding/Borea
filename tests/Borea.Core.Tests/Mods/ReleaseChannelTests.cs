using Borea.Core.Dependencies;
using Borea.Core.Mods;

namespace Borea.Core.Tests.Mods;

public sealed class ReleaseChannelTests
{
    [Theory]
    [InlineData(ReleaseChannel.Stable, ReleaseStatus.Stable, true)]
    [InlineData(ReleaseChannel.Stable, ReleaseStatus.Testing, false)]
    [InlineData(ReleaseChannel.Stable, ReleaseStatus.Dev, false)]
    [InlineData(ReleaseChannel.Stable, ReleaseStatus.Unknown, false)]
    [InlineData(ReleaseChannel.Testing, ReleaseStatus.Stable, true)]
    [InlineData(ReleaseChannel.Testing, ReleaseStatus.Testing, true)]
    [InlineData(ReleaseChannel.Testing, ReleaseStatus.Dev, false)]
    [InlineData(ReleaseChannel.Testing, ReleaseStatus.Unknown, false)]
    [InlineData(ReleaseChannel.Dev, ReleaseStatus.Stable, true)]
    [InlineData(ReleaseChannel.Dev, ReleaseStatus.Testing, true)]
    [InlineData(ReleaseChannel.Dev, ReleaseStatus.Dev, true)]
    [InlineData(ReleaseChannel.Dev, ReleaseStatus.Unknown, true)]
    public void Includes_FollowsTheChannelOrder_AndCountsAnUnknownStatusAsDev(ReleaseChannel channel, ReleaseStatus status, bool expected)
    {
        Assert.Equal(expected, channel.Includes(status));
    }

    [Theory]
    [InlineData(ReleaseChannel.Stable, "stable")]
    [InlineData(ReleaseChannel.Testing, "testing")]
    [InlineData(ReleaseChannel.Dev, "dev")]
    public void ToName_AndTryParse_RoundTrip(ReleaseChannel channel, string name)
    {
        Assert.Equal(name, channel.ToName());
        Assert.True(ReleaseChannels.TryParse(name, out var parsed));
        Assert.Equal(channel, parsed);
    }

    [Fact]
    public void TryParse_IgnoresCase()
    {
        Assert.True(ReleaseChannels.TryParse("Testing", out var parsed));
        Assert.Equal(ReleaseChannel.Testing, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" stable")]
    [InlineData("nightly")]
    [InlineData("unknown")]
    public void TryParse_AnythingElse_IsNotAChannel(string? name)
    {
        Assert.False(ReleaseChannels.TryParse(name, out _));
    }

    [Fact]
    public void ToName_UndefinedChannel_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((ReleaseChannel)42).ToName());
    }

    [Theory]
    [InlineData(ReleaseChannel.Stable, "1.0.0")]
    [InlineData(ReleaseChannel.Testing, "1.1.0-beta.1")]
    [InlineData(ReleaseChannel.Dev, "1.2.0-dev.1")]
    public async Task GetLatestReleaseInChannelAsync_ReturnsTheNewestReleaseTheChannelOffers(ReleaseChannel channel, string expected)
    {
        var repository = new FakeRepository(
            Release("1.0.0"),
            Release("1.1.0-beta.1", ReleaseStatus.Testing),
            Release("1.2.0-dev.1", ReleaseStatus.Dev));

        var latest = await repository.GetLatestReleaseInChannelAsync("A", channel);

        Assert.Equal(ModVersion.Parse(expected), latest!.Version);
    }

    [Theory]
    [InlineData(ReleaseChannel.Stable, "1.0.0")]
    [InlineData(ReleaseChannel.Testing, "1.0.0")]
    [InlineData(ReleaseChannel.Dev, "2.0.0")]
    public async Task GetLatestReleaseInChannelAsync_UnknownStatus_IsOfferedOnlyOnDev(ReleaseChannel channel, string expected)
    {
        var repository = new FakeRepository(Release("1.0.0"), Release("2.0.0", ReleaseStatus.Unknown));

        var latest = await repository.GetLatestReleaseInChannelAsync("A", channel);

        Assert.Equal(ModVersion.Parse(expected), latest!.Version);
    }

    [Fact]
    public async Task GetLatestReleaseInChannelAsync_SkipsAYankedReleaseInTheChannel()
    {
        var repository = new FakeRepository(
            Release("1.0.0"),
            Release("1.1.0", yanked: true),
            Release("1.2.0-dev.1", ReleaseStatus.Dev));

        var latest = await repository.GetLatestReleaseInChannelAsync("A", ReleaseChannel.Stable);

        Assert.Equal(ModVersion.Parse("1.0.0"), latest!.Version);
    }

    [Fact]
    public async Task GetLatestReleaseInChannelAsync_NoReleaseInTheChannel_ReturnsNull()
    {
        var repository = new FakeRepository(Release("1.0.0-dev.1", ReleaseStatus.Dev));

        Assert.Null(await repository.GetLatestReleaseInChannelAsync("A", ReleaseChannel.Testing));
    }

    [Fact]
    public async Task GetLatestReleaseInChannelAsync_NewestReleaseInTheChannel_ReadsNoFurtherRelease()
    {
        var repository = new FakeRepository(Release("1.0.0"), Release("2.0.0"));

        var latest = await repository.GetLatestReleaseInChannelAsync("A", ReleaseChannel.Stable);

        Assert.Equal(ModVersion.Parse("2.0.0"), latest!.Version);
        Assert.Equal(0, repository.ReleaseReads);
        Assert.Equal(0, repository.VersionReads);
    }

    [Fact]
    public async Task GetLatestReleaseInChannelAsync_ThroughARepositoryOfANarrowerChannel_SeesTheWiderChannel()
    {
        var inner = new FakeRepository(Release("1.0.0"), Release("2.0.0-dev.1", ReleaseStatus.Dev));
        var stable = new ReleaseChannelModRepository(inner, ReleaseChannel.Stable);

        var latest = await stable.GetLatestReleaseInChannelAsync("A", ReleaseChannel.Dev);

        Assert.Equal(ModVersion.Parse("2.0.0-dev.1"), latest!.Version);
    }

    [Fact]
    public async Task Repository_LatestReleaseFollowsTheChannel_AndEveryOtherReadIsUnchanged()
    {
        var dev = Release("2.0.0-dev.1", ReleaseStatus.Dev);
        var inner = new FakeRepository(Release("1.0.0"), dev);
        var repository = new ReleaseChannelModRepository(inner, ReleaseChannel.Stable);

        var latest = await repository.GetLatestReleaseAsync("A");
        var versions = await repository.GetAvailableVersionsAsync("A");
        var exact = await repository.GetReleaseAsync("A", dev.Version);
        await repository.GetListingAsync("A");

        Assert.Equal(ModVersion.Parse("1.0.0"), latest!.Version);
        Assert.Equal([dev.Version, ModVersion.Parse("1.0.0")], versions);
        Assert.Same(dev, exact);
        Assert.Equal(1, inner.ListingReads);
    }

    [Fact]
    public void Repository_UndefinedChannel_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReleaseChannelModRepository(new FakeRepository(), (ReleaseChannel)42));
    }

    private static ModVersionMetadata Release(string version, ReleaseStatus status = ReleaseStatus.Stable, bool yanked = false) => new(
        1,
        "A",
        ModVersion.Parse(version),
        status,
        DateTimeOffset.UnixEpoch,
        "2026.7.4.2131",
        2131,
        new DownloadInfo("https://example.com/mod.zip", new string('A', 64), 1, "application/zip"),
        1,
        Array.Empty<ModDependency>(),
        yanked: yanked);

    /// <summary>Like the index repository: yanked releases are skipped except for an exact read.</summary>
    private sealed class FakeRepository(params ModVersionMetadata[] releases) : IModRepository
    {
        public int VersionReads { get; private set; }

        public int ReleaseReads { get; private set; }

        public int ListingReads { get; private set; }

        public Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModMetadata>>([]);

        public Task<ModMetadata?> GetListingAsync(string modId, CancellationToken cancellationToken = default)
        {
            ListingReads++;
            return Task.FromResult<ModMetadata?>(null);
        }

        public Task<ModVersionMetadata?> GetLatestReleaseAsync(string modId, CancellationToken cancellationToken = default)
            => Task.FromResult(releases.Where(value => !value.Yanked).OrderByDescending(value => value.Version).FirstOrDefault());

        public Task<ModVersionMetadata?> GetReleaseAsync(string modId, ModVersion version, CancellationToken cancellationToken = default)
        {
            ReleaseReads++;
            return Task.FromResult(releases.FirstOrDefault(value => value.Version == version));
        }

        public Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default)
        {
            VersionReads++;
            return Task.FromResult<IReadOnlyList<ModVersion>>(releases.Where(value => !value.Yanked).Select(value => value.Version).OrderByDescending(value => value).ToList());
        }

        public Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModMetadata>>([]);
    }
}
