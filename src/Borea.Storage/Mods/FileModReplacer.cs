using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.State;

namespace Borea.Storage.Mods;

public sealed class FileModReplacer : IModReplacer
{
    private readonly IGamePathProvider _paths;
    private readonly IModDownloader _downloader;
    private readonly IInstanceRepository _instances;
    private readonly IModStateRepository _modState;
    private readonly TimeProvider _timeProvider;
    private readonly Func<string, bool> _deleteRecoveryDirectory;

    public FileModReplacer(
        IGamePathProvider paths,
        IModDownloader downloader,
        IInstanceRepository instances,
        IModStateRepository modState,
        TimeProvider? timeProvider = null)
        : this(paths, downloader, instances, modState, timeProvider, TryDeleteDirectory)
    {
    }

    internal FileModReplacer(
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

    public async Task<ModReplacementResult> ReplaceAsync(
        Guid instanceId,
        InstalledMod expectedCurrent,
        ModVersionMetadata replacement,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedCurrent);
        ArgumentNullException.ThrowIfNull(replacement);
        FileModInstaller.RequireInstallable(replacement);

        if (!ModIds.Equals(expectedCurrent.ModId, replacement.ModId))
            throw new ArgumentException("The replacement must have the same mod ID as the installed mod.", nameof(replacement));

        RequireOwned(expectedCurrent);
        var before = await _instances.GetByIdAsync(instanceId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No instance with ID '{instanceId}' exists.");
        RequireExpected(before, expectedCurrent);

        var entries = await _modState.GetEntriesAsync(instanceId, cancellationToken).ConfigureAwait(false);
        var matchingEntries = entries.Where(entry => ModIds.Equals(entry.ModId, expectedCurrent.ModId)).ToArray();
        if (matchingEntries.Length == 0)
            throw new InvalidOperationException($"The manifest has no entry for installed mod '{expectedCurrent.ModId}'.");
        var wasActive = matchingEntries.Any(entry => entry.Enabled);

        var archivePath = Path.Combine(Path.GetTempPath(), $"borea-download-{Guid.NewGuid():N}.zip");
        var instanceRoot = _paths.GetInstanceRoot(instanceId);
        var stagingFolder = Path.Combine(instanceRoot, $".borea-staging-{Guid.NewGuid():N}");
        var backupFolder = Path.Combine(instanceRoot, $".borea-recovery-{Guid.NewGuid():N}");
        var modsFolder = _paths.GetInstanceModsFolder(instanceId);
        DownloadResult download;
        InstalledMod? installed = null;
        string? installedFolder = null;
        var backupCreated = false;
        var replacementMoved = false;

        try
        {
            download = await _downloader.DownloadAsync(replacement, archivePath, progress, cancellationToken).ConfigureAwait(false);
            FileModInstaller.Unpack(archivePath, replacement, stagingFolder);
            var ownershipToken = Guid.NewGuid().ToString("N");
            await File.WriteAllTextAsync(Path.Combine(stagingFolder, ModFolders.OwnershipFileName), ownershipToken, cancellationToken).ConfigureAwait(false);

            installed = new InstalledMod(
                replacement.ModId,
                replacement.Version,
                expectedCurrent.Reason,
                _timeProvider.GetUtcNow(),
                replacement,
                download.Sha256,
                ModInstallOwnership.Borea,
                ownershipToken);

            await _instances.UpdateAsync(
                instanceId,
                current =>
                {
                    RequireExpected(current, expectedCurrent);
                    installedFolder = ModFolders.FindOwned(modsFolder, expectedCurrent.ModId, expectedCurrent.OwnershipToken!)
                        ?? throw new InvalidOperationException($"Borea cannot find its owned folder for '{expectedCurrent.ModId}'.");
                    Directory.Move(installedFolder, backupFolder);
                    backupCreated = true;
                    Directory.Move(stagingFolder, installedFolder);
                    replacementMoved = true;
                    current.ReplaceMod(installed);
                    return true;
                },
                cancellationToken).ConfigureAwait(false);

            await _modState.AddEntryAsync(instanceId, replacement.ModId, wasActive, cancellationToken).ConfigureAwait(false);
            var active = await _modState.IsActiveAsync(instanceId, replacement.ModId, cancellationToken).ConfigureAwait(false);
            if (active != wasActive)
                throw new InvalidOperationException($"The manifest state changed while '{replacement.ModId}' was replaced.");

            var retainedRecoveryDirectory = _deleteRecoveryDirectory(backupFolder) ? null : backupFolder;
            backupCreated = retainedRecoveryDirectory is not null;
            return new ModReplacementResult(expectedCurrent, installed, download, retainedRecoveryDirectory);
        }
        catch (Exception operationError)
        {
            if (backupCreated)
            {
                try
                {
                    await RestoreAsync(instanceId, expectedCurrent, installed!, installedFolder!, backupFolder, replacementMoved).ConfigureAwait(false);
                    backupCreated = false;
                    replacementMoved = false;
                }
                catch (Exception recoveryError)
                {
                    var recoveryDirectory = Directory.Exists(backupFolder) ? backupFolder : installedFolder!;
                    throw new ModReplacementRecoveryException(operationError, recoveryError, recoveryDirectory);
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

    private async Task RestoreAsync(Guid instanceId, InstalledMod previous, InstalledMod replacement, string installedFolder, string backupFolder, bool replacementMoved)
    {
        await _instances.UpdateAsync(
            instanceId,
            current =>
            {
                var currentMod = current.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, previous.ModId))
                    ?? throw new InvalidOperationException($"The installed record for '{previous.ModId}' is missing.");
                if (!Matches(currentMod, previous) && !Matches(currentMod, replacement))
                    throw new InvalidOperationException($"Mod '{previous.ModId}' changed while Borea tried to restore it.");

                if (replacementMoved && Directory.Exists(installedFolder))
                {
                    var ownedReplacement = ModFolders.FindOwned(Path.GetDirectoryName(installedFolder)!, replacement.ModId, replacement.OwnershipToken!);
                    if (!string.Equals(ownedReplacement, installedFolder, StringComparison.Ordinal))
                        throw new InvalidOperationException($"The installed folder for '{previous.ModId}' changed while Borea tried to restore it.");
                    Directory.Delete(installedFolder, recursive: true);
                }
                if (!Directory.Exists(backupFolder))
                    throw new InvalidOperationException($"The recovery folder for '{previous.ModId}' is missing.");
                Directory.Move(backupFolder, installedFolder);

                if (!Matches(currentMod, previous))
                    current.ReplaceMod(previous);
                return true;
            }).ConfigureAwait(false);
    }

    private static void RequireExpected(Instance instance, InstalledMod expected)
    {
        var current = instance.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, expected.ModId))
            ?? throw new InvalidOperationException($"Mod '{expected.ModId}' is no longer installed.");
        RequireOwned(current);
        if (!Matches(current, expected))
            throw new InvalidOperationException($"Mod '{expected.ModId}' changed after the replacement was requested.");
    }

    private static bool Matches(InstalledMod current, InstalledMod expected) =>
        current.Version == expected.Version &&
        current.Reason == expected.Reason &&
        current.InstalledAt == expected.InstalledAt &&
        string.Equals(current.Checksum, expected.Checksum, StringComparison.OrdinalIgnoreCase) &&
        current.Ownership == expected.Ownership &&
        string.Equals(current.OwnershipToken, expected.OwnershipToken, StringComparison.Ordinal);

    private static void RequireOwned(InstalledMod installed)
    {
        if (!installed.CanDeleteFiles)
            throw new InvalidOperationException($"Borea cannot replace '{installed.ModId}' because it does not own the installed folder.");
    }

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
