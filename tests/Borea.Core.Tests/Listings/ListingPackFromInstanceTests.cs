using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Listings;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.State;
using static Borea.Core.Tests.ModPacks.PackMemberRepository;

namespace Borea.Core.Tests.Listings;

public sealed class ListingPackFromInstanceTests
{
    [Fact]
    public void Of_TwoIndexModsStarMapAndAManualMod_PinsTheIndexModsInLoadOrder_AndNamesStarMapAndTheManualMod()
    {
        var snapshot = Snapshot(Mod("Alpha", Release("Alpha", "1.0.0")), Mod("Beta", Release("Beta", "2.0.0")), Loader("StarMap"));
        var instance = Instance([Installed("Beta", "2.0.0", loader: "StarMap"), Installed("StarMap", "0.4.1", ContentType.ModLoader), Installed("alpha", "1.0.0")], ["LocalOnly"]);

        var pack = ListingPackFromInstance.Of(snapshot, instance, Enabled("StarMap", "Beta", "alpha"));

        Assert.Equal([new ListingPackMember("Beta", "2.0.0"), new ListingPackMember("Alpha", "1.0.0")], pack.Members);
        Assert.Equal(
            [new ListingLeftOutMod("StarMap", "0.4.1", ListingLeftOutReason.ModLoader), new ListingLeftOutMod("LocalOnly", null, ListingLeftOutReason.NotInstalledByBorea)],
            pack.LeftOut);
    }

    [Fact]
    public void Of_DisabledIndexMod_IsLeftOutAndNamed_AlsoWithoutAManifestEntry()
    {
        var snapshot = Snapshot(Mod("Alpha", Release("Alpha", "1.0.0")), Mod("Beta", Release("Beta", "2.0.0")), Mod("Gamma", Release("Gamma", "3.0.0")));
        var instance = Instance([Installed("Alpha", "1.0.0"), Installed("Beta", "2.0.0"), Installed("Gamma", "3.0.0")]);

        var pack = ListingPackFromInstance.Of(snapshot, instance, [new ModManifestEntry("Alpha", true), new ModManifestEntry("Beta", false)]);

        Assert.Equal([new ListingPackMember("Alpha", "1.0.0")], pack.Members);
        Assert.Equal(
            [new ListingLeftOutMod("Beta", "2.0.0", ListingLeftOutReason.Disabled), new ListingLeftOutMod("Gamma", "3.0.0", ListingLeftOutReason.Disabled)],
            pack.LeftOut);
    }

    [Fact]
    public void Of_InstalledReleaseYankedOrNotStamped_OrModNotListedOrDelisted_IsLeftOutAndNamed()
    {
        var snapshot = Snapshot(
            Mod("Yanked", Release("Yanked", "1.0.0", yanked: true), Release("Yanked", "0.9.0")),
            Mod("Unstamped", Release("Unstamped", "2.0.0")),
            Mod("Delisted", [Release("Delisted", "1.0.0")], new IndexStatus(IndexStatusState.Delisted, "delisted")));
        var instance = Instance([Installed("Yanked", "1.0.0"), Installed("Unstamped", "1.0.0"), Installed("Delisted", "1.0.0"), Installed("Unlisted", "1.0.0")]);

        var pack = ListingPackFromInstance.Of(snapshot, instance, Enabled("Yanked", "Unstamped", "Delisted", "Unlisted"));

        Assert.Empty(pack.Members);
        Assert.Equal(
            [
                new ListingLeftOutMod("Yanked", "1.0.0", ListingLeftOutReason.Yanked),
                new ListingLeftOutMod("Unstamped", "1.0.0", ListingLeftOutReason.ReleaseNotListed),
                new ListingLeftOutMod("Delisted", "1.0.0", ListingLeftOutReason.NotListed),
                new ListingLeftOutMod("Unlisted", "1.0.0", ListingLeftOutReason.NotListed),
            ],
            pack.LeftOut);
    }

