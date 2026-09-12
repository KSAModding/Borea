using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;

namespace Borea.Cli.Tests;

internal static class LoaderFixtures
{
    public static ModMetadata Listing(string id = "StarMap", string launch = "StarMap.exe") => new(
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
        install: new InstallDescriptor(target: InstallAnchor.Standalone),
        provides: new LoaderProvides(
            launch: launch,
            configure: new LoaderConfigure("StarMapConfig.json", ConfigureFormat.Json, "GameLocation")));

    public static ModVersionMetadata Release(string id = "StarMap", string version = "0.4.6", bool yanked = false) => new(
        specVersion: 1,
        modId: id,
        version: ModVersion.Parse(version),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
        gameMin: "2026.9.7.5402",
        gameMinRevision: 5402,
        download: new DownloadInfo("https://example.invalid/loader.zip", null, 100, "application/zip"),
        installSizeBytes: 200,
        dependencies: Array.Empty<ModDependency>(),
        type: ContentType.ModLoader,
        install: new InstallInfo(root: null, derived: true, target: InstallAnchor.Standalone),
        yanked: yanked,
        source: "index");
}
