using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Tests.Mods;

namespace Borea.Core.Tests.Launch;

public sealed class LaunchLoaderChoiceTests
{
    [Fact]
    public void Choose_GivenLoaderId_WinsOverTheLoaderTheModsNeed()
    {
        var instance = InstanceWith(NeedsLoader("flight-tools", "StarMap"));

        var choice = LaunchLoaderChoice.Choose(instance, Recorded("StarMap", "OtherLoader"), [Listing("StarMap"), Listing("OtherLoader")], "otherloader");

        Assert.True(choice.Succeeded);
        Assert.Equal("OtherLoader", choice.Loader.ModId);
        Assert.False(choice.RequiredByMods);
        Assert.Empty(choice.LoaderIds);
    }

    [Fact]
    public void Choose_GivenLoaderIdTheModsNeed_SaysTheModsRequireIt()
    {
        var instance = InstanceWith(NeedsLoader("flight-tools", "StarMap"));

        var choice = LaunchLoaderChoice.Choose(instance, Recorded("StarMap"), [Listing("StarMap")], "StarMap");

        Assert.Equal("StarMap", choice.Loader?.ModId);
        Assert.True(choice.RequiredByMods);
    }

    [Fact]
    public void Choose_GivenLoaderIdNotRecorded_NamesIt()
    {
        var instance = InstanceWith();

        var choice = LaunchLoaderChoice.Choose(instance, Recorded("StarMap"), [Listing("StarMap"), Listing("OtherLoader")], "OtherLoader");

        Assert.False(choice.Succeeded);
        Assert.Null(choice.Loader);
        Assert.Equal(LaunchLoaderFailure.GivenLoaderNotInstalled, choice.Failure);
        Assert.Equal(["OtherLoader"], choice.LoaderIds);
    }

    [Fact]
    public void Choose_ModsNeedOneRecordedLoader_UsesItAlsoInAnotherCase()
    {
        var instance = InstanceWith(NeedsLoader("flight-tools", "StarMap"), NeedsLoader("orbit-tools", "starmap"), TestFixtures.SampleInstalledMod("parts-pack"));

        var choice = LaunchLoaderChoice.Choose(instance, Recorded("OtherLoader", "STARMAP"), [Listing("OtherLoader"), Listing("StarMap")]);

        Assert.Equal(LaunchLoaderFailure.None, choice.Failure);
        Assert.Equal("StarMap", choice.Loader?.ModId);
        Assert.True(choice.RequiredByMods);
    }

    [Fact]
    public void Choose_ModsNeedALoaderThatIsNotRecorded_NamesIt()
    {
        var instance = InstanceWith(NeedsLoader("flight-tools", "StarMap"));

        var choice = LaunchLoaderChoice.Choose(instance, Recorded("OtherLoader"), [Listing("StarMap"), Listing("OtherLoader")]);

        Assert.Equal(LaunchLoaderFailure.NeededLoaderNotInstalled, choice.Failure);
        Assert.Equal(["StarMap"], choice.LoaderIds);
    }

    [Fact]
    public void Choose_ModsNeedDifferentLoaders_NamesThemInIdOrder()
    {
        var instance = InstanceWith(NeedsLoader("flight-tools", "StarMap"), NeedsLoader("orbit-tools", "otherLoader"));

        var choice = LaunchLoaderChoice.Choose(instance, Recorded("StarMap", "otherLoader"), [Listing("StarMap"), Listing("otherLoader")]);

        Assert.Equal(LaunchLoaderFailure.DifferentLoadersNeeded, choice.Failure);
        Assert.Equal(["otherLoader", "StarMap"], choice.LoaderIds);
    }

    [Fact]
    public void Choose_RecordedLoaderWithoutAListing_NamesIt()
    {
        var instance = InstanceWith(NeedsLoader("flight-tools", "StarMap"));

        var choice = LaunchLoaderChoice.Choose(instance, Recorded("StarMap"), [TestFixtures.SampleModMetadata("StarMap")]);

        Assert.Equal(LaunchLoaderFailure.LoaderNotListed, choice.Failure);
        Assert.Equal(["StarMap"], choice.LoaderIds);
    }