    [Fact]
    public void Of_InstalledReleaseWhoseDownloadIsGone_IsLeftOutWithTheDate()
    {
        var gone = new DateTimeOffset(2026, 9, 23, 10, 24, 0, TimeSpan.Zero);
        var snapshot = Snapshot(Mod("Gone", Release("Gone", "1.0.0", unavailableSince: gone)), Mod("Alpha", Release("Alpha", "1.0.0")));
        var instance = Instance([Installed("Gone", "1.0.0"), Installed("Alpha", "1.0.0")]);

        var pack = ListingPackFromInstance.Of(snapshot, instance, Enabled("Gone", "Alpha"));

        Assert.Equal([new ListingPackMember("Alpha", "1.0.0")], pack.Members);
        Assert.Equal([new ListingLeftOutMod("Gone", "1.0.0", ListingLeftOutReason.DownloadGone, gone)], pack.LeftOut);
    }

    [Fact]
    public void Of_ModThatBoreaManagesAfterItsFilesMatchedARelease_IsAMember()
    {
        var snapshot = Snapshot(Mod("Alpha", Release("Alpha", "1.0.0")));
        var instance = Instance([Installed("Alpha", "1.0.0", ownership: ModInstallOwnership.Foreign)]);

        var pack = ListingPackFromInstance.Of(snapshot, instance, Enabled("Alpha"));

        Assert.Equal([new ListingPackMember("Alpha", "1.0.0")], pack.Members);
        Assert.Empty(pack.LeftOut);
    }

    [Fact]
    public void Of_LoaderThatTheSnapshotDoesNotList_IsStillALoader()
    {
        var instance = Instance([Installed("StarMap", "0.4.1", ContentType.ModLoader)]);

        var pack = ListingPackFromInstance.Of(Snapshot(), instance, Enabled("StarMap"));

        Assert.Equal([new ListingLeftOutMod("StarMap", "0.4.1", ListingLeftOutReason.ModLoader)], pack.LeftOut);
    }

    [Fact]
    public void Of_LoaderThatTheModsNeed_IsNamedOnce_WithTheVersionBoreaSetUp()
    {
        var snapshot = Snapshot(Mod("Alpha", Release("Alpha", "1.0.0")), Mod("Beta", Release("Beta", "2.0.0")), Loader("StarMap"));
        var instance = Instance([Installed("Alpha", "1.0.0", loader: "StarMap"), Installed("Beta", "2.0.0", loader: "starmap")], ["LocalOnly"]);
        var installations = new Dictionary<string, LoaderInstallation>
        {
            ["starmap"] = new(Path.Combine(Path.GetTempPath(), "StarMap"), ModVersion.Parse("0.4.6"), rawVersion: null, isAdopted: false),
        };

        var pack = ListingPackFromInstance.Of(snapshot, instance, Enabled("Alpha", "Beta"), installations);

        Assert.Equal([new ListingPackMember("Alpha", "1.0.0"), new ListingPackMember("Beta", "2.0.0")], pack.Members);
        Assert.Equal(
            [new ListingLeftOutMod("StarMap", "0.4.6", ListingLeftOutReason.ModLoader), new ListingLeftOutMod("LocalOnly", null, ListingLeftOutReason.NotInstalledByBorea)],
            pack.LeftOut);
    }

    [Fact]
    public void Of_LoaderThatTheModsNeed_WithoutAnInstallation_IsNamedWithoutAVersion()
    {
        var instance = Instance([Installed("Alpha", "1.0.0", loader: "StarMap")]);

        var pack = ListingPackFromInstance.Of(Snapshot(Mod("Alpha", Release("Alpha", "1.0.0"))), instance, Enabled("Alpha"));

        Assert.Equal([new ListingLeftOutMod("StarMap", null, ListingLeftOutReason.ModLoader)], pack.LeftOut);
    }

    [Fact]
    public void Of_ModThatTheSnapshotListsAsALoader_IsALoader()
    {
        var instance = Instance([Installed("StarMap", "0.4.1")]);

        var pack = ListingPackFromInstance.Of(Snapshot(Loader("StarMap")), instance, Enabled("StarMap"));

        Assert.Equal([new ListingLeftOutMod("StarMap", "0.4.1", ListingLeftOutReason.ModLoader)], pack.LeftOut);
    }

