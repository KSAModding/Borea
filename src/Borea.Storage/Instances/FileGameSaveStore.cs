using System.IO.Compression;
using Borea.Core.Instances;
using Borea.Core.Paths;
using Tomlyn;
using Tomlyn.Model;

namespace Borea.Storage.Instances;

/// <summary>
/// Reads each folder the way SaveMetaData.FromDirectory does.
/// </summary>
public sealed class FileGameSaveStore : IGameSaveStore
{
    private const string MetadataFileName = "meta.toml";

    private static readonly EnumerationOptions SizeOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private static readonly EnumerationOptions EveryEntry = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly IGamePathProvider _paths;
    private readonly GameSaveBackupFolder _backups;

    public FileGameSaveStore(IGamePathProvider paths, TimeProvider? time = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _backups = new GameSaveBackupFolder(paths, time ?? TimeProvider.System);
    }

    public string GetFolder(Guid instanceId, GameSaveKind kind)
        => FindFolder(kind == GameSaveKind.Save ? _paths.GetInstanceSavesFolder(instanceId) : _paths.GetInstanceVehiclesFolder(instanceId));

    public Task<IReadOnlyList<GameSaveEntry>> ListAsync(Guid instanceId, GameSaveKind kind, CancellationToken cancellationToken = default)
        => Task.Run(() => List(GetFolder(instanceId, kind), kind, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<GameSaveEntry>> ListSharedProfileAsync(GameSaveKind kind, CancellationToken cancellationToken = default)
        => Task.Run(() => List(GetSharedProfileFolder(kind), kind, cancellationToken), cancellationToken);

    public Task<bool> HasSharedProfileItemsAsync(GameSaveKind kind, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            var directory = new DirectoryInfo(GetSharedProfileFolder(kind));
            return directory.Exists && directory.EnumerateDirectories().Any();
        }, cancellationToken);

    public Task<string> BackUpAsync(Guid instanceId, GameSaveEntry entry, CancellationToken cancellationToken = default)
        => Task.Run(() => BackUpCoreAsync(instanceId, RequireInInstance(instanceId, entry), _backups.Now(), cancellationToken), cancellationToken);

    public Task<IReadOnlyList<string>> BackUpAllAsync(Guid instanceId, GameSaveKind kind, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<string>>(async () =>
        {
            var now = _backups.Now();
            var zips = new List<string>();
            try
            {
                foreach (var entry in List(GetFolder(instanceId, kind), kind, cancellationToken))
                    zips.Add(await BackUpCoreAsync(instanceId, entry, now, cancellationToken).ConfigureAwait(false));
            }
            catch
            {
                foreach (var zip in zips)
                {
                    TryDelete(() => File.Delete(zip));
                    GameSaveBackupFolder.TryDeleteRecord(zip);
                }

                throw;
            }

            return zips;
        }, cancellationToken);

    public Task<GameSaveCopyOutcome> CopyAsync(GameSaveEntry entry, Guid targetInstanceId, bool replace, CancellationToken cancellationToken = default)
        => Task.Run(() => CopyCoreAsync(entry, targetInstanceId, replace, cancellationToken), cancellationToken);

    public Task<string> DeleteAsync(Guid instanceId, GameSaveEntry entry, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            RequireInInstance(instanceId, entry);
            return _backups.MoveInAsync(instanceId, entry.Kind, entry.Path, GameSaveBackupReason.Deleted);
        }, cancellationToken);

    private string GetSharedProfileFolder(GameSaveKind kind) => FindFolder(Path.Combine(_paths.GetSharedProfileRoot(), FolderName(kind)));

    private static string FolderName(GameSaveKind kind) => GameSaveBackupFolder.FolderName(kind);

    /// <summary>
    /// The folder in any letter case, because a profile can hold "vehicles"
    /// instead of "Vehicles". The exact spelling wins.
    /// </summary>
    internal static string FindFolder(string folder)
    {
        var parent = new DirectoryInfo(Path.GetDirectoryName(folder)!);
        if (!parent.Exists)
            return folder;

        var name = Path.GetFileName(folder);
        var matches = parent.EnumerateDirectories().Where(directory => string.Equals(directory.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        return (matches.FirstOrDefault(directory => string.Equals(directory.Name, name, StringComparison.Ordinal)) ?? matches.FirstOrDefault())?.FullName ?? folder;
    }

    private static IReadOnlyList<GameSaveEntry> List(string folder, GameSaveKind kind, CancellationToken cancellationToken)
    {
        var directory = new DirectoryInfo(folder);
        if (!directory.Exists)
            return [];

        var entries = new List<GameSaveEntry>();
        foreach (var item in directory.EnumerateDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(Read(item, kind));
        }

        return entries
            .OrderByDescending(entry => entry.UpdatedAt)
            .ThenBy(entry => entry.FolderName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static GameSaveEntry Read(DirectoryInfo folder, GameSaveKind kind)
    {
        var (name, updated, build) = ReadMetadata(Path.Combine(folder.FullName, MetadataFileName));
        return new GameSaveEntry(
            kind,
            folder.Name,
            folder.FullName,
            string.IsNullOrWhiteSpace(name) ? folder.Name : name,
            updated ?? new DateTimeOffset(folder.LastWriteTimeUtc),
            string.IsNullOrWhiteSpace(build) ? null : build,
            folder.EnumerateFiles("*", SizeOptions).Sum(file => file.Length));
    }

    internal static (string? Name, DateTimeOffset? Updated, string? Build) ReadMetadata(string path)
    {
        try
        {
            return ParseMetadata(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return default;
        }
    }

    internal static (string? Name, DateTimeOffset? Updated, string? Build) ParseMetadata(string text)
    {
        TomlTable table;
        try
        {
            table = TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
        }
        catch (TomlException)
        {
            return default;
        }

        return (Text(table, "name"), Updated(table), Text(table, "version"));
    }

    private static string? Text(TomlTable table, string key) => table.TryGetValue(key, out var value) ? value as string : null;

    private static DateTimeOffset? Updated(TomlTable table)
    {
        if (!table.TryGetValue("updated", out var value) || value is not TomlDateTime updated)
            return null;

        // SaveMetaData.Write stores DateTime.UtcNow, and the file carries no offset
        return updated.Kind is TomlDateTimeKind.OffsetDateTimeByNumber or TomlDateTimeKind.OffsetDateTimeByZ
            ? updated.DateTime
            : new DateTimeOffset(DateTime.SpecifyKind(updated.DateTime.DateTime, DateTimeKind.Utc));
    }

    private GameSaveEntry RequireInInstance(Guid instanceId, GameSaveEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var parent = Path.GetDirectoryName(Path.GetFullPath(entry.Path));
        if (parent is null || !PathComparer.Equals(parent, Path.GetFullPath(GetFolder(instanceId, entry.Kind))))
            throw new ArgumentException($"{entry.Path} is not in the {FolderName(entry.Kind)} folder of instance '{instanceId}'.", nameof(entry));

        if (!Directory.Exists(entry.Path))
            throw new DirectoryNotFoundException($"{entry.Path} does not exist.");

        return entry;
    }

    private async Task<string> BackUpCoreAsync(Guid instanceId, GameSaveEntry entry, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var folder = Directory.CreateDirectory(_backups.KindFolder(instanceId, entry.Kind)).FullName;
        var folderName = Path.GetFileName(entry.Path);
        for (var attempt = 1; ; attempt++)
        {
            var path = Path.Combine(folder, GameSaveBackupFolder.Name(folderName, GameSaveBackupFolder.Stamp(now), attempt) + ".zip");
            FileStream stream;
            try
            {
                stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }
            catch (IOException) when (File.Exists(path))
            {
                continue;
            }

            try
            {
                using (stream)
                    ZipFile.CreateFromDirectory(entry.Path, stream, CompressionLevel.Optimal, includeBaseDirectory: true);
            }
            catch
            {
                TryDelete(() => File.Delete(path));
                throw;
            }

            await GameSaveBackupFolder.TryWriteRecordAsync(path, folderName, now, GameSaveBackupReason.BackedUp).ConfigureAwait(false);
            return path;
        }
    }

    private async Task<GameSaveCopyOutcome> CopyCoreAsync(GameSaveEntry entry, Guid targetInstanceId, bool replace, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var source = new DirectoryInfo(entry.Path);
        if (!source.Exists)
            throw new DirectoryNotFoundException($"{entry.Path} does not exist.");

        var instanceRoot = _paths.GetInstanceRoot(targetInstanceId);
        if (!Directory.Exists(instanceRoot))
            throw new DirectoryNotFoundException($"Instance '{targetInstanceId}' has no folder at {instanceRoot}.");

        var folder = GetFolder(targetInstanceId, entry.Kind);
        var target = Path.Combine(folder, source.Name);
        if (PathComparer.Equals(Path.GetFullPath(target), source.FullName))
            throw new InvalidOperationException($"{entry.Path} is already in instance '{targetInstanceId}'.");

        var exists = Directory.Exists(target);
        if (exists && !replace)
            return GameSaveCopyOutcome.Exists;

        // the game reads only the saves and Vehicles folders, so a copy that stops leaves nothing it would load
        var staging = Path.Combine(instanceRoot, ".borea-copy-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyFolder(source, staging, cancellationToken);
            Directory.CreateDirectory(folder);
            var replaced = exists ? await _backups.MoveInAsync(targetInstanceId, entry.Kind, target, GameSaveBackupReason.Replaced).ConfigureAwait(false) : null;
            try
            {
                Directory.Move(staging, target);
            }
            catch when (replaced is not null)
            {
                Directory.Move(replaced, target);
                GameSaveBackupFolder.TryDeleteRecord(replaced);
                throw;
            }
        }
        finally
        {
            if (Directory.Exists(staging))
                TryDelete(() => Directory.Delete(staging, recursive: true));
        }

        return GameSaveCopyOutcome.Copied;
    }

    private static void CopyFolder(DirectoryInfo source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        foreach (var item in source.EnumerateFileSystemInfos("*", EveryEntry))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetRelativePath(source.FullName, item.FullName));
            if (item is FileInfo file)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                CopyFile(file, target);
            }
            else
            {
                Directory.CreateDirectory(target);
            }
        }
    }

    /// <summary>On Windows, a file that another program is writing fails the copy instead of arriving half written.</summary>
    private static void CopyFile(FileInfo source, string target)
    {
        using (var input = source.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            input.CopyTo(output);

        File.SetLastWriteTimeUtc(target, source.LastWriteTimeUtc);
    }

    internal static void TryDelete(Action delete)
    {
        try
        {
            delete();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // the original failure matters more than the leftover
        }
    }
}
