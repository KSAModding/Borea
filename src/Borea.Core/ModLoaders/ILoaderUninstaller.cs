using Borea.Core.Mods;

namespace Borea.Core.ModLoaders;

/// <summary>
/// Removes loader installations. Both calls refuse while a game runs, move the folder away before they save the record, and
/// delete it last, so a failure never leaves a record that names a half-deleted folder. A folder that the player owns stays.
/// The callers check that no instance uses a version, because only they know the instances.
/// </summary>
public interface ILoaderUninstaller
{
    /// <summary>
    /// Removes the loader record and the folder that it names. The directory is removed only when Borea created it, and the
    /// loader folder Loaders/&lt;id&gt; is never removed while it holds version folders.
    /// </summary>
    Task<LoaderUninstallResult> UninstallAsync(
        string loaderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one installed version of a loader. When the record names that version, it moves to the newest remaining
    /// version, a release before a prerelease, or it is removed when no version remains.
    /// </summary>
    Task<LoaderUninstallResult> UninstallVersionAsync(
        string loaderId,
        ModVersion version,
        CancellationToken cancellationToken = default);
}

/// <param name="Directory">The folder of the removed installation, or null when nothing was installed.</param>
/// <param name="RecordRemoved">True when the record of the loader was removed. A record that moved to another version is not removed.</param>
/// <param name="DirectoryRemoved">True when the folder left its place, so the version is no longer installed.</param>
/// <param name="Failure">Why nothing changed, or <see cref="LoaderUninstallFailure.None"/>.</param>
/// <param name="Version">The removed version, or null when it is unknown.</param>
/// <param name="FolderDeleted">
/// True when the folder that left its place was also deleted. When it is false after a removal, a file was locked, and the
/// next removal deletes what is left.
/// </param>
public sealed record LoaderUninstallResult(
    string LoaderId,
    string? Directory,
    bool RecordRemoved,
    bool DirectoryRemoved,
    LoaderUninstallFailure Failure = LoaderUninstallFailure.None,
    ModVersion? Version = null,
    bool FolderDeleted = false);

public enum LoaderUninstallFailure
{
    None,

    /// <summary>A game or loader runs, so the folder and the record did not change.</summary>
    GameRunning,

    /// <summary>The loader or the version is not installed.</summary>
    NotInstalled,
}
