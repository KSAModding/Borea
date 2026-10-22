namespace Borea.Core.ModLoaders;

/// <summary>
/// Lists the loader versions that are on disk. It only reads, so it never moves, installs or removes a folder.
/// </summary>
public interface ILoaderVersionStore
{
    /// <summary>
    /// Every installed version of every loader, grouped by loader id with the newest version first and the versions Borea does not know last.
    /// Each folder is listed once, and each version of a loader is listed once, so <see cref="InstalledLoaderVersion.Find"/> is never ambiguous.
    /// </summary>
    Task<IReadOnlyList<InstalledLoaderVersion>> ListAsync(CancellationToken cancellationToken = default);
}
