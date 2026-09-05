using Borea.Core.Dependencies;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;

namespace Borea.Storage.Tests.Mods;

/// <summary>
/// Sample metadata builders for the storage round-trip tests: a minimal shape
/// with every optional absent, and a full shape with every optional set.
/// </summary>
internal static class MetadataFixtures
{
    public static IReadOnlyDictionary<string, string> SampleLinks() =>
        new Dictionary<string, string> { ["forums"] = "https://forums.example/thread/1" };

    // Sub-second ticks on purpose, so a serializer that drops fractional
    // seconds fails the round-trip tests.
    public static DateTimeOffset SampleTimestamp() =>
        new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567);

    public static ModMetadata MinimalMetadata(string modId = "test-mod") => new(
        specVersion: 1,
        modId: modId,
        source: "TestSource",
        name: "Test Mod",
        authors: new[] { "Author" },
        abstractText: "Abstract.",
        license: "MIT",
        links: SampleLinks(),
        gameMin: "2026.7");

    public static ModMetadata FullMetadata(string modId = "test-mod") => new(
        specVersion: 1,
        modId: modId,
        source: "TestSource",
        name: "Test Mod",
        authors: new[] { "Author A", "Author B" },
        abstractText: "Abstract.",
        license: "MIT",
        links: new Dictionary<string, string>
        {
            ["forums"] = "https://forums.example/thread/1",
            ["repository"] = "https://example.com/repo",
        },
        gameMin: "2026.7.4.2131",
        tags: new[] { "parts", "tools" },
        description: "A longer CommonMark description.",
        status: ModStatus.Deprecated,
        supersededBy: "successor-mod",
        releases: new ReleaseSource(
            new[] { new ReleaseHost("github", "owner/repo"), new ReleaseHost("spacedock", "4253") },
            "github"),
        gameMax: "2026.8.3.5117",
        os: new[] { "windows", "linux" },
        loader: new LoaderRequirement("StarMap", ModVersion.Parse("0.4.5"), ModVersion.Parse("0.5.0")),
        dependencies: new[]
        {
            new ModDependency("cool-lib", ModDependencyKind.Required, ModVersion.Parse("1.0.0"), ModVersion.Parse("2.0.0")),
            new ModDependency("old-lib", ModDependencyKind.Conflict, ModVersion.Parse("3.0.0")),
            ModDependency.OfAlternatives(ModDependencyKind.Recommends, new[]
            {
                new ModDependencyAlternative("audio-a", ModVersion.Parse("2.0.0")),
                new ModDependencyAlternative("audio-b"),
            }),
        },
        install: new InstallDescriptor(
            root: "UnusualRoot",
            manages: new[] { "config/settings.json" },
            steps: new[] { "Relaunch the game once to enable it." },
            uninstall: Array.Empty<string>()));

    public static DownloadInfo SampleDownload() =>
        new("https://example.com/mod.zip", new string('A', 64), 1024, "application/zip", new[] { "https://mirror.example/mod.zip" });

    public static ModVersionMetadata MinimalRelease(string modId = "test-mod", string version = "1.0.0", IReadOnlyList<ModDependency>? dependencies = null) => new(
        specVersion: 1,
        modId: modId,
        version: ModVersion.Parse(version),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: SampleTimestamp(),
        gameMin: "2026.7",
        gameMinRevision: 2131,
        // Lowercase digest on purpose, so the uppercase normalization is
        // exercised by the round trip.
        download: new DownloadInfo("https://example.com/mod.zip", new string('b', 64), 512, "application/zip"),
        installSizeBytes: 2048,
        dependencies: dependencies ?? Array.Empty<ModDependency>());

    public static ModVersionMetadata FullRelease(string modId = "test-mod") => new(
        specVersion: 1,
        modId: modId,
        version: ModVersion.Parse("1.2.0-beta.1"),
        releaseStatus: ReleaseStatus.Testing,
        releaseDate: SampleTimestamp(),
        gameMin: "2026.7.4.2131",
        gameMinRevision: 2131,
        download: SampleDownload(),
        installSizeBytes: 4096,
        dependencies: new[]
        {
            new ModDependency("cool-lib", ModDependencyKind.Required, ModVersion.Parse("1.0.0"), source: MetadataSource.Authored),
            new ModDependency("kitten-ext", ModDependencyKind.Optional, source: MetadataSource.Derived),
        },
        gameMax: "2026.8.3.5117",
        gameMaxRevision: 5117,
        os: new[] { "windows" },
        install: new InstallInfo(modId, derived: true),
        loader: new LoaderRequirement("StarMap", ModVersion.Parse("0.4.5"), source: MetadataSource.Authored),
        changelog: "https://example.com/changelog",
        listing: new ListingSnapshot(
            "Test Mod",
            new[] { "Author" },
            "Abstract at stamp time.",
            "MIT",
            new[] { "parts" },
            SampleLinks(),
            "Description at stamp time."),
        yanked: true,
        yankedReason: "Broken above revision 5117.",
        source: "TestSource");
}
