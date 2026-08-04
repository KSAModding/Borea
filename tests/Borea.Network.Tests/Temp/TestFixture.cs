using Borea.Core.Game;
using Borea.Core.Mods;

namespace Borea.Network.Tests.Temp;

internal static class TestFixtures
{
    public static ModMetadata SampleModMetadata(
        string modId, string source, string name = "Test Mod", string version = "1.0.0") =>
        new(
            modId,
            source,
            name,
            author: "Test Author",
            version: ModVersion.Parse(version),
            builtForGameVersion: GameVersion.Parse("2026.7.4.2131"),
            description: "Description.",
            releasedAt: DateTimeOffset.UtcNow,
            fileSizeBytes: 1024);
}
