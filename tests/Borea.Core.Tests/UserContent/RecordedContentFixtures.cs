using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.Core.Tests.UserContent;

internal static class RecordedContentFixtures
{
    public static ModVersionMetadata Release(ContentType type, string id, string folder, IReadOnlyList<ModDependency> dependencies, string version = "1.0.0") =>
        new(1, id, ModVersion.Parse(version), ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, "2026.10.19.5638", 5638, new DownloadInfo($"https://example.com/{id}.zip", new string('A', 64), 1, "application/zip"), 1, dependencies, type, install: new InstallInfo(null, derived: false, folder: folder));

    public static InstalledContentRecord Record(ContentType type, string id, string folder, params ModDependency[] dependencies) =>
        new(Release(type, id, folder, dependencies), folder, DateTimeOffset.UnixEpoch, new Dictionary<string, string>());

    public static ModDependency Required(string modId, string? min = null, string? max = null) =>
        new(modId, ModDependencyKind.Required, min is null ? null : ModVersion.Parse(min), max is null ? null : ModVersion.Parse(max));
}