    [Fact]
    public void Of_RecordedVehiclesAndSaves_PinsTheUnchangedOnes_AndNamesTheChangedAndUnrecordedOnes()
    {
        var rover = Content("Rover", ContentType.Vehicle, "1.0.0");
        var moon = Content("MoonBase", ContentType.Save, "2.0.0");
        var changed = Content("Buggy", ContentType.Vehicle, "1.0.0");
        var snapshot = new ContentIndexSnapshot(1, [], [], null, [], vehicles: [Listed(rover), Listed(changed)], saves: [Listed(moon)]);
        var content = new[]
        {
            new ListingInstalledContent(ContentType.Vehicle, "Rover", Record(rover), Changed: false),
            new ListingInstalledContent(ContentType.Vehicle, "Buggy", Record(changed), Changed: true),
            new ListingInstalledContent(ContentType.Save, "Moon Base", Record(moon, "Moon Base"), Changed: false),
            new ListingInstalledContent(ContentType.Vehicle, "My Own", null, Changed: false),
        };

        var pack = ListingPackFromInstance.Of(snapshot, Instance([]), [], content: content);

        Assert.Equal([new ListingPackMember("Rover", "1.0.0") { Type = ContentType.Vehicle }], pack.Vehicles);
        Assert.Equal([new ListingPackMember("MoonBase", "2.0.0") { Type = ContentType.Save }], pack.Saves);
        Assert.Empty(pack.Members);
        Assert.Equal(
            [new ListingLeftOutMod("Buggy", "1.0.0", ListingLeftOutReason.ChangedSinceInstall, Type: ContentType.Vehicle), new ListingLeftOutMod("My Own", null, ListingLeftOutReason.NotInstalledByBorea, Type: ContentType.Vehicle)],
            pack.LeftOut);
    }

    private static ModVersionMetadata Content(string id, ContentType type, string version) =>
        new(1, id, ModVersion.Parse(version), ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, "2026.10.19.5638", 5638, new DownloadInfo($"https://example.com/{id}.zip", new string('A', 64), 1, "application/zip"), 1, [], type, install: new InstallInfo(null, derived: true, folder: id));

    private static ContentIndexListing Listed(ModVersionMetadata release) =>
        new(release.ModId, new ModMetadata(1, release.ModId, "index", release.ModId, ["Author"], "Abstract.", "MIT", new Dictionary<string, string>(), null, release.Type), [release], null);

    private static InstalledContentRecord Record(ModVersionMetadata release, string? folder = null) =>
        new(release, folder ?? release.ModId, DateTimeOffset.UnixEpoch, new Dictionary<string, string>());

    private static ContentIndexSnapshot Snapshot(params ContentIndexListing[] listings) => new(1, listings, [], null, []);

    private static ContentIndexListing Mod(string id, params ModVersionMetadata[] releases) => new(id, Listing(id, id), releases, null);

    private static ContentIndexListing Mod(string id, ModVersionMetadata[] releases, IndexStatus status) => new(id, Listing(id, id), releases, status);

    private static ContentIndexListing Loader(string id) => new(
        id,
        new ModMetadata(1, id, "index", id, ["Maxi"], id + " abstract.", "MIT", new Dictionary<string, string> { ["forums"] = $"https://forums.example/{id}" }, "2026.7.4.2131", ContentType.ModLoader),
        [Release(id, "0.4.1")],
        null);

    private static InstalledMod Installed(string id, string version, ContentType type = ContentType.Mod, ModInstallOwnership ownership = ModInstallOwnership.Borea, string? loader = null)
    {
        var release = Release(id, version);
        var requirement = loader is null ? null : new LoaderRequirement(loader, ModVersion.Parse("0.4.0"), maxVersion: null);
        var metadata = new ModVersionMetadata(1, id, release.Version, release.ReleaseStatus, release.ReleaseDate, release.GameMin, release.GameMinRevision, release.Download, 1, [], type, loader: requirement);
        return new InstalledMod(id, release.Version, InstallReason.Manual, DateTimeOffset.UnixEpoch, metadata, ownership: ownership, ownershipToken: ownership == ModInstallOwnership.Borea ? "token" : null);
    }

    private static Instance Instance(IReadOnlyList<InstalledMod> mods, IReadOnlyList<string>? foreignFolders = null) => Borea.Core.Instances.Instance.FromExisting(
        Guid.NewGuid(), "Main", InstanceSource.Custom.Value, DateTimeOffset.UnixEpoch, mods, (foreignFolders ?? []).Select(folder => new ForeignMod(folder)).ToList(), isFavorite: false);

    private static List<ModManifestEntry> Enabled(params string[] ids) => ids.Select(id => new ModManifestEntry(id, true)).ToList();
}
