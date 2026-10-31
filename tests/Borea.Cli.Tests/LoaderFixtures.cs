using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;

namespace Borea.Cli.Tests;

internal static class LoaderFixtures
{
    /// <summary>A loader listing shaped like StarMap's, with the flag and the variable it takes an instance by.</summary>
    public static ModMetadata Listing(string id = "StarMap", string launch = "StarMap.exe", IReadOnlyList<string>? steps = null, IReadOnlyList<string>? uninstall = null) =>
        LoaderListing(id, launch, new InstanceHandover("-InstancePath", "STARMAP_INSTANCE_PATH"), steps: steps, uninstall: uninstall);

    /// <summary>A loader listing that does not say how the loader takes an instance.</summary>
    public static ModMetadata ListingWithoutInstance(string id = "OtherLoader", string launch = "StarMap.exe") =>
        LoaderListing(id, launch, instance: null);

    /// <summary>A StarMap-shaped listing that runs through <paramref name="runtime"/> on every platform.</summary>
    public static ModMetadata ListingWithRuntime(string runtime, string id = "StarMap") =>
        LoaderListing(
            id,
            "StarMap.exe",
            new InstanceHandover("-InstancePath", "STARMAP_INSTANCE_PATH"),
            Enum.GetValues<OsPlatform>().ToDictionary(platform => platform, _ => new LoaderPlatformLaunch("StarMap.dll", runtime)));

    private static ModMetadata LoaderListing(string id, string launch, InstanceHandover? instance, Dictionary<OsPlatform, LoaderPlatformLaunch>? platforms = null, IReadOnlyList<string>? steps = null, IReadOnlyList<string>? uninstall = null) => new(
        specVersion: 1,
        modId: id,
        source: "index",
        name: id,
        authors: new[] { "Loader author" },
        abstractText: "A mod loader.",
        license: "MIT",
        links: new Dictionary<string, string> { ["forums"] = "https://example.invalid/forums" },
        gameMin: "2026.9.7.5402",
        type: ContentType.ModLoader,
        install: new InstallDescriptor(target: InstallAnchor.Standalone, steps: steps, uninstall: uninstall),
        provides: new LoaderProvides(
            launch: launch,
            configure: new LoaderConfigure("StarMapConfig.json", ConfigureFormat.Json, "GameLocation"),
            instance: instance,
            platforms: platforms));

    public static ModVersionMetadata Release(string id = "StarMap", string version = "0.4.6", bool yanked = false, ReleaseStatus releaseStatus = ReleaseStatus.Stable, DateTimeOffset? unavailableSince = null, string gameMin = "2026.9.7.5402", string? gameMax = null) => new(
        specVersion: 1,
        modId: id,
        version: ModVersion.Parse(version),
        releaseStatus: releaseStatus,
        releaseDate: new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
        gameMin: gameMin,
        gameMinRevision: GameVersion.Parse(gameMin).Revision,
        download: new DownloadInfo("https://example.invalid/loader.zip", null, 100, "application/zip", unavailableSince: unavailableSince),
        installSizeBytes: 200,
        dependencies: Array.Empty<ModDependency>(),
        type: ContentType.ModLoader,
        install: new InstallInfo(root: null, derived: true, target: InstallAnchor.Standalone),
        gameMax: gameMax,
        gameMaxRevision: gameMax is null ? null : GameVersion.Parse(gameMax).Revision,
        yanked: yanked,
        source: "index");
}
