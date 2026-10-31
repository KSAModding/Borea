using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.UserContent;
using Borea.Storage.Files;
using Borea.Storage.Mods;

namespace Borea.Storage.Instances;

/// <summary>
/// Installs a vehicle or save into Vehicles/&lt;folder&gt; or saves/&lt;folder&gt; (RFC 0085). The archive is
/// downloaded, unpacked and checked in a staging folder in the instance root, on the same volume and outside
/// the folders the game reads (VehicleSaves.Refresh and GameSaves.Refresh read every folder under them), so
/// one move puts it into place. A folder is never linked from a shared store, because the game writes the
/// item back into its folder (SaveDirectory.TryReplace).
/// </summary>
public sealed class FileUserContentInstaller : IUserContentInstaller
{
    private readonly IGamePathProvider _paths;
    private readonly IModDownloader _downloader;
    private readonly IInstalledContentRecords _records;
    private readonly Func<bool> _isGameRunning;
    private readonly GameSaveBackupFolder _backups;
    private readonly TimeProvider _time;

    /// <param name="records">Pass the one store of the app, so two stores never write one file.</param>
    /// <param name="isGameRunning">Whether a KSA or StarMap process runs, whoever started it.</param>
    public FileUserContentInstaller(IGamePathProvider paths, IModDownloader downloader, IInstalledContentRecords records, Func<bool> isGameRunning, TimeProvider? time = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _isGameRunning = isGameRunning ?? throw new ArgumentNullException(nameof(isGameRunning));
        _time = time ?? TimeProvider.System;
        _backups = new GameSaveBackupFolder(paths, _time);
    }

    public Task<UserContentResult> CheckInstallAsync(Guid instanceId, ModVersionMetadata release, string? name, CancellationToken cancellationToken = default)
    {
        RequireInstallable(release);
        return CheckInstallCoreAsync(instanceId, release, name, cancellationToken);
    }

