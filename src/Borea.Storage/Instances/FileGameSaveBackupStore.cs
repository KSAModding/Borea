using System.IO.Compression;
using Borea.Core.Instances;
using Borea.Core.Paths;

namespace Borea.Storage.Instances;

/// <summary>
/// Reads what a backup was from its record, and from its name and folders
/// when it has none.
/// </summary>
public sealed class FileGameSaveBackupStore : IGameSaveBackupStore
{
    private const string MetadataFileName = "meta.toml";

    private static readonly EnumerationOptions SizeOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly IGamePathProvider _paths;
    private readonly GameSaveBackupFolder _backups;

    public FileGameSaveBackupStore(IGamePathProvider paths, TimeProvider? time = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _backups = new GameSaveBackupFolder(paths, time ?? TimeProvider.System);
    }

    public Task<IReadOnlyList<GameSaveBackup>> ListAsync(Guid instanceId, CancellationToken cancellationToken = default)
        => Task.Run(() => ListCoreAsync(instanceId, cancellationToken), cancellationToken);

    public Task<GameSaveRestoreOutcome> RestoreAsync(GameSaveBackup backup, bool replace, CancellationToken cancellationToken = default)
        => Task.Run(() => RestoreCoreAsync(backup, replace, cancellationToken), cancellationToken);

    public Task DeleteAsync(GameSaveBackup backup, CancellationToken cancellationToken = default)
        => Task.Run(() => Delete(Resolve(backup)), cancellationToken);

    public Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
        => Task.Run(async () =>
        {
            var root = new DirectoryInfo(_paths.GetBackupsRoot());
            if (!root.Exists)
                return 0;

            var deleted = 0;
            foreach (var folder in root.EnumerateDirectories())
            {
                if (!Guid.TryParse(folder.Name, out var instanceId))
                    continue;

                IReadOnlyList<GameSaveBackup> backups;
                try
                {
                    backups = await ListCoreAsync(instanceId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var backup in backups)
                {
                    if (backup.CreatedAt >= cutoff)
                        continue;

                    try
                    {
                        Delete(Resolve(backup));
                        deleted++;
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        // one backup that cannot be deleted now does not keep the others
                    }
                }
            }

            return deleted;
        }, cancellationToken);

    private async Task<IReadOnlyList<GameSaveBackup>> ListCoreAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        var root = new DirectoryInfo(_backups.InstanceFolder(instanceId));
        if (!root.Exists)
            return [];

        var backups = new List<GameSaveBackup>();
        foreach (var item in root.EnumerateFileSystemInfos())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsBookkeeping(item))
                continue;

