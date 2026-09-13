using Borea.Core.Dependencies;
using Borea.Core.Index;
using Borea.Core.ModPacks;
using Borea.Core.Mods;

namespace Borea.Cli.Tests;

internal static class ContentCommandFixtures
{
    public static ModMetadata Listing(
        string id = "flight-tools",
        string name = "Flight Tools",
        ModStatus status = ModStatus.Active,
        string? supersededBy = null) => new(
            specVersion: 1,
            modId: id,
            source: "index",
            name: name,
            authors: new[] { "Test Author" },
            abstractText: "Tools for orbital flight.",
            license: "MIT",
            links: new Dictionary<string, string>
            {
                ["forums"] = "https://forums.example/threads/flight-tools.1/",
                ["repository"] = "https://example.com/flight-tools",
            },
            gameMin: "2026.8.22.5348",
            tags: new[] { "utility" },
            description: "A longer description of the flight tools.",
            status: status,
            supersededBy: supersededBy);

    public static ModVersionMetadata Release(
        string id = "flight-tools",
        string version = "2.0.0",
        int gameMinRevision = 5348,
        int? gameMaxRevision = null,
        bool yanked = false,
        string? yankedReason = null,
        IReadOnlyList<ModDependency>? dependencies = null) => new(
            specVersion: 1,
            modId: id,
            version: ModVersion.Parse(version),
            releaseStatus: ReleaseStatus.Stable,
            releaseDate: new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
            gameMin: gameMinRevision == 5348 ? "2026.8.22.5348" : $"2026.1.1.{gameMinRevision}",
            gameMinRevision: gameMinRevision,
            download: new DownloadInfo("https://example.com/flight-tools.zip", new string('A', 64), 1024, "application/zip"),
            installSizeBytes: 2048,
            dependencies: dependencies ?? Array.Empty<ModDependency>(),
            gameMax: gameMaxRevision is null ? null : $"2026.1.1.{gameMaxRevision}",
            gameMaxRevision: gameMaxRevision,
            yanked: yanked,
            yankedReason: yankedReason,
            source: "index");

    public static ModPackMetadata PackVersion(
        string id = "navigation-pack",
        string version = "1.0.0",
        string name = "Navigation Pack",
        string gameMin = "2026.8.22.5348",
        string? gameMax = null,
        ModStatus status = ModStatus.Active,
        string? supersededBy = null,
        IReadOnlyList<ModPackEntry>? mods = null,
        IReadOnlyList<ModPackEntry>? vehicles = null,
        IReadOnlyList<ModPackEntry>? saves = null) => new(
            specVersion: 1,
            modPackId: id,
            source: "index",
            name: name,
            authors: new[] { "Pack Author" },
            abstractText: "Mods for maneuver planning, tested together.",
            license: "CC0-1.0",
            links: new Dictionary<string, string>
            {
                ["forums"] = "https://forums.example/threads/navigation-pack.2/",
            },
            gameMin: gameMin,
            version: ModVersion.Parse(version),
            releasedAt: new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero),
            mods: mods ?? new[] { new ModPackEntry("flight-tools", ModVersion.Parse("2.0.0")) },
            tags: new[] { "navigation" },
            status: status,
            supersededBy: supersededBy,
            gameMax: gameMax,
            changelog: "https://example.com/navigation-pack/changelog",
            vehicles: vehicles,
            saves: saves);

    public static ContentIndexPackVersion Retracted(ModPackMetadata version, string reason = "This pack version is broken.") =>
        new(version, new IndexStatus(IndexStatusState.Retracted, "retracted", reason: reason));
}
