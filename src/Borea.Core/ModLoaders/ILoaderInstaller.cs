using Borea.Core.Mods;

namespace Borea.Core.ModLoaders;

/// <summary>
/// Installs one release of a mod loader per RFC 0035 and records its
/// directory in the settings. Starting it is the launcher's job.
/// </summary>
public interface ILoaderInstaller
{
    /// <summary>
    /// The stamped install decides where the loader goes; the live listing
    /// decides what to launch and what to configure, because RFC 0035 never
    /// stamps <c>[provides]</c>. A recorded loader is replaced in place and
    /// keeps its configuration file. A failed first install removes what it
    /// wrote.
    /// </summary>
    /// <param name="directory">
    /// Where a standalone loader goes. Null means the recorded directory, or
    /// Borea's own choice for a first install. Invalid for any other anchor.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The listing is not a mod loader, the release does not belong to it, or
    /// the directory does not fit the anchor.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The destination holds foreign files, the loader is recorded elsewhere,
    /// the game directory is not set, the archive is empty where the release
    /// says, or the launch target is missing.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The release is not a mod loader, or names an anchor, path, archive
    /// format or configuration format this installer cannot perform.
    /// </exception>
    /// <exception cref="DownloadFailedException">No source served the archive.</exception>
    Task<LoaderInstallResult> InstallAsync(
        ModMetadata loader,
        ModVersionMetadata release,
        string? directory = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What a loader install left behind.
/// </summary>
/// <param name="ConfigurationFile">The file that received the game path, or null.</param>
/// <param name="Replaced">Whether a recorded install was replaced in place.</param>
public sealed record LoaderInstallResult(
    string LoaderId,
    ModVersion Version,
    string Directory,
    DownloadResult Download,
    string? ConfigurationFile,
    bool Replaced);
