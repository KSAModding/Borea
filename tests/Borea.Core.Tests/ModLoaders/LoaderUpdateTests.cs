using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;

namespace Borea.Core.Tests.ModLoaders;

public sealed class LoaderUpdateTests
{
    private static readonly GameVersion Build5602 = GameVersion.Parse("2026.10.13.5602");

    private static readonly GameVersion Build5638 = GameVersion.Parse("2026.10.19.5638");

    [Fact]
    public async Task InstalledReleaseEndsBeforeTheGame_OffersTheReleaseThatSupportsIt()
    {
        var mods = new FakeRepository(Release("0.4.6", max: "2026.10.13.5602"), Release("0.4.7", max: "2026.10.13.5602"), Release("0.4.8"));

        var update = await LoaderUpdate.FindAsync(mods, ReleaseChannel.Stable, "StarMap", Installed("0.4.7"), Build5638);

        Assert.Equal(GameCompatibility.Incompatible, update.InstalledCompatibility);
        Assert.Equal(ModVersion.Parse("0.4.8"), update.Offer!.Version);
        Assert.True(update.IsUrgent);
    }

    [Fact]
    public async Task NewerReleaseStartsAfterTheGame_IsNotOffered()
    {
        var mods = new FakeRepository(Release("0.4.7", max: "2026.10.13.5602"), Release("0.4.8", min: "2026.10.19.5638"));

        var update = await LoaderUpdate.FindAsync(mods, ReleaseChannel.Stable, "StarMap", Installed("0.4.7"), Build5602);

        Assert.Equal(GameCompatibility.Compatible, update.InstalledCompatibility);
        Assert.Null(update.Offer);
        Assert.False(update.IsUrgent);
    }

    [Fact]
    public async Task NewerReleaseThatFits_IsOfferedWithoutUrgency()
    {
        var mods = new FakeRepository(Release("0.4.7"), Release("0.4.8"));

        var update = await LoaderUpdate.FindAsync(mods, ReleaseChannel.Stable, "StarMap", Installed("0.4.7"), Build5602);

        Assert.Equal(GameCompatibility.Compatible, update.InstalledCompatibility);
        Assert.Equal(ModVersion.Parse("0.4.8"), update.Offer!.Version);
        Assert.False(update.IsUrgent);
    }

    [Fact]
    public async Task NewestReleaseDoesNotFit_OffersTheNewestThatDoes()
    {
        var mods = new FakeRepository(Release("0.4.6", max: "2026.10.13.5602"), Release("0.4.7"), Release("0.4.8", min: "2026.10.19.5638"));

        var update = await LoaderUpdate.FindAsync(mods, ReleaseChannel.Stable, "StarMap", Installed("0.4.6"), GameVersion.Parse("2026.10.14.5610"));

        Assert.Equal(ModVersion.Parse("0.4.7"), update.Offer!.Version);
        Assert.True(update.IsUrgent);
    }

    [Fact]
    public async Task InstalledReleaseNewerThanTheGame_OffersNoDowngrade()
    {
        var mods = new FakeRepository(Release("0.4.7"), Release("0.4.8", min: "2026.10.19.5638"));

        var update = await LoaderUpdate.FindAsync(mods, ReleaseChannel.Stable, "StarMap", Installed("0.4.8"), Build5602);

        Assert.Equal(GameCompatibility.Incompatible, update.InstalledCompatibility);
        Assert.Null(update.Offer);
        Assert.False(update.IsUrgent);
    }

    [Fact]
    public async Task UnknownInstalledVersion_OffersTheNewestThatFitsWithoutUrgency()
    {
        var mods = new FakeRepository(Release("0.4.7", max: "2026.10.13.5602"), Release("0.4.8"));

        var update = await LoaderUpdate.FindAsync(mods, ReleaseChannel.Stable, "StarMap", new LoaderInstallation("C:/StarMap", null, null, isAdopted: true), Build5638);

        Assert.Null(update.InstalledVersion);
        Assert.Equal(GameCompatibility.Unknown, update.InstalledCompatibility);
        Assert.Equal(ModVersion.Parse("0.4.8"), update.Offer!.Version);
        Assert.False(update.IsUrgent);
    }

