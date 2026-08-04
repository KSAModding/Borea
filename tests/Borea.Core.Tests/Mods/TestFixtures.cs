using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Mods;

namespace Borea.Core.Tests.Mods;

/// <summary>
/// Shared sample-data builders for Core tests. Centralizes valid default
/// construction so a constructor signature change only needs updating here,
/// not in every test file that builds a sample mod.
/// </summary>
internal static class TestFixtures
{
    public static ModMetadata SampleModMetadata(
        string modId = "test-mod",
        string version = "1.0.0",
        string gameVersion = "2026.7.4.2131",
        IReadOnlyList<ModDependency>? dependencies = null) =>
        new(
            modId,
            source: "TestSource",
            name: "Test Mod",
            author: "Test Author",
            version: ModVersion.Parse(version),
            builtForGameVersion: GameVersion.Parse(gameVersion),
            description: "A mod used for testing.",
            releasedAt: DateTimeOffset.UtcNow,
            fileSizeBytes: 1024,
            dependencies: dependencies,
            tags: new[] { "test" });

    public static InstalledMod SampleInstalledMod(
        string modId = "test-mod",
        string version = "1.0.0",
        InstallReason reason = InstallReason.Manual) =>
        new(
            modId,
            ModVersion.Parse(version),
            reason,
            DateTimeOffset.UtcNow,
            SampleModMetadata(modId, version));
}
