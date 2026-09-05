using Borea.Core.Mods;

namespace Borea.Core.ModPacks;

/// <summary>
/// Downloads all mods referenced by a mod pack into Borea's library.
/// </summary>
public interface IModPackDownloader
{
    /// <summary>
    /// Downloads all mods referenced by a mod pack into Borea's library.
    /// Continues past individual mod failures rather than aborting the whole
    /// operation, so the caller can inspect ModPackDownloadResult and simply
    /// re-run this method to retry only the mods that failed (already-installed
    /// mods are skipped by the implementation, not re-downloaded).
    /// </summary>
    Task<ModPackDownloadResult> DownloadAsync(
        ModPackMetadata modPack,
        Func<string, Mods.ModVersion, string> resolveDestination,
        IProgress<ModPackDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public readonly record struct ModPackDownloadProgress(
    string CurrentModId,
    int CompletedCount,
    int TotalCount,
    DownloadProgress CurrentModProgress);

/// <summary>
/// Outcome of a completed mod pack download attempt. Each entry in the pack
/// resolves to either a success or a failure; nothing is thrown for
/// individual mod failures, only for pack-wide problems (e.g. invalid input).
/// </summary>
public sealed class ModPackDownloadResult
{
    public IReadOnlyDictionary<string, DownloadResult> Succeeded { get; }
    public IReadOnlyDictionary<string, ModDownloadFailure> Failed { get; }

    /// <summary>
    /// True if every entry in the pack downloaded successfully (or was
    /// already installed). False if anything needs a re-run.
    /// </summary>
    public bool IsComplete => Failed.Count == 0;

    public ModPackDownloadResult(
        IReadOnlyDictionary<string, DownloadResult> succeeded,
        IReadOnlyDictionary<string, ModDownloadFailure> failed)
    {
        Succeeded = succeeded ?? throw new ArgumentNullException(nameof(succeeded));
        Failed = failed ?? throw new ArgumentNullException(nameof(failed));
    }
}

/// <summary>
/// Records why a specific mod within a pack failed to download.
/// </summary>
public readonly record struct ModDownloadFailure(string ModId, Mods.ModVersion Version, string Reason);