    [Fact]
    public async Task NotInstalled_OffersTheNewestThatFits()
    {
        var mods = new FakeRepository(Release("0.4.7"), Release("0.4.8", min: "2026.10.19.5638"));

        var update = await LoaderUpdate.FindAsync(mods, ReleaseChannel.Stable, "StarMap", installation: null, Build5602);

        Assert.Equal(ModVersion.Parse("0.4.7"), update.Offer!.Version);
        Assert.False(update.IsUrgent);
    }

    [Fact]
    public async Task UnknownGame_OffersTheNewestOfTheChannel()
    {
        var mods = new FakeRepository(Release("0.4.7"), Release("0.4.8", min: "2026.10.19.5638"));

        var update = await LoaderUpdate.FindAsync(mods, ReleaseChannel.Stable, "StarMap", Installed("0.4.7"), game: null);

        Assert.Equal(GameCompatibility.Unknown, update.InstalledCompatibility);
        Assert.Equal(ModVersion.Parse("0.4.8"), update.Offer!.Version);
        Assert.False(update.IsUrgent);
    }

    [Fact]
    public async Task SkipsYankedGoneAndOutOfChannelReleases()
    {
        var mods = new ReleaseChannelModRepository(
            new FakeRepository(
                Release("0.4.7", max: "2026.10.13.5602"),
                Release("0.4.8"),
                Release("0.4.9", yanked: true),
                Release("0.4.10", gone: true),
                Release("0.5.0-beta.1", status: ReleaseStatus.Testing)),
            ReleaseChannel.Dev);

        var update = await LoaderUpdate.FindAsync(mods, ReleaseChannel.Stable, "StarMap", Installed("0.4.7"), Build5638);

        Assert.Equal(ModVersion.Parse("0.4.8"), update.Offer!.Version);
    }

    private static LoaderInstallation Installed(string version) =>
        new("C:/StarMap", ModVersion.Parse(version), version, isAdopted: false);

    private static ModVersionMetadata Release(string version, string min = "2026.8.3.5117", string? max = null, ReleaseStatus status = ReleaseStatus.Stable, bool yanked = false, bool gone = false) => new(
        1,
        "StarMap",
        ModVersion.Parse(version),
        status,
        DateTimeOffset.UnixEpoch,
        min,
        GameVersion.Parse(min).Revision,
        new DownloadInfo("https://example.com/StarMap.zip", new string('A', 64), 1, "application/zip", unavailableSince: gone ? DateTimeOffset.UnixEpoch : null),
        1,
        Array.Empty<ModDependency>(),
        type: ContentType.ModLoader,
        gameMax: max,
        gameMaxRevision: max is null ? null : GameVersion.Parse(max).Revision,
        yanked: yanked);

    /// <summary>Lists every release that is not yanked, and reads any release by its exact version.</summary>
    private sealed class FakeRepository(params ModVersionMetadata[] releases) : IModRepository
    {
        public Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModMetadata>>([]);

        public Task<ModVersionMetadata?> GetLatestReleaseAsync(string modId, CancellationToken cancellationToken = default)
            => Task.FromResult(releases.Where(value => value.IsOffered).OrderByDescending(value => value.Version).FirstOrDefault());

        public Task<ModVersionMetadata?> GetReleaseAsync(string modId, ModVersion version, CancellationToken cancellationToken = default)
            => Task.FromResult(releases.FirstOrDefault(value => value.Version == version));

        public Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModVersion>>(releases.Where(value => !value.Yanked).Select(value => value.Version).OrderByDescending(value => value).ToList());

        public Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModMetadata>>([]);
    }
}
