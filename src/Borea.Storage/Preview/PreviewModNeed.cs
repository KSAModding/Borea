using Borea.Core.Dependencies;
using Borea.Core.Mods;

namespace Borea.Storage.Preview;

/// <summary>Where the preview reads the files of a mod from. The sources after the game come in the order the preview tries them.</summary>
internal enum PreviewModLocation
{
    /// <summary>A content mod of the game folder, such as Content/Core.</summary>
    Game,

    /// <summary>The mods folder of the target instance.</summary>
    InstanceFolder,

    /// <summary>The shared store entry of the release, see <see cref="Mods.ModStore"/>.</summary>
    StoreEntry,

    /// <summary>The mods folder of another instance that holds the same release.</summary>
    OtherInstance,

    /// <summary>The release archive in the preview cache, read in place.</summary>
    ArchiveCache,
}

/// <summary>
/// A mod that the preview reads, with the folder or the zip file it reads it from. <paramref name="Version"/> is null for
/// a content mod of the game and for a mod that Borea did not install.
/// </summary>
internal sealed record PreviewModSource(string ModId, ModVersion? Version, PreviewModLocation Location, string Path);

/// <summary>
/// A mod that the vehicle needs and that no local source has. <paramref name="Release"/> is the release the plan selected,
/// and the fetch consent shows its download size. It is null when only the dependency is known, because the planner
/// failed or the offline index has no release that fits, and the size is unknown then.
/// </summary>
internal sealed record PreviewModNeed(string ModId, ModVersionMetadata? Release, ModDependency? Dependency = null)
{
    public long? SizeBytes => Release?.Download.SizeBytes;
}
