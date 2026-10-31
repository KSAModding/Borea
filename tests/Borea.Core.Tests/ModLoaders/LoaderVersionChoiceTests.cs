using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Tests.Mods;
using static Borea.Core.Tests.ModLoaders.LoaderUpdateTests;

namespace Borea.Core.Tests.ModLoaders;

public sealed class LoaderVersionChoiceTests
{
    private static readonly GameVersion Build5602 = GameVersion.Parse("2026.10.13.5602");

    private static readonly GameVersion Build5638 = GameVersion.Parse("2026.10.19.5638");

    [Fact]
    public async Task OneInstalledVersionAndNoSetting_StartsItWithTheOfferOfToday()
    {
        var mods = new FakeRepository(Release("0.4.7"), Release("0.4.8"));
        var installed = Installed(("0.4.7", true));

        var resolution = await ResolveAsync(LoaderSetting.Automatic, installed, mods, Build5602);
        var today = await LoaderUpdate.FindAsync(mods, ReleaseChannel.Stable, "StarMap", new LoaderInstallation(installed[0].DirectoryPath, installed[0].Version, "0.4.7", isAdopted: false), Build5602);

        Assert.Same(installed[0], resolution.Start);
        Assert.False(resolution.IsPinned);
        Assert.Equal(today.Offer!.Version, resolution.Offer!.Version);
        Assert.Equal(GameCompatibility.Compatible, resolution.StartCompatibility);
        Assert.Equal(LoaderVersionProblem.None, resolution.Problem);
    }

