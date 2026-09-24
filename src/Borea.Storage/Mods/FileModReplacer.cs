using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.Planning;
using Borea.Core.State;
using Borea.Storage.Files;

namespace Borea.Storage.Mods;

public sealed class FileModReplacer : IModReplacer
{
    private readonly IGamePathProvider _paths;
    private readonly IModDownloader _downloader;
    private readonly IInstanceRepository _instances;
    private readonly IModStateRepository _modState;
    private readonly TimeProvider _timeProvider;
    private readonly Func<string, bool> _deleteRecoveryDirectory;
    private readonly ModStore _store;

    /// <param name="store">Null copies every release into its instance.</param>
    public FileModReplacer(
        IGamePathProvider paths,
        IModDownloader downloader,
        IInstanceRepository instances,
        IModStateRepository modState,
        TimeProvider? timeProvider = null,
        ModStore? store = null)
        : this(paths, downloader, instances, modState, timeProvider, TryDeleteDirectory, store)
    {
    }

    internal FileModReplacer(
        IGamePathProvider paths,
        IModDownloader downloader,
        IInstanceRepository instances,
        IModStateRepository modState,
        TimeProvider? timeProvider,
        Func<string, bool> deleteRecoveryDirectory,
        ModStore? store = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _modState = modState ?? throw new ArgumentNullException(nameof(modState));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _deleteRecoveryDirectory = deleteRecoveryDirectory ?? throw new ArgumentNullException(nameof(deleteRecoveryDirectory));
        _store = store ?? new ModStore(paths, new DirectoryLinker(), linksReleases: false);
    }

    public async Task<ModReplacementResult> ReplaceAsync(
        Guid instanceId,
        InstalledMod expectedCurrent,
        ModVersionMetadata replacement,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => (await ReplaceCoreAsync(instanceId, expectedCurrent, replacement, expectedState: null, progress, cancellationToken).ConfigureAwait(false)).Result;

    public async Task<GuardedModReplacementResult> ReplaceGuardedAsync(
        Guid instanceId,
        InstalledMod expectedCurrent,
        ModVersionMetadata replacement,
        InstallPlanningState expectedState,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedState);
        return await ReplaceCoreAsync(instanceId, expectedCurrent, replacement, expectedState, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GuardedModReplacementResult> ReplaceCoreAsync(
        Guid instanceId,
        InstalledMod expectedCurrent,
        ModVersionMetadata replacement,
        InstallPlanningState? expectedState,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
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

        var instanceRoot = _paths.GetInstanceRoot(instanceId);
        var stagingFolder = Path.Combine(instanceRoot, $".borea-staging-{Guid.NewGuid():N}");
        var backupFolder = Path.Combine(instanceRoot, $".borea-recovery-{Guid.NewGuid():N}");
        var modsFolder = _paths.GetInstanceModsFolder(instanceId);
        DownloadResult download;
        InstalledMod? installed = null;
        string? installedFolder = null;
        var backupCreated = false;
        var replacementMoved = false;
        InstallPlanningState? resultingState = null;

        try
        {
            var staged = await _store.StageAsync(_downloader, replacement, stagingFolder, progress, cancellationToken).ConfigureAwait(false);
            download = staged.Download;
            installed = new InstalledMod(
                replacement.ModId,
                replacement.Version,
                expectedCurrent.Reason,
                _timeProvider.GetUtcNow(),
                replacement,
                download.Sha256,
                ModInstallOwnership.Borea,
                staged.OwnershipToken,
                staged.Storage);

            await _instances.UpdateAsync(
                instanceId,
                current =>
                {
                    if (expectedState is not null && !expectedState.Matches(current))
                        throw new InvalidOperationException("The instance changed after the replacement was planned.");

                    RequireExpected(current, expectedCurrent);
                    installedFolder = ModFolders.FindOwned(modsFolder, expectedCurrent, _store)
                        ?? throw new InvalidOperationException($"Borea cannot find its owned folder for '{expectedCurrent.ModId}'.");
                    Directory.Move(installedFolder, backupFolder);
                    backupCreated = true;
                    Directory.Move(stagingFolder, installedFolder);
                    replacementMoved = true;
                    current.ReplaceMod(installed);
                    resultingState = InstallPlanningState.Capture(current);
                    return true;
                },
                cancellationToken).ConfigureAwait(false);

            await _modState.AddEntryAsync(instanceId, replacement.ModId, wasActive, cancellationToken).ConfigureAwait(false);
            var active = await _modState.IsActiveAsync(instanceId, replacement.ModId, cancellationToken).ConfigureAwait(false);
            if (active != wasActive)
                throw new InvalidOperationException($"The manifest state changed while '{replacement.ModId}' was replaced.");

            var retainedRecoveryDirectory = _deleteRecoveryDirectory(backupFolder) ? null : backupFolder;
            backupCreated = retainedRecoveryDirectory is not null;
            await _store.ReleaseAsync(expectedCurrent).ConfigureAwait(false);
            var result = new ModReplacementResult(expectedCurrent, installed, download, retainedRecoveryDirectory);
            return new GuardedModReplacementResult(result, resultingState!);
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

            TryDeleteDirectory(stagingFolder);
            if (installed is not null)
                await _store.ReleaseAsync(installed).ConfigureAwait(false);

            throw;
        }
        finally
        {
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
                    var ownedReplacement = ModFolders.FindOwned(Path.GetDirectoryName(installedFolder)!, replacement, _store);
                    if (!string.Equals(ownedReplacement, installedFolder, StringComparison.Ordinal))
                        throw new InvalidOperationException($"The installed folder for '{previous.ModId}' changed while Borea tried to restore it.");
                    DirectoryLinks.DeleteTreeWithoutFollowingLinks(installedFolder);
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
        current.Storage == expected.Storage &&
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
            DirectoryLinks.DeleteTreeWithoutFollowingLinks(path);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
