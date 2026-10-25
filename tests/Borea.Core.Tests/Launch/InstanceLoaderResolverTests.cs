using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using static Borea.Core.Tests.ModLoaders.LoaderUpdateTests;
using static Borea.Core.Tests.ModLoaders.LoaderVersionChoiceTests;

namespace Borea.Core.Tests.Launch;

public sealed class InstanceLoaderResolverTests
{
    private static readonly GameVersion Build5602 = GameVersion.Parse("2026.10.13.5602");

    private static readonly FakeRepository Releases = new(Release("0.4.7"), Release("0.4.8", min: "2026.10.19.5638"));

    [Fact]
    public async Task PinnedVersionAndNothingInstalled_MissesThePinFromTheSetting()
    {
        var instance = InstanceWith();
        instance.SetLoader(LoaderSetting.Pinned("StarMap", ModVersion.Parse("0.4.7")));

        var resolution = await ResolveAsync(instance, []);

        Assert.Equal(LaunchLoaderFailure.GivenLoaderNotInstalled, resolution.Choice.Failure);
        Assert.Equal("StarMap", resolution.Loader?.ModId);
        Assert.True(resolution.FromSetting);
        Assert.True(resolution.Version!.IsPinned);
        Assert.Equal(ModVersion.Parse("0.4.7"), resolution.Version.Missing?.Version);
    }

    [Fact]
    public async Task PinnedVersionBesideAnotherInstalledVersion_SucceedsButDoesNotStart()
    {
        var instance = InstanceWith();
        instance.SetLoader(LoaderSetting.Pinned("StarMap", ModVersion.Parse("0.4.7")));

        var resolution = await ResolveAsync(instance, Installed(("0.4.8", true)));

        Assert.True(resolution.Choice.Succeeded);
        Assert.Null(resolution.Start);
        Assert.Equal(ModVersion.Parse("0.4.7"), resolution.Version!.Missing?.Version);
    }

    [Fact]
    public async Task AutomaticInstanceWithoutModsAndNothingInstalled_MissesTheNewestReleaseThatFits()
    {
        var resolution = await ResolveAsync(InstanceWith(), []);

        Assert.Equal(LaunchLoaderFailure.NoLoaderTakesInstance, resolution.Choice.Failure);
        Assert.Equal("StarMap", resolution.Loader?.ModId);
        Assert.False(resolution.FromSetting);
        Assert.Equal(ModVersion.Parse("0.4.7"), resolution.Version!.Missing?.Version);
        Assert.Equal(LoaderVersionProblem.NotInstalled, resolution.Version.Problem);
    }

    [Fact]
    public async Task StoredLoaderId_IsUsedAsTheGivenLoader()
    {
        var instance = InstanceWith(NeedsStarMap("flight-tools", "0.4.0"));
        instance.SetLoader(LoaderSetting.ForLoader("OtherLoader"));
        IReadOnlyList<InstalledLoaderVersion> installed = [.. Installed(("0.4.7", true)), new("OtherLoader", ModVersion.Parse("1.0.0"), "1.0.0", "OtherLoader", IsAdopted: false, IsRecorded: true)];
        var held = new ListingsRepository(() => [Listing(), Listing("OtherLoader")]);

        var resolution = await InstanceLoaderResolver.ResolveAsync(instance, installed, held, held, Releases, ReleaseChannel.Stable, Build5602);

        Assert.Equal("OtherLoader", resolution.Choice.Loader?.ModId);
        Assert.True(resolution.FromSetting);
        Assert.Same(installed[1], resolution.Version!.Start);
    }

    [Fact]
    public async Task HeldListings_AreReadBeforeTheSources()
    {
        var offline = new ListingsRepository(() => throw new HttpRequestException("SpaceDock is offline."));
        var live = new ListingsRepository(() => [Listing()]);

        var served = await InstanceLoaderResolver.ResolveAsync(InstanceWith(), Installed(("0.4.7", true)), new ListingsRepository(() => [Listing()]), offline, Releases, ReleaseChannel.Stable, Build5602);
        var lacking = await InstanceLoaderResolver.ResolveAsync(InstanceWith(NeedsStarMap("flight-tools", "0.4.0")), [], new ListingsRepository(() => []), live, Releases, ReleaseChannel.Stable, Build5602);

        Assert.Equal(ModVersion.Parse("0.4.7"), served.Version!.Start?.Version);
        Assert.Equal(0, offline.Reads);
        Assert.Equal(ModVersion.Parse("0.4.7"), lacking.Version!.Missing?.Version);
        Assert.Equal(1, live.Reads);
    }

    [Fact]
    public async Task UsedBy_TagsThePinnedAndTheAutomaticUser()
    {
        var pinned = InstanceWith();
        pinned.SetLoader(LoaderSetting.Pinned("StarMap", ModVersion.Parse("0.4.7")));
        var automatic = InstanceWith();
        var installed = Installed(("0.4.8", true), ("0.4.7", false));
        var held = new ListingsRepository(() => [Listing()]);
        var releases = new FakeRepository(Release("0.4.7"), Release("0.4.8"));

        var uses = await LoaderVersionUse.UsedByAsync([pinned, automatic], installed, held, held, releases, ReleaseChannel.Stable, _ => Build5602);

        Assert.Equal(new LoaderVersionUser(automatic, IsPinned: false), Assert.Single(uses[0].Users));
        Assert.Equal(new LoaderVersionUser(pinned, IsPinned: true), Assert.Single(uses[1].Users));
    }

    [Fact]
    public async Task UsedBy_KeepsThePinnedUserWhenTheLoaderIsNotListed()
    {
        var pinned = InstanceWith();
        pinned.SetLoader(LoaderSetting.Pinned("StarMap", ModVersion.Parse("0.4.7")));
        var unlisted = new ListingsRepository(() => []);

        var uses = await LoaderVersionUse.UsedByAsync([pinned], Installed(("0.4.7", true)), unlisted, unlisted, Releases, ReleaseChannel.Stable, _ => Build5602);

        Assert.Equal(new LoaderVersionUser(pinned, IsPinned: true), Assert.Single(Assert.Single(uses).Users));
    }

    private static Task<InstanceLoaderResolution> ResolveAsync(Instance instance, IReadOnlyList<InstalledLoaderVersion> installed)
    {
        var held = new ListingsRepository(() => [Listing()]);
        return InstanceLoaderResolver.ResolveAsync(instance, installed, held, held, Releases, ReleaseChannel.Stable, Build5602);
    }

    private static Instance InstanceWith(params InstalledMod[] mods) =>
        Instance.FromExisting(Guid.NewGuid(), "Main", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, mods, isFavorite: false);

    /// <summary>Serves the catalog that <paramref name="read"/> gives and counts the reads. Nothing else is read in these tests.</summary>
    private sealed class ListingsRepository(Func<IReadOnlyList<ModMetadata>> read) : IModRepository
    {
        public int Reads { get; private set; }

        public Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(read());
        }

        public Task<ModVersionMetadata?> GetLatestReleaseAsync(string modId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ModVersionMetadata?> GetReleaseAsync(string modId, ModVersion version, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
