using Borea.Core.Dependencies;
using Borea.Core.Mods;

namespace Borea.Core.Tests.Mods;

/// <summary>
/// Shared sample-data builders for Core tests. Centralizes valid default
/// construction so a constructor signature change only needs updating here,
/// not in every test file that builds a sample mod.
/// </summary>
internal static class TestFixtures
{
    public static IReadOnlyDictionary<string, string> SampleLinks() =>
        new Dictionary<string, string> { ["forums"] = "https://forums.example/thread/1" };

    public static ModMetadata SampleModMetadata(
        string modId = "test-mod",
        IReadOnlyList<ModDependency>? dependencies = null) =>
        new(
            specVersion: 1,
            modId: modId,
            source: "TestSource",
            name: "Test Mod",
            authors: new[] { "Test Author" },
            abstractText: "A mod used for testing.",
            license: "MIT",
            links: SampleLinks(),
            gameMin: "2026.7.4.2131",
            dependencies: dependencies);

    public static DownloadInfo SampleDownload() =>
        new("https://example.com/mod.zip", new string('A', 64), 1024, "application/zip");

    public static ModVersionMetadata SampleVersionMetadata(
        string modId = "test-mod",
        string version = "1.0.0",
        IReadOnlyList<ModDependency>? dependencies = null) =>
        new(
            specVersion: 1,
            modId: modId,
            version: ModVersion.Parse(version),
            releaseStatus: ReleaseStatus.Stable,
            releaseDate: DateTimeOffset.UtcNow,
            gameMin: "2026.7.4.2131",
            gameMinRevision: 2131,
            download: SampleDownload(),
            installSizeBytes: 2048,
            dependencies: dependencies ?? Array.Empty<ModDependency>());

    public static InstalledMod SampleInstalledMod(
        string modId = "test-mod",
        string version = "1.0.0",
        InstallReason reason = InstallReason.Manual,
        IReadOnlyList<ModDependency>? dependencies = null) =>
        new(
            modId,
            ModVersion.Parse(version),
            reason,
            DateTimeOffset.UtcNow,
            SampleModMetadata(modId),
            dependencies ?? Array.Empty<ModDependency>());
}
