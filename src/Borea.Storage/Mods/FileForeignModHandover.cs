using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.State;

namespace Borea.Storage.Mods;

/// <summary>
/// File-backed <see cref="IForeignModHandover"/>. The release is downloaded and
/// unpacked into a staging folder first, so the folder and the record change
/// together at the end, and the ownership marker reaches the instance only with
/// the files Borea itself wrote. The previous files go into a recovery folder
/// that the handover deletes when it is finished, and a delete that fails names
/// that folder in the result instead of dropping it silently.
/// </summary>
public sealed class FileForeignModHandover : IForeignModHandover
{
    private readonly IGamePathProvider _paths;
    private readonly IModDownloader _downloader;
    private readonly IInstanceRepository _instances;
    private readonly IModStateRepository _modState;
    private readonly TimeProvider _timeProvider;
    private readonly Func<string, bool> _deleteRecoveryDirectory;

    public FileForeignModHandover(
        IGamePathProvider paths,
        IModDownloader downloader,
        IInstanceRepository instances,
        IModStateRepository modState,
        TimeProvider? timeProvider = null)
        : this(paths, downloader, instances, modState, timeProvider, TryDeleteDirectory)
    {
    }

    internal FileForeignModHandover(
        IGamePathProvider paths,
        IModDownloader downloader,
        IInstanceRepository instances,
        IModStateRepository modState,
        TimeProvider? timeProvider,
        Func<string, bool> deleteRecoveryDirectory)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _modState = modState ?? throw new ArgumentNullException(nameof(modState));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _deleteRecoveryDirectory = deleteRecoveryDirectory ?? throw new ArgumentNullException(nameof(deleteRecoveryDirectory));
    }

    public async Task<ModHandoverResult> TakeOwnershipAsync(
        Guid instanceId,
        string modId,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("Mod ID cannot be null or whitespace.", nameof(modId));

        var instance = await _instances.GetByIdAsync(instanceId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No instance with ID '{instanceId}' exists.");
        var foreign = RequireForeign(instance, modId);
        var release = foreign.Metadata;
        FileModInstaller.RequireInstallable(release);

        var modsFolder = _paths.GetInstanceModsFolder(instanceId);
        if (ModFolders.Find(modsFolder, foreign.ModId) is null)
            throw new InvalidOperationException($"The instance has no folder for '{foreign.ModId}'.");

        var wasActive = await _modState.IsActiveAsync(instanceId, foreign.ModId, cancellationToken).ConfigureAwait(false);
        var instanceRoot = _paths.GetInstanceRoot(instanceId);
        var archivePath = Path.Combine(Path.GetTempPath(), $"borea-download-{Guid.NewGuid():N}.zip");
        var stagingFolder = Path.Combine(instanceRoot, $".borea-staging-{Guid.NewGuid():N}");
        var backupFolder = Path.Combine(instanceRoot, $".borea-recovery-{Guid.NewGuid():N}");
        InstalledMod? owned = null;
        string? folder = null;
        var backupCreated = false;
        var ownedMoved = false;

        try
        {
            var download = await _downloader.DownloadAsync(release, archivePath, progress.ForDownload(release), cancellationToken).ConfigureAwait(false);
            progress.Report(release, InstallPhase.Extracting);
            FileModInstaller.Unpack(archivePath, release, stagingFolder);

            progress.Report(release, InstallPhase.Finishing);
            var ownershipToken = Guid.NewGuid().ToString("N");
            await File.WriteAllTextAsync(Path.Combine(stagingFolder, ModFolders.OwnershipFileName), ownershipToken, cancellationToken).ConfigureAwait(false);
            owned = new InstalledMod(
                foreign.ModId,
                release.Version,
                foreign.Reason,
                _timeProvider.GetUtcNow(),
                release,
                download.Sha256,
                ModInstallOwnership.Borea,
                ownershipToken);

            await _instances.UpdateAsync(
                instanceId,
                current =>
                {
                    var recorded = RequireForeign(current, foreign.ModId);
                    if (!Matches(recorded, foreign))
                        throw new InvalidOperationException($"Mod '{foreign.ModId}' changed after the handover was requested.");

                    folder = ModFolders.Find(modsFolder, foreign.ModId)
                        ?? throw new InvalidOperationException($"The instance has no folder for '{foreign.ModId}'.");
                    Directory.Move(folder, backupFolder);
                    backupCreated = true;
                    Directory.Move(stagingFolder, folder);
                    ownedMoved = true;
                    current.ReplaceMod(owned);
                    return true;
                },
                cancellationToken).ConfigureAwait(false);

            await _modState.AddEntryAsync(instanceId, foreign.ModId, wasActive, cancellationToken).ConfigureAwait(false);
            if (await _modState.IsActiveAsync(instanceId, foreign.ModId, cancellationToken).ConfigureAwait(false) != wasActive)
                throw new InvalidOperationException($"The manifest state changed while Borea took over '{foreign.ModId}'.");

            var retainedRecoveryDirectory = _deleteRecoveryDirectory(backupFolder) ? null : backupFolder;
            backupCreated = retainedRecoveryDirectory is not null;
            return new ModHandoverResult(owned, download, retainedRecoveryDirectory);
        }
        catch (Exception operationError)
        {
            if (backupCreated)
            {
                try
                {
                    await RestoreAsync(instanceId, foreign, owned!, folder!, backupFolder, ownedMoved).ConfigureAwait(false);
                }
                catch (Exception recoveryError)
                {
                    throw new ModReplacementRecoveryException(operationError, recoveryError, Directory.Exists(backupFolder) ? backupFolder : folder!);
                }
            }

            throw;
        }
        finally
        {
            TryDeleteFile(archivePath);
            TryDeleteDirectory(stagingFolder);
        }
    }

    private async Task RestoreAsync(
        Guid instanceId,
        InstalledMod foreign,
        InstalledMod owned,
        string folder,
        string backupFolder,
        bool ownedMoved)
    {
        await _instances.UpdateAsync(
            instanceId,
            current =>
            {
                var recorded = current.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, foreign.ModId))
                    ?? throw new InvalidOperationException($"The installed record for '{foreign.ModId}' is missing.");
                if (!Matches(recorded, foreign) && !Matches(recorded, owned))
                    throw new InvalidOperationException($"Mod '{foreign.ModId}' changed while Borea tried to restore it.");

                if (ownedMoved && Directory.Exists(folder))
                {
                    var ownedFolder = ModFolders.FindOwned(Path.GetDirectoryName(folder)!, owned.ModId, owned.OwnershipToken!);
                    if (!string.Equals(ownedFolder, folder, StringComparison.Ordinal))
                        throw new InvalidOperationException($"The folder of '{foreign.ModId}' changed while Borea tried to restore it.");

                    Directory.Delete(folder, recursive: true);
                }

                if (!Directory.Exists(backupFolder))
                    throw new InvalidOperationException($"The recovery folder for '{foreign.ModId}' is missing.");

                Directory.Move(backupFolder, folder);
                if (!Matches(recorded, foreign))
                    current.ReplaceMod(foreign);

                return true;
            }).ConfigureAwait(false);
    }

    private static InstalledMod RequireForeign(Instance instance, string modId)
    {
        var installed = instance.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, modId))
            ?? throw new InvalidOperationException($"Mod '{modId}' is not installed in this instance.");

        if (installed.Ownership != ModInstallOwnership.Foreign)
            throw new InvalidOperationException($"Borea already owns the files of '{installed.ModId}'.");

        return installed;
    }

    private static bool Matches(InstalledMod current, InstalledMod expected) =>
        current.Version == expected.Version &&
        current.Reason == expected.Reason &&
        current.InstalledAt == expected.InstalledAt &&
        string.Equals(current.Checksum, expected.Checksum, StringComparison.OrdinalIgnoreCase) &&
        current.Ownership == expected.Ownership &&
        string.Equals(current.OwnershipToken, expected.OwnershipToken, StringComparison.Ordinal);

    private static bool TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
