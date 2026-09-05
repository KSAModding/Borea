using Borea.Core.Dependencies;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;

namespace Borea.Network.Tests.Temp;

internal static class TestFixtures
{
    public static ModMetadata SampleModMetadata(
        string modId, string source, string name = "Test Mod") =>
        new(
            specVersion: 1,
            modId: modId,
            source: source,
            name: name,
            authors: new[] { "Test Author" },
            abstractText: "A mod used for testing.",
            license: "MIT",
            links: new Dictionary<string, string> { ["forums"] = "https://forums.example/thread/1" },
            gameMin: "2026.7.4.2131");

    public static ModVersionMetadata SampleRelease(
        string modId, string version = "1.0.0", string? source = null) =>
        new(
            specVersion: 1,
            modId: modId,
            version: ModVersion.Parse(version),
            releaseStatus: ReleaseStatus.Stable,
            releaseDate: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
            gameMin: "2026.7.4.2131",
            gameMinRevision: 2131,
            download: new DownloadInfo("https://example.com/mod.zip", new string('A', 64), 1024, "application/zip"),
            installSizeBytes: 2048,
            dependencies: Array.Empty<ModDependency>(),
            source: source);

    /// <summary>
    /// A listing with every optional field populated, for field-by-field copy assertions.
    /// </summary>
    public static ModMetadata FullModMetadata(string modId = "mod-full", string source = "original") =>
        new(
            specVersion: 1,
            modId: modId,
            source: source,
            name: "Full Mod",
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
            description: "A longer description.",
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
                new ModDependency("cool-lib", ModDependencyKind.Required, ModVersion.Parse("1.0.0")),
                ModDependency.OfAlternatives(ModDependencyKind.Recommends, new[]
                {
                    new ModDependencyAlternative("audio-a", ModVersion.Parse("2.0.0")),
                    new ModDependencyAlternative("audio-b"),
                }),
            },
            install: new InstallDescriptor(root: "UnusualRoot"));

    /// <summary>
    /// A release with every optional field populated, including yanked.
    /// </summary>
    public static ModVersionMetadata FullRelease(string modId = "mod-full", string source = "original") =>
        new(
            specVersion: 1,
            modId: modId,
            version: ModVersion.Parse("1.2.0-beta.1"),
            releaseStatus: ReleaseStatus.Testing,
            releaseDate: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
            gameMin: "2026.7.4.2131",
            gameMinRevision: 2131,
            download: new DownloadInfo(
                "https://example.com/mod.zip", new string('A', 64), 1024, "application/zip",
                new[] { "https://mirror.example/mod.zip" }),
            installSizeBytes: 2048,
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
                "Full Mod",
                new[] { "Author A" },
                "Abstract at stamp time.",
                "MIT",
                new[] { "parts" },
                new Dictionary<string, string> { ["forums"] = "https://forums.example/thread/1" },
                "Description at stamp time."),
            yanked: true,
            yankedReason: "Broken above revision 5117.",
            source: source);
}
