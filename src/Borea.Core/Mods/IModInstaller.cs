using Borea.Core.State;

namespace Borea.Core.Mods;

/// <summary>
/// Puts one release of a mod into an instance: the archive is fetched and
/// verified, the directory <see cref="InstallInfo.Root"/> names is unpacked
/// into the instance's mods folder under the id, the install is recorded on
/// the instance, and the manifest gets an entry under the rules of
/// <see cref="IModStateRepository.AddEntryAsync"/>. Which release to install
/// is the caller's decision, dependencies included: nothing here walks them.
/// </summary>
public interface IModInstaller
{
    /// <summary>
    /// Installs <paramref name="release"/> into the instance. The folder is
    /// named by the id whatever the archive calls it, because the game assigns
    /// the id from the folder name (Mod.MakeUsing). A failed install removes
    /// the folder and the record it created, and the error that caused the
    /// failure is the one the caller sees; a folder the cleanup cannot remove
    /// stays behind.
    /// </summary>
    /// <param name="enable">
    /// Whether a new manifest entry is written enabled. An entry that already
    /// exists keeps its flag either way, so a mod the user disabled in the game
    /// stays disabled through a reinstall.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The instance does not exist, already has the mod, or holds a folder under
    /// that id which Borea did not install; or the archive does not hold a mod
    /// where the release says it does.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The release is not a mod, or names an install target, path or archive
    /// format this installer cannot perform.
    /// </exception>
    /// <exception cref="DownloadFailedException">No source served the archive.</exception>
    Task<InstallResult> InstallAsync(
        Guid instanceId,
        ModVersionMetadata release,
        InstallReason reason,
        bool enable,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What an install left behind.
/// </summary>
/// <param name="Mod">The record now on the instance.</param>
/// <param name="Download">Where the archive came from and what it hashed to.</param>
/// <param name="ManifestEntry">Whether the manifest entry was written or was already there.</param>
public sealed record InstallResult(InstalledMod Mod, DownloadResult Download, ModEntryAddResult ManifestEntry);
