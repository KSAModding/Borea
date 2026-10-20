using System.IO.Compression;
using System.Text.RegularExpressions;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Storage.Files;
using Tomlyn;
using Tomlyn.Model;

namespace Borea.Storage.Instances;

/// <summary>
/// Reads each folder the way SaveMetaData.FromDirectory does. Keeps the install records of
/// <see cref="IInstalledContentRecords"/> true: a renamed folder keeps its record, and a folder
/// that is copied over or deleted loses it, because a folder without a record is the player's own.
/// </summary>
public sealed partial class FileGameSaveStore : IGameSaveStore
{
    internal const string MetadataFileName = "meta.toml";

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

    internal static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly IGamePathProvider _paths;
    private readonly GameSaveBackupFolder _backups;
    private readonly IInstalledContentRecords _records;

    /// <param name="records">Pass the one store of the app, so two stores never write one file.</param>
    public FileGameSaveStore(IGamePathProvider paths, TimeProvider? time = null, IInstalledContentRecords? records = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _backups = new GameSaveBackupFolder(paths, time ?? TimeProvider.System);
        _records = records ?? new FileInstalledContentRecords(paths);
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
        => Task.Run(async () =>
        {
            RequireInInstance(instanceId, entry);
            await _records.RemoveAsync(instanceId, ContentTypeOf(entry.Kind), entry.FolderName, cancellationToken).ConfigureAwait(false);
            return await _backups.MoveInAsync(instanceId, entry.Kind, entry.Path, GameSaveBackupReason.Deleted).ConfigureAwait(false);
        }, cancellationToken);

    public Task<GameSaveRenameOutcome> RenameAsync(Guid instanceId, GameSaveEntry entry, string newName, CancellationToken cancellationToken = default)
        => Task.Run(() => RenameCoreAsync(instanceId, RequireInInstance(instanceId, entry), newName, cancellationToken), cancellationToken);

    public Task<GameSaveArchive> WriteShareArchiveAsync(Guid instanceId, GameSaveEntry entry, string name, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(destination);
        return Task.Run(() => WriteShareArchive(RequireInInstance(instanceId, entry), name, destination, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Reads the name again, because the player may have played since the share was checked, and the release checks take the
    /// top-level directory as the name the game shows.
    /// </summary>
    private static GameSaveArchive WriteShareArchive(GameSaveEntry entry, string name, Stream destination, CancellationToken cancellationToken)
    {
        var metadata = Path.Combine(entry.Path, MetadataFileName);
        if (!string.Equals(ReadMetadata(metadata).Name, name, StringComparison.Ordinal))
            throw new InvalidOperationException($"The name in {metadata} is no longer '{name}'.");

        var source = new DirectoryInfo(entry.Path);
        using var hashing = new HashingWriteStream(destination);
        using (var zip = new ZipArchive(hashing, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in source.EnumerateFileSystemInfos("*", EveryEntry).OrderBy(item => item.FullName, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = $"{name}/{Path.GetRelativePath(source.FullName, item.FullName).Replace(Path.DirectorySeparatorChar, '/')}";
                if (item is FileInfo file)
                {
                    var archived = zip.CreateEntry(path, CompressionLevel.Optimal);
                    archived.LastWriteTime = file.LastWriteTime;
                    using var input = file.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var output = archived.Open();
                    input.CopyTo(output);
                }
                else if (item is DirectoryInfo directory && !directory.EnumerateFileSystemInfos().Any())
                {
                    zip.CreateEntry(path + "/");
                }
            }
        }

        return new GameSaveArchive(hashing.Sha256(), hashing.Length);
    }

    private static ContentType ContentTypeOf(GameSaveKind kind) => kind == GameSaveKind.Save ? ContentType.Save : ContentType.Vehicle;

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
        => ParseTable(text) is { } table ? (Text(table, "name"), Updated(table), Text(table, "version")) : default;

    private static TomlTable? ParseTable(string text)
    {
        try
        {
            return TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
        }
        catch (TomlException)
        {
            return null;
        }
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

            // The record goes first: a folder that loses it only makes Borea more careful, and the copy is the player's own.
            await _records.RemoveAsync(targetInstanceId, ContentTypeOf(entry.Kind), source.Name, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Writes meta.toml first and moves the folder second, because the move is the
    /// step that fails when a file in the folder is open. The install record follows
    /// last. A failed move or record write moves the folder back and writes the old
    /// meta.toml back.
    /// </summary>
    private async Task<GameSaveRenameOutcome> RenameCoreAsync(Guid instanceId, GameSaveEntry entry, string newName, CancellationToken cancellationToken)
    {
        if (!GameSaveName.IsValid(newName))
            return GameSaveRenameOutcome.InvalidName;

        var source = new DirectoryInfo(entry.Path);
        var folder = source.Parent!.FullName;
        var others = List(folder, entry.Kind, cancellationToken).Where(other => !PathComparer.Equals(other.FolderName, source.Name));
        if (others.Any(other => string.Equals(other.Name, newName, StringComparison.OrdinalIgnoreCase) || string.Equals(other.FolderName, newName, StringComparison.OrdinalIgnoreCase)))
            return GameSaveRenameOutcome.NameTaken;

        // No folder has the new name, so a record of it belongs to a folder the player deleted in the game.
        if (!string.Equals(source.Name, newName, StringComparison.OrdinalIgnoreCase))
            await _records.RemoveAsync(instanceId, ContentTypeOf(entry.Kind), newName, cancellationToken).ConfigureAwait(false);

        var metadataPath = Path.Combine(source.FullName, MetadataFileName);
        byte[] original;
        try
        {
            original = File.ReadAllBytes(metadataPath);
        }
        catch (FileNotFoundException exception)
        {
            throw new InvalidOperationException($"{source.FullName} has no {MetadataFileName}, so the game does not list it and there is no name to change.", exception);
        }

        string text;
        using (var reader = new StreamReader(new MemoryStream(original)))
            text = reader.ReadToEnd();

        var renamed = WithName(text, newName)
            ?? throw new InvalidOperationException($"Borea cannot change the name in {metadataPath} without changing the rest of the file.");

        cancellationToken.ThrowIfCancellationRequested();
        await AtomicFile.WriteAllTextAsync(metadataPath, renamed, cancellationToken).ConfigureAwait(false);
        var target = Path.Combine(folder, newName);
        var moved = false;
        try
        {
            if (!string.Equals(source.Name, newName, StringComparison.Ordinal))
            {
                Directory.Move(source.FullName, target);
                moved = true;
            }

            // The hashes stay, so the renamed item counts as changed since the install.
            await _records.RenameFolderAsync(instanceId, ContentTypeOf(entry.Kind), source.Name, newName, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (moved)
                Directory.Move(target, source.FullName);

            await AtomicFile.WriteAllBytesAsync(metadataPath, original).ConfigureAwait(false);
            throw;
        }

        return GameSaveRenameOutcome.Renamed;
    }

    [GeneratedRegex(@"^[ \t]*name[ \t]*=[ \t]*(?:""(?:[^""\\\r\n]|\\.)*""|'[^'\r\n]*')", RegexOptions.Multiline)]
    private static partial Regex NameLine();

    /// <summary>
    /// The text with only the value of the top-level "name" changed, so the file keeps
    /// every other value in the form the game wrote it. Null when the text is not TOML,
    /// or when the edit would change anything else.
    /// </summary>
    internal static string? WithName(string text, string name)
    {
        if (ParseTable(text) is not { } before)
            return null;

        // a name is letters, digits, spaces, "-" and "_", so it needs no escapes
        var value = $"name = \"{name}\"";
        var line = NameLine().Match(text);
        var renamed = line.Success
            ? string.Concat(text.AsSpan(0, line.Index), value, text.AsSpan(line.Index + line.Length))
            : value + (text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n") + text;

        if (ParseTable(renamed) is not { } after || !string.Equals(Text(after, "name"), name, StringComparison.Ordinal))
            return null;

        before.Remove("name");
        after.Remove("name");
        return string.Equals(TomlSerializer.Serialize(before), TomlSerializer.Serialize(after), StringComparison.Ordinal) ? renamed : null;
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