            if (item is DirectoryInfo folder && KindOf(folder.Name) is { } kind)
            {
                foreach (var backup in folder.EnumerateFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsBookkeeping(backup) && await TryReadAsync(instanceId, root, backup, kind, cancellationToken).ConfigureAwait(false) is { } read)
                        backups.Add(read);
                }
            }
            else if (await TryReadAsync(instanceId, root, item, kind: null, cancellationToken).ConfigureAwait(false) is { } read)
            {
                backups.Add(read);
            }
        }

        return backups
            .OrderByDescending(backup => backup.CreatedAt)
            .ThenBy(backup => backup.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static GameSaveKind? KindOf(string folderName)
    {
        foreach (var kind in Enum.GetValues<GameSaveKind>())
        {
            if (string.Equals(GameSaveBackupFolder.FolderName(kind), folderName, StringComparison.OrdinalIgnoreCase))
                return kind;
        }

        return null;
    }

    private static bool IsBookkeeping(FileSystemInfo item)
        => item.Name.StartsWith(GameSaveBackupFolder.WorkPrefix, StringComparison.Ordinal)
            || (item is FileInfo && item.Name.Contains(GameSaveBackupFolder.RecordSuffix, StringComparison.OrdinalIgnoreCase));

    /// <summary>Null for a backup that was moved or deleted while the list was read.</summary>
    private static async Task<GameSaveBackup?> TryReadAsync(Guid instanceId, DirectoryInfo root, FileSystemInfo item, GameSaveKind? kind, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadAsync(instanceId, root, item, kind, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException) when (!Directory.Exists(item.FullName) && !File.Exists(item.FullName))
        {
            return null;
        }
    }

    private static async Task<GameSaveBackup> ReadAsync(Guid instanceId, DirectoryInfo root, FileSystemInfo item, GameSaveKind? kind, CancellationToken cancellationToken)
    {
        var id = Path.GetRelativePath(root.FullName, item.FullName).Replace(Path.DirectorySeparatorChar, '/');
        var isArchive = item is FileInfo && string.Equals(item.Extension, ".zip", StringComparison.OrdinalIgnoreCase);
        var size = item is FileInfo file ? file.Length : ((DirectoryInfo)item).EnumerateFiles("*", SizeOptions).Sum(entry => entry.Length);
        var folderTime = new DateTimeOffset(item.LastWriteTimeUtc);
        if (kind is null || (item is FileInfo && !isArchive))
            return new GameSaveBackup(instanceId, id, item.FullName, null, null, item.Name, folderTime, GameSaveBackupReason.Unknown, isArchive, size);

        var record = await GameSaveBackupFolder.TryReadRecordAsync(item.FullName, cancellationToken).ConfigureAwait(false);
        var parsed = GameSaveBackupFolder.ParseName(isArchive ? Path.GetFileNameWithoutExtension(item.Name) : item.Name);
        var folderName = GameSaveBackupFolder.IsFolderName(record?.Folder) ? record!.Folder : parsed?.FolderName;
        var reason = record is not null
            ? GameSaveBackupFolder.ReadReason(record.Reason)
            : isArchive && parsed is not null ? GameSaveBackupReason.BackedUp : GameSaveBackupReason.Unknown;
        return new GameSaveBackup(
            instanceId,
            id,
            item.FullName,
            folderName is null ? null : kind,
            folderName,
            ReadName(item, isArchive) ?? folderName ?? item.Name,
            record?.CreatedAt ?? parsed?.CreatedAt ?? folderTime,
            reason,
            isArchive,
            size);
    }

    /// <summary>The name in the meta.toml of the save or vehicle, in the folder or in the zip.</summary>
    private static string? ReadName(FileSystemInfo item, bool isArchive)
    {
        if (!isArchive)
            return NameOf(FileGameSaveStore.ReadMetadata(Path.Combine(item.FullName, MetadataFileName)).Name);

        try
        {
            using var zip = ZipFile.OpenRead(item.FullName);
            var metadata = zip.Entries.FirstOrDefault(entry => entry.FullName.Split('/') is [_, MetadataFileName] or [MetadataFileName]);
            if (metadata is null)
                return null;

            using var reader = new StreamReader(metadata.Open());
            return NameOf(FileGameSaveStore.ParseMetadata(reader.ReadToEnd()).Name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    private static string? NameOf(string? name) => string.IsNullOrWhiteSpace(name) ? null : name;

    /// <summary>The path of the backup below the Backups folder of its instance, from its id and never from its path.</summary>
    private string Resolve(GameSaveBackup backup)
    {
        ArgumentNullException.ThrowIfNull(backup);

        var root = Path.GetFullPath(_backups.InstanceFolder(backup.InstanceId));
        var path = Path.GetFullPath(Path.Combine(root, backup.Id));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, PathComparison)
            || Path.GetFileName(path).StartsWith(GameSaveBackupFolder.WorkPrefix, StringComparison.Ordinal))
            throw new ArgumentException($"{backup.Id} is not a backup of instance '{backup.InstanceId}'.", nameof(backup));

        if (!Directory.Exists(path) && !File.Exists(path))
            throw new FileNotFoundException($"The backup {backup.Id} does not exist any more.", path);

        return path;
    }

    private async Task<GameSaveRestoreOutcome> RestoreCoreAsync(GameSaveBackup backup, bool replace, CancellationToken cancellationToken)
    {
        var path = Resolve(backup);
        if (backup is not { Kind: { } kind, FolderName: { } folderName } || !GameSaveBackupFolder.IsFolderName(folderName))
            throw new InvalidOperationException($"Borea cannot tell where the backup {backup.Id} came from, so it cannot restore it.");

        var instanceRoot = _paths.GetInstanceRoot(backup.InstanceId);
        if (!Directory.Exists(instanceRoot))
            throw new DirectoryNotFoundException($"Instance '{backup.InstanceId}' has no folder at {instanceRoot}.");

        var folder = FileGameSaveStore.FindFolder(kind == GameSaveKind.Save ? _paths.GetInstanceSavesFolder(backup.InstanceId) : _paths.GetInstanceVehiclesFolder(backup.InstanceId));
        var target = Path.Combine(folder, folderName);
        var exists = Directory.Exists(target);
        if (exists && !replace)
            return GameSaveRestoreOutcome.Exists;

        // the game reads only the saves and Vehicles folders, so an extract that stops leaves nothing it would load
        var staging = backup.IsArchive ? Path.Combine(instanceRoot, GameSaveBackupFolder.WorkPrefix + "restore-" + Guid.NewGuid().ToString("N")) : null;
        try
        {
            var source = staging is null ? path : Extract(path, staging, cancellationToken);
            Directory.CreateDirectory(folder);
            var replaced = exists ? await _backups.MoveInAsync(backup.InstanceId, kind, target, GameSaveBackupReason.Replaced).ConfigureAwait(false) : null;
            try
            {
                Directory.Move(source, target);
            }
            catch when (replaced is not null)
            {
                Directory.Move(replaced, target);
                GameSaveBackupFolder.TryDeleteRecord(replaced);
                throw;
            }

            if (!backup.IsArchive)
                GameSaveBackupFolder.TryDeleteRecord(path);
        }
        finally
        {
            if (staging is not null && Directory.Exists(staging))
                FileGameSaveStore.TryDelete(() => Directory.Delete(staging, recursive: true));
        }

        return GameSaveRestoreOutcome.Restored;
    }

    /// <summary>Back up zips the folder with its name at the top, so that one folder is what comes back.</summary>
    private static string Extract(string zip, string staging, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            ZipFile.ExtractToDirectory(zip, staging);
        }
        catch (InvalidDataException exception)
        {
            throw new IOException($"{zip} is not a zip that Borea can read. {exception.Message}", exception);
        }

        var folders = Directory.GetDirectories(staging);
        return folders.Length == 1 && Directory.GetFiles(staging).Length == 0 ? folders[0] : staging;
    }

    /// <summary>
    /// Renames the backup first, so a delete that stops halfway leaves no
    /// partial backup in the list. The next delete clears such a leftover.
    /// </summary>
    private static void Delete(string path)
    {
        var parent = Path.GetDirectoryName(path)!;
        foreach (var leftover in Directory.EnumerateFileSystemEntries(parent, GameSaveBackupFolder.WorkPrefix + "delete-*"))
            FileGameSaveStore.TryDelete(() => DeleteEntry(leftover));

        var trash = Path.Combine(parent, GameSaveBackupFolder.WorkPrefix + "delete-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(path))
            Directory.Move(path, trash);
        else
            File.Move(path, trash);

        GameSaveBackupFolder.TryDeleteRecord(path);
        DeleteEntry(trash);
    }

    private static void DeleteEntry(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        else
            File.Delete(path);
    }
}