    [Fact]
    public void Choose_NoModNeedsALoader_UsesTheRecordedLoaderThatTakesAnInstance()
    {
        var instance = InstanceWith([TestFixtures.SampleInstalledMod("parts-pack")], [new ForeignMod("LocalOnly")]);

        var choice = LaunchLoaderChoice.Choose(
            instance,
            Recorded("Alpha", "Beta", "StarMap"),
            [Listing("Alpha", takesInstance: false), Listing("StarMap"), Listing("OtherLoader")]);

        Assert.Equal("StarMap", choice.Loader?.ModId);
        Assert.False(choice.RequiredByMods);
    }

    [Fact]
    public void Choose_NoModNeedsALoaderAndTwoLoadersTakeAnInstance_UsesTheFirstByIdIgnoringCase()
    {
        var instance = InstanceWith();

        var choice = LaunchLoaderChoice.Choose(instance, Recorded("StarMap", "anotherLoader"), [Listing("StarMap"), Listing("anotherLoader")]);

        Assert.Equal("anotherLoader", choice.Loader?.ModId);
        Assert.False(choice.RequiredByMods);
    }

    [Fact]
    public void Choose_NoModNeedsALoaderAndNoRecordedLoaderTakesAnInstance_Fails()
    {
        var instance = InstanceWith(TestFixtures.SampleInstalledMod("parts-pack"));

        var choice = LaunchLoaderChoice.Choose(instance, Recorded("Alpha"), [Listing("Alpha", takesInstance: false), Listing("StarMap")]);

        Assert.Equal(LaunchLoaderFailure.NoLoaderTakesInstance, choice.Failure);
        Assert.Null(choice.Loader);
        Assert.Empty(choice.LoaderIds);
    }

    [Fact]
    public void Choose_NoModNeedsALoaderAndOnlyUnlistedLoadersCouldTakeAnInstance_NamesThemInIdOrder()
    {
        var instance = InstanceWith(TestFixtures.SampleInstalledMod("parts-pack"));

        var choice = LaunchLoaderChoice.Choose(instance, Recorded("StarMap", "Alpha", "beta"), [Listing("Alpha", takesInstance: false)]);

        Assert.Equal(LaunchLoaderFailure.LoaderNotListed, choice.Failure);
        Assert.Equal(["beta", "StarMap"], choice.LoaderIds);
    }

    private static Instance InstanceWith(params InstalledMod[] mods) => InstanceWith(mods, []);

    private static Instance InstanceWith(IReadOnlyList<InstalledMod> mods, IReadOnlyList<ForeignMod> foreignMods) =>
        Instance.FromExisting(Guid.NewGuid(), "Main", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, mods, foreignMods, isFavorite: false);

    private static InstalledMod NeedsLoader(string modId, string loaderId)
    {
        var release = new ModVersionMetadata(
            specVersion: 1,
            modId: modId,
            version: ModVersion.Parse("1.0.0"),
            releaseStatus: ReleaseStatus.Stable,
            releaseDate: DateTimeOffset.UtcNow,
            gameMin: "2026.7.4.2131",
            gameMinRevision: 2131,
            download: TestFixtures.SampleDownload(),
            installSizeBytes: 2048,
            dependencies: [],
            loader: new LoaderRequirement(loaderId, ModVersion.Parse("0.4.0")));
        return new InstalledMod(modId, release.Version, InstallReason.Manual, DateTimeOffset.UtcNow, release);
    }

    private static Dictionary<string, LoaderInstallation> Recorded(params string[] loaderIds) =>
        loaderIds.ToDictionary(id => id, id => new LoaderInstallation(Path.Combine(Path.GetTempPath(), "Loaders", id), null, null, isAdopted: false), ModIds.Comparer);

    private static ModMetadata Listing(string loaderId, bool takesInstance = true) => new(
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
        provides: new LoaderProvides(
            launch: "StarMap.exe",
            instance: takesInstance ? new InstanceHandover("-InstancePath", "STARMAP_INSTANCE_PATH") : null));
}
