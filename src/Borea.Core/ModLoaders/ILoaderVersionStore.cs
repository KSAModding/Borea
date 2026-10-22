namespace Borea.Core.ModLoaders;

/// <summary>
/// Lists the loader versions that are on disk. It only reads, so it never moves, installs or removes a folder.
/// A version folder that is not recorded is listed as a complete install as soon as it holds an entry. An installer must therefore
/// unpack a new version into a dot-prefixed sibling, which the store skips, and rename it into place as its last step.
/// A folder that cannot be read is skipped and does not make the list fail.
/// </summary>
public interface ILoaderVersionStore
{
    /// <summary>
    /// Every installed version of every loader, grouped by loader id with the newest version first and the versions Borea does not know last.
    /// Each folder is listed once, and each version of a loader is listed once, so <see cref="InstalledLoaderVersion.Find"/> is never ambiguous.
    /// </summary>
    Task<IReadOnlyList<InstalledLoaderVersion>> ListAsync(CancellationToken cancellationToken = default);
}
