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
    /// stamps <c>[provides]</c>. A standalone loader without a given directory
    /// goes into its own version folder beside the installed versions, and a
    /// new version starts with the configuration file of the recorded one. The
    /// record names the version that an older Borea starts for every instance,
    /// so it moves to the new version only when that version is not lower, when
    /// the recorded version is a prerelease and the new one is a release, when
    /// the recorded folder is gone, or when the caller says that the recorded
    /// version does not fit the game. It never moves from a release to a
    /// prerelease outside a prerelease channel, and never from a folder outside
    /// Borea's Loaders root, so the install of an older version for a pin
    /// changes nothing for the other instances.
    /// Reinstalling an installed version replaces its folder in place, keeps
    /// its configuration file, and restores the old folder when that fails.
    /// A failed new install removes what it wrote.
    /// </summary>
    /// <param name="directory">
    /// Where a standalone loader goes. Null means Borea's own version folder.
    /// A given directory keeps the single recorded install. Invalid for any
    /// other anchor.
    /// </param>
    /// <param name="replacesUnfitRecord">
    /// True when the index rates the recorded version incompatible with the
    /// game and does not rate this release so. The record then moves to this
    /// release also when it is lower. See <see cref="LoaderUpdate.ReplacesUnfitRecordAsync"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The listing is not a mod loader, the release does not belong to it, or
    /// the directory does not fit the anchor.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The destination holds foreign files or another version, the loader is
    /// recorded elsewhere, the version of the loader in the folder of an older
    /// Borea is unknown, a game runs while a folder must be replaced or moved,
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
        bool replacesUnfitRecord = false,
        IProgress<InstallProgress>? progress = null,
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