    [Fact]
    public async Task Automatic_NewestInstalledIsIncompatibleOrOutsideTheRange_StartsTheOlderOne()
    {
        var installed = Installed(("0.4.8", true), ("0.4.7", false));

        var incompatible = await ResolveAsync(LoaderSetting.Automatic, installed, new FakeRepository(Release("0.4.7"), Release("0.4.8", min: "2026.10.19.5638")), Build5602);
        var outsideRange = await ResolveAsync(LoaderSetting.Automatic, installed, new FakeRepository(Release("0.4.7"), Release("0.4.8")), Build5602, Need(max: "0.4.7"));

        Assert.Equal(ModVersion.Parse("0.4.7"), incompatible.Start?.Version);
        Assert.Equal(LoaderVersionProblem.None, incompatible.Problem);
        Assert.Equal(ModVersion.Parse("0.4.7"), outsideRange.Start?.Version);
        Assert.Null(outsideRange.Offer);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Automatic_OnlyANewerVersionIsInstalled_OffersAnOlderReleaseOnlyForAKeptBuild(bool kept)
    {
        var mods = new FakeRepository(Release("0.4.7", max: "2026.10.13.5602"), Release("0.4.8", min: "2026.10.19.5638"));
        var installed = Installed(("0.4.8", true));

        var resolution = await ResolveAsync(LoaderSetting.Automatic, installed, mods, Build5602, gameIsKept: kept);

        Assert.Same(installed[0], resolution.Start);
        Assert.Equal(LoaderVersionProblem.GameIncompatible, resolution.Problem);
        Assert.Equal(kept ? ModVersion.Parse("0.4.7") : null, resolution.Offer?.Version);
        Assert.Equal(kept, resolution.IsUrgent);
    }

    [Fact]
    public async Task NothingInstalled_MissesTheNewestReleaseThatFits()
    {
        var mods = new FakeRepository(Release("0.4.7"), Release("0.4.8", min: "2026.10.19.5638"));

        var resolution = await ResolveAsync(LoaderSetting.Automatic, [], mods, Build5602);

        Assert.Null(resolution.Start);
        Assert.Equal(ModVersion.Parse("0.4.7"), resolution.Missing?.Version);
        Assert.Equal(LoaderVersionProblem.NotInstalled, resolution.Problem);
    }

    [Fact]
    public async Task PinThatIsNotInstalled_MissesThePin()
    {
        var mods = new FakeRepository(Release("0.4.7", max: "2026.10.13.5602"), Release("0.4.8"));

        var resolution = await ResolveAsync(Pin("0.4.7"), Installed(("0.4.8", true)), mods, Build5638);

        Assert.Null(resolution.Start);
        Assert.True(resolution.IsPinned);
        Assert.Equal(ModVersion.Parse("0.4.7"), resolution.Missing?.Version);
        Assert.Null(resolution.Offer);
        Assert.Equal(LoaderVersionProblem.NotInstalled, resolution.Problem);
    }

    [Fact]
    public async Task YankedPinThatIsNotInstalled_IsNotInstallable()
    {
        var mods = new FakeRepository(Release("0.4.7", yanked: true), Release("0.4.8"));

        var resolution = await ResolveAsync(Pin("0.4.7"), Installed(("0.4.8", true)), mods, Build5638);

        Assert.Null(resolution.Start);
        Assert.Null(resolution.Missing);
        Assert.Equal(LoaderVersionProblem.NotInstallable, resolution.Problem);
    }

    [Fact]
    public async Task InstalledPinThatIsIncompatible_StartsThePinWithoutAnOffer()
    {
        var mods = new FakeRepository(Release("0.4.7", max: "2026.10.13.5602"), Release("0.4.8"));
        var installed = Installed(("0.4.8", true), ("0.4.7", false));

        var resolution = await ResolveAsync(Pin("0.4.7"), installed, mods, Build5638);

        Assert.Same(installed[1], resolution.Start);
        Assert.True(resolution.IsPinned);
        Assert.Equal(GameCompatibility.Incompatible, resolution.StartCompatibility);
        Assert.Equal(LoaderVersionProblem.GameIncompatible, resolution.Problem);
        Assert.Null(resolution.Offer);
    }

    [Fact]
    public async Task Offline_StartsTheNewestInstalledVersionTheModsAccept()
    {
        var installed = Installed(("0.4.9", true), ("0.4.8", false), ("0.4.7", false));

        var resolution = await ResolveAsync(LoaderSetting.Automatic, installed, mods: null, Build5638, Need(max: "0.4.8"));

        Assert.Equal(ModVersion.Parse("0.4.8"), resolution.Start?.Version);
        Assert.Equal(GameCompatibility.Unknown, resolution.StartCompatibility);
        Assert.Null(resolution.Offer);
        Assert.Equal(LoaderVersionProblem.None, resolution.Problem);
    }

    [Fact]
    public async Task PrereleaseInstalledForAPin_AutomaticOnTheStableChannelStartsTheStableVersion()
    {
        var mods = new FakeRepository(Release("0.4.8"), Release("0.5.0-beta.1", status: ReleaseStatus.Testing));
        var installed = Installed(("0.5.0-beta.1", false), ("0.4.8", true));

        var online = await ResolveAsync(LoaderSetting.Automatic, installed, mods, Build5638);
        var offline = await ResolveAsync(LoaderSetting.Automatic, installed, mods: null, Build5638);

        Assert.Equal(ModVersion.Parse("0.4.8"), online.Start?.Version);
        Assert.Equal(ModVersion.Parse("0.4.8"), offline.Start?.Version);
    }

    internal static ModMetadata Listing(string loaderId = "StarMap") => new(
        specVersion: 1,
        modId: loaderId,
        source: "TestSource",
        name: loaderId,
        authors: ["Loader author"],
        abstractText: "A mod loader.",
        license: "MIT",
        links: TestFixtures.SampleLinks(),
        gameMin: "2026.7.4.2131",
        type: ContentType.ModLoader,
        provides: new LoaderProvides(launch: "StarMap.exe", instance: new InstanceHandover("-InstancePath", "STARMAP_INSTANCE_PATH")));

    /// <summary>StarMap versions in the given order, each in its own folder below the Loaders root.</summary>
    internal static IReadOnlyList<InstalledLoaderVersion> Installed(params (string Version, bool Recorded)[] versions) =>
        versions.Select(entry => new InstalledLoaderVersion("StarMap", ModVersion.Parse(entry.Version), entry.Version, Path.Combine("Loaders", "StarMap", entry.Version), IsAdopted: false, entry.Recorded)).ToList();

    private static LoaderSetting Pin(string version) => LoaderSetting.Pinned("StarMap", ModVersion.Parse(version));

    private static NeededLoader Need(string max) => NeededLoader.From("StarMap", [NeedsStarMap("flight-tools", "0.4.0", max)]);

    private static Task<LoaderVersionResolution> ResolveAsync(LoaderSetting setting, IReadOnlyList<InstalledLoaderVersion> installed, IModRepository? mods, GameVersion game, NeededLoader? need = null, bool gameIsKept = false) =>
        LoaderVersionChoice.ResolveAsync(setting, "StarMap", need, installed, [Listing()], mods, ReleaseChannel.Stable, game, gameIsKept);
}
