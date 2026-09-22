namespace Borea.Core.Mods;

public interface IForeignModHandover
{
    /// <summary>
    /// Installs the release a mod Borea did not install is recorded as over its
    /// folder, so Borea owns the files from then on and can update and remove
    /// the mod. The mod keeps its place in the instance and its enabled state,
    /// and a handover that fails leaves the folder and the record as they were.
    /// </summary>
    Task<ModHandoverResult> TakeOwnershipAsync(
        Guid instanceId,
        string modId,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What a finished handover left behind.
/// <see cref="RetainedRecoveryDirectory"/> names the folder that still holds
/// the previous files, because Borea could not delete it, and is null when the
/// handover left nothing behind.
/// </summary>
public sealed record ModHandoverResult(
    InstalledMod Installed,
    DownloadResult Download,
    string? RetainedRecoveryDirectory);