    public async Task<UserContentResult> CheckUpdateAsync(Guid instanceId, InstalledContentRecord installed, ModVersionMetadata release, string? name, bool discardChanges, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installed);
        RequireInstallable(release);
        RequireSameListing(installed, release);
        return (await CheckUpdateCoreAsync(instanceId, installed, release, name, discardChanges, cancellationToken).ConfigureAwait(false)).Result;
    }

    public async Task<UserContentResult> InstallAsync(Guid instanceId, ModVersionMetadata release, string? name, UserContentStep? beforeMove = null, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        RequireInstallable(release);
        var check = await CheckInstallCoreAsync(instanceId, release, name, cancellationToken).ConfigureAwait(false);
        if (check.Outcome != UserContentOutcome.Ready)
            return check;

        var folderName = check.FolderName!;
        var kindFolder = UserContentFolder.KindFolder(_paths, instanceId, release.Type);
        var staging = StagingFolder(instanceId);
        try
        {
            var files = await StageAsync(release, folderName, staging, beforeMove, progress, cancellationToken).ConfigureAwait(false);
            if (_isGameRunning())
                return new UserContentResult(UserContentOutcome.GameRunning, folderName);
            if (UserContentFolder.IsTaken(kindFolder, folderName))
                return new UserContentResult(UserContentOutcome.NameTaken, folderName);

            // No folder has the name now, so a record of it, or an older record of this listing, has lost its folder.
            await DropStaleAsync(instanceId, release, folderName, cancellationToken).ConfigureAwait(false);
            var record = new InstalledContentRecord(release, folderName, _time.GetUtcNow(), files);
            var target = Path.Combine(kindFolder, folderName);
            Directory.CreateDirectory(kindFolder);
            DirectoryRename.Move(staging, target);
            try
            {
                await _records.AddAsync(instanceId, record, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                DirectoryRename.Move(target, staging);
                throw;
            }

            return new UserContentResult(UserContentOutcome.Installed, folderName, record);
        }
        finally
        {
            TryDeleteFolder(staging);
        }
    }

    public async Task<UserContentResult> UpdateAsync(Guid instanceId, InstalledContentRecord installed, ModVersionMetadata release, string? name, bool discardChanges, UserContentStep? beforeMove = null, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installed);
        RequireInstallable(release);
        RequireSameListing(installed, release);
        var (check, current) = await CheckUpdateCoreAsync(instanceId, installed, release, name, discardChanges, cancellationToken).ConfigureAwait(false);
        if (check.Outcome != UserContentOutcome.Ready)
            return check;

        var folderName = check.FolderName!;
        var kindFolder = UserContentFolder.KindFolder(_paths, instanceId, release.Type);
        var staging = StagingFolder(instanceId);
        try
        {
            var files = await StageAsync(release, folderName, staging, beforeMove, progress, cancellationToken).ConfigureAwait(false);

            // The player may have played the save while the archive downloaded.
            var again = await CheckUpdateCoreAsync(instanceId, current!, release, name, discardChanges, CancellationToken.None).ConfigureAwait(false);
            if (again.Result.Outcome != UserContentOutcome.Ready)
                return again.Result;

            // No folder has the new name, so a record of it has lost its folder.
            if (!string.Equals(folderName, current!.FolderName, StringComparison.OrdinalIgnoreCase))
                await _records.RemoveAsync(instanceId, release.Type, folderName, cancellationToken).ConfigureAwait(false);

            var oldFolder = FileGameSaveStore.FindFolder(Path.Combine(kindFolder, current.FolderName));
            var target = Path.Combine(kindFolder, folderName);
            var record = new InstalledContentRecord(release, folderName, _time.GetUtcNow(), files);
            var backup = await _backups.MoveInAsync(instanceId, UserContentFolder.Kind(release.Type), oldFolder, GameSaveBackupReason.Replaced).ConfigureAwait(false);
            var moved = false;
            try
            {
                DirectoryRename.Move(staging, target);
                moved = true;
                await _records.ReplaceAsync(instanceId, current.FolderName, record, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                if (moved)
                    DirectoryRename.Move(target, staging);

                DirectoryRename.Move(backup, oldFolder);
                GameSaveBackupFolder.TryDeleteRecord(backup);
                throw;
            }

            return new UserContentResult(UserContentOutcome.Updated, folderName, record);
        }
        finally
        {
            TryDeleteFolder(staging);
        }
    }

    public async Task<UserContentResult> RemoveAsync(Guid instanceId, InstalledContentRecord installed, bool discardChanges, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installed);
        if (_isGameRunning())
            return new UserContentResult(UserContentOutcome.GameRunning, installed.FolderName);

        var (outcome, current, folder) = await FindRecordedAsync(instanceId, installed, discardChanges, cancellationToken).ConfigureAwait(false);
        if (outcome is not null)
            return outcome;

        await _records.RemoveAsync(instanceId, current!.Type, current.FolderName, cancellationToken).ConfigureAwait(false);
        try
        {
            await _backups.MoveInAsync(instanceId, UserContentFolder.Kind(current.Type), folder!, GameSaveBackupReason.Deleted).ConfigureAwait(false);
        }
        catch
        {
            await _records.AddAsync(instanceId, current, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        return new UserContentResult(UserContentOutcome.Removed, current.FolderName, current);
    }

    private async Task<UserContentResult> CheckInstallCoreAsync(Guid instanceId, ModVersionMetadata release, string? name, CancellationToken cancellationToken)
    {
        var folderName = name ?? release.Install!.Folder!;
        if (_isGameRunning())
            return new UserContentResult(UserContentOutcome.GameRunning, folderName);
        if (!GameSaveName.IsValid(folderName))
            return new UserContentResult(UserContentOutcome.InvalidName, folderName);

        var kindFolder = UserContentFolder.KindFolder(_paths, instanceId, release.Type);
        foreach (var record in await _records.ListAsync(instanceId, cancellationToken).ConfigureAwait(false))
        {
            if (record.Type == release.Type && ModIds.Equals(record.ListingId, release.ModId) && Directory.Exists(FileGameSaveStore.FindFolder(Path.Combine(kindFolder, record.FolderName))))
                return new UserContentResult(UserContentOutcome.AlreadyInstalled, record.FolderName, record);
        }

        return UserContentFolder.IsTaken(kindFolder, folderName)
            ? new UserContentResult(UserContentOutcome.NameTaken, folderName)
            : new UserContentResult(UserContentOutcome.Ready, folderName);
    }

    private async Task<(UserContentResult Result, InstalledContentRecord? Current)> CheckUpdateCoreAsync(Guid instanceId, InstalledContentRecord installed, ModVersionMetadata release, string? name, bool discardChanges, CancellationToken cancellationToken)
    {
        if (_isGameRunning())
            return (new UserContentResult(UserContentOutcome.GameRunning, installed.FolderName), null);

        var (outcome, current, _) = await FindRecordedAsync(instanceId, installed, discardChanges, cancellationToken).ConfigureAwait(false);
        if (outcome is not null)
            return (outcome, null);

        // A folder whose name differs from the release it came from got its name from the player, and it keeps it.
        var folderName = name
            ?? (string.Equals(current!.FolderName, current.Metadata.Install?.Folder, StringComparison.Ordinal) ? release.Install!.Folder! : current.FolderName);
        if (!GameSaveName.IsValid(folderName))
            return (new UserContentResult(UserContentOutcome.InvalidName, folderName), null);

        var kindFolder = UserContentFolder.KindFolder(_paths, instanceId, release.Type);
        return UserContentFolder.IsTaken(kindFolder, folderName, own: current!.FolderName)
            ? (new UserContentResult(UserContentOutcome.NameTaken, folderName), null)
            : (new UserContentResult(UserContentOutcome.Ready, folderName), current);
    }

    /// <summary>The record and its folder, or the outcome that stops the change.</summary>
    private async Task<(UserContentResult? Outcome, InstalledContentRecord? Current, string? Folder)> FindRecordedAsync(Guid instanceId, InstalledContentRecord installed, bool discardChanges, CancellationToken cancellationToken)
    {
        var current = await _records.FindAsync(instanceId, installed.Type, installed.FolderName, cancellationToken).ConfigureAwait(false);
        var folder = current is null ? null : FileGameSaveStore.FindFolder(Path.Combine(UserContentFolder.KindFolder(_paths, instanceId, current.Type), current.FolderName));
        if (current is null || !Directory.Exists(folder))
            return (new UserContentResult(UserContentOutcome.NotInstalled, installed.FolderName), null, null);

        var changes = await _records.CompareAsync(instanceId, current, cancellationToken).ConfigureAwait(false);
        if (!changes.IsUnchanged && !discardChanges)
            return (new UserContentResult(UserContentOutcome.Changed, current.FolderName, current, changes), null, null);

        return (null, current, folder);
    }

    /// <summary>Downloads, unpacks, checks and renames the release in the staging folder, then runs the step before the move.</summary>
    private async Task<IReadOnlyDictionary<string, string>> StageAsync(ModVersionMetadata release, string folderName, string staging, UserContentStep? beforeMove, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var archive = Path.Combine(Path.GetTempPath(), $"borea-download-{Guid.NewGuid():N}.zip");
        try
        {
            await _downloader.DownloadAsync(release, archive, progress.ForDownload(release), cancellationToken).ConfigureAwait(false);
            progress.Report(release, InstallPhase.Extracting);
            var root = release.Install?.Root ?? ModArchive.DeriveRoot(archive, [UserContentFolder.MetadataFileName, UserContentFolder.GameFileName(release.Type)]);
            if (ModArchive.Extract(archive, root, staging) == 0)
                throw new InvalidOperationException($"The archive of '{release.ModId}' {release.Version} holds no files{(root is null ? "" : $" under '{root}'")}.");
        }
        finally
        {
            TryDeleteFile(archive);
        }

        UserContentFolder.CheckFiles(staging, release);
        UserContentFolder.Check(staging, release);
        if (!string.Equals(folderName, release.Install!.Folder, StringComparison.Ordinal))
        {
            var metadata = Path.Combine(staging, UserContentFolder.MetadataFileName);
            var renamed = FileGameSaveStore.WithName(await File.ReadAllTextAsync(metadata, cancellationToken).ConfigureAwait(false), folderName)
                ?? throw new InvalidOperationException($"Borea cannot change the name in the {UserContentFolder.MetadataFileName} of '{release.ModId}' without changing the rest of the file.");
            await AtomicFile.WriteAllTextAsync(metadata, renamed, cancellationToken).ConfigureAwait(false);
        }

        var files = await InstalledContentHashes.ComputeAsync(staging, cancellationToken).ConfigureAwait(false);
        if (beforeMove is not null)
            await beforeMove(progress, cancellationToken).ConfigureAwait(false);

        progress.Report(release, InstallPhase.Finishing);
        return files;
    }

    private async Task DropStaleAsync(Guid instanceId, ModVersionMetadata release, string folderName, CancellationToken cancellationToken)
    {
        await _records.RemoveAsync(instanceId, release.Type, folderName, cancellationToken).ConfigureAwait(false);
        foreach (var record in await _records.ListAsync(instanceId, cancellationToken).ConfigureAwait(false))
        {
            if (record.Type == release.Type && ModIds.Equals(record.ListingId, release.ModId))
                await _records.RemoveAsync(instanceId, record.Type, record.FolderName, cancellationToken).ConfigureAwait(false);
        }
    }

    private string StagingFolder(Guid instanceId) =>
        Path.Combine(_paths.GetInstanceRoot(instanceId), $"{GameSaveBackupFolder.WorkPrefix}install-{Guid.NewGuid():N}");

    private static void RequireInstallable(ModVersionMetadata release)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (!release.Type.IsVehicleOrSave())
            throw new NotSupportedException($"'{release.ModId}' is a {release.Type}, and only a vehicle or save installs as a folder under Vehicles or saves.");

        if (!ModArchive.IsZip(release.Download.ContentType))
            throw new NotSupportedException($"'{release.ModId}' is served as '{release.Download.ContentType}', and only a zip archive can be unpacked.");
    }

    private static void RequireSameListing(InstalledContentRecord installed, ModVersionMetadata release)
    {
        if (installed.Type != release.Type || !ModIds.Equals(installed.ListingId, release.ModId))
            throw new ArgumentException($"The release of '{release.ModId}' does not replace the record of '{installed.ListingId}'.", nameof(release));
    }

    private static void TryDeleteFolder(string path)
    {
        if (Directory.Exists(path))
            FileGameSaveStore.TryDelete(() => Directory.Delete(path, recursive: true));
    }

    private static void TryDeleteFile(string path) => FileGameSaveStore.TryDelete(() => File.Delete(path));
}
