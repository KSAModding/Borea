using Borea.Core.Mods;

namespace Borea.Core.ModLoaders;

/// <summary>
/// One version of a loader on disk, either the installation Borea recorded for the loader or a version folder below the Loaders root.
/// </summary>
/// <param name="Version">The release version, or null when Borea does not know it.</param>
/// <param name="RawVersion">The file version read from the launch target, which only a recorded installation carries.</param>
/// <param name="IsAdopted">True when Borea did not create the folder, so it must never delete it.</param>
/// <param name="IsRecorded">True when this is the installation that the Borea settings record for the loader.</param>
public sealed record InstalledLoaderVersion(
    string LoaderId,
    ModVersion? Version,
    string? RawVersion,
    string DirectoryPath,
    bool IsAdopted,
    bool IsRecorded)
{
    /// <summary>The versions of one loader, in the order of the list.</summary>
    public static IReadOnlyList<InstalledLoaderVersion> Of(IEnumerable<InstalledLoaderVersion> versions, string loaderId)
    {
        ArgumentNullException.ThrowIfNull(versions);
        return versions.Where(v => ModIds.Equals(v.LoaderId, loaderId)).ToList();
    }

    /// <summary>The installed folder of one exact version of a loader, or null when that version is not installed.</summary>
    public static InstalledLoaderVersion? Find(IEnumerable<InstalledLoaderVersion> versions, string loaderId, ModVersion version)
    {
        ArgumentNullException.ThrowIfNull(versions);
        return versions.FirstOrDefault(v => ModIds.Equals(v.LoaderId, loaderId) && v.Version == version);
    }
}
