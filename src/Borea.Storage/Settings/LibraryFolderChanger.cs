using System.Diagnostics;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Paths;
using Borea.Core.Settings;
using Borea.Storage.Instances;
using Borea.Storage.Launch;
using Borea.Storage.Paths;

namespace Borea.Storage.Settings;

public sealed class LibraryFolderChanger : ILibraryFolderChanger
{
    private const string InstancesFolderName = "Instances";
    private const string BackupsFolderName = "Backups";
    private const int CopyBufferSize = 1 << 20;
    private const int MaxLinkDepth = 32;
    private const string StarMapProcessName = "StarMap";
    private static readonly string[] BoreaProcessNames = ["Borea.App", "borea"];

    // StarMap's GameSurveyer.TryLoadCoreAndGame loads the game assembly into the StarMap process
    private static readonly string[] GameProcessNames = SharedProfileLauncher.CurrentPlatform() is { } platform && GameExecutable.FileName(platform) is { } game
        ? [Path.GetFileNameWithoutExtension(game), StarMapProcessName]
        : [StarMapProcessName];

    private static readonly EnumerationOptions EveryEntry = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
    };

    private readonly IBoreaSettingsRepository _settings;
    private readonly IGamePathProvider _paths;
    private readonly string _defaultFolder;
    private readonly ILauncher _launcher;
    private readonly IInstanceLocks _locks;
    private readonly Func<bool> _isGameProcessRunning;
    private readonly Func<bool> _isOtherBoreaRunning;
    private readonly Func<string, string, bool> _isSameVolume;
    private readonly Action<string, string> _moveDirectory;

    /// <param name="defaultFolder">Where the Instances and Backups folders are when no library folder is saved.</param>
    /// <param name="isGameProcessRunning">Null looks for a KSA or StarMap process.</param>
    /// <param name="isOtherBoreaRunning">Null looks for a Borea App or command other than this process.</param>
    /// <param name="isSameVolume">Whether two folders are on one volume, so a rename can move between them. Null compares their mount points.</param>
    public LibraryFolderChanger(
        IBoreaSettingsRepository settings,
        IGamePathProvider paths,
        string defaultFolder,
        ILauncher launcher,
        IInstanceLocks locks,
        Func<bool>? isGameProcessRunning = null,
        Func<bool>? isOtherBoreaRunning = null,
        Func<string, string, bool>? isSameVolume = null)
        : this(settings, paths, defaultFolder, launcher, locks, isGameProcessRunning, isOtherBoreaRunning, isSameVolume, Directory.Move)
    {
    }

    /// <param name="moveDirectory">Renames a folder within one volume.</param>
    internal LibraryFolderChanger(
        IBoreaSettingsRepository settings,
        IGamePathProvider paths,
        string defaultFolder,
        ILauncher launcher,
        IInstanceLocks locks,
        Func<bool>? isGameProcessRunning,
        Func<bool>? isOtherBoreaRunning,
        Func<string, string, bool>? isSameVolume,
        Action<string, string> moveDirectory)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        if (string.IsNullOrWhiteSpace(defaultFolder) || !Path.IsPathFullyQualified(defaultFolder))
            throw new ArgumentException("The default library folder must be an absolute path.", nameof(defaultFolder));

        _defaultFolder = Normalize(defaultFolder);
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _locks = locks ?? throw new ArgumentNullException(nameof(locks));
        _isGameProcessRunning = isGameProcessRunning ?? (() => IsProcessRunning(GameProcessNames));
        _isOtherBoreaRunning = isOtherBoreaRunning ?? (() => IsProcessRunning(BoreaProcessNames));
        _isSameVolume = isSameVolume ?? OnSameVolume;
        _moveDirectory = moveDirectory ?? throw new ArgumentNullException(nameof(moveDirectory));
    }

    public async Task<LibraryFolderChangeResult> ChangeAsync(
        string? folder,
        IProgress<LibraryMoveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var saved = await _settings.GetAsync(cancellationToken).ConfigureAwait(false)
            ?? new BoreaSettings(gameDirectoryPath: null);
        var previous = saved.LibraryFolderPath is null ? _defaultFolder : Normalize(saved.LibraryFolderPath);
        var requested = folder ?? _defaultFolder;

        if (string.IsNullOrWhiteSpace(requested) || !Path.IsPathFullyQualified(requested))
            return Refused(LibraryFolderChangeOutcome.NotAbsolute, requested, previous, $"The library folder must be an absolute path: {requested}");

        var target = Normalize(requested);
        if (CheckPlace(target, previous, saved.GameDirectoryPath) is { } refusal)
            return refusal;

        var created = CreateFolder(target);
        if (created is null)
            return Refused(LibraryFolderChangeOutcome.NotWritable, target, previous, $"Borea cannot create or write to {target}.");

        var changed = false;
        try
        {
            if (!CanWrite(target))
                return Refused(LibraryFolderChangeOutcome.NotWritable, target, previous, $"Borea cannot create or write to {target}.");

            var result = await ChangeCoreAsync(saved, previous, target, progress, cancellationToken).ConfigureAwait(false);
            changed = result.Changed;
            return result;
        }
        finally
        {
            if (!changed)
                RemoveEmptyFolders(created);
        }
    }

    private LibraryFolderChangeResult? CheckPlace(string target, string previous, string? gameDirectory)
    {
        if (File.Exists(target))
            return Refused(LibraryFolderChangeOutcome.IsFile, target, previous, $"{target} is a file, not a folder.");

        var real = Resolve(target);
        var realPrevious = Resolve(previous);
        if (SamePath(real, realPrevious))
            return Refused(LibraryFolderChangeOutcome.CurrentLibrary, target, previous, $"The library is already in {target}.");

        if (IsInside(real, realPrevious))
            return Refused(LibraryFolderChangeOutcome.InsideCurrentLibrary, target, previous, $"{target} is inside the current library folder {previous}.");

        if (IsInside(realPrevious, real))
            return Refused(LibraryFolderChangeOutcome.ContainsCurrentLibrary, target, previous, $"{target} contains the current library folder {previous}.");

        var realDefault = Resolve(_defaultFolder);
        if (IsInside(real, realDefault))
            return Refused(LibraryFolderChangeOutcome.InsideBoreaFolder, target, previous, $"{target} is inside Borea's own folder {_defaultFolder}. Choose that folder itself or a folder outside it.");

        if (IsInside(realDefault, real))
            return Refused(LibraryFolderChangeOutcome.ContainsBoreaFolder, target, previous, $"{target} contains Borea's own folder {_defaultFolder}. Choose a folder that does not contain it.");

        if (gameDirectory is not null && Path.IsPathFullyQualified(gameDirectory) && IsSameOrInside(real, Resolve(Normalize(gameDirectory))))
            return Refused(LibraryFolderChangeOutcome.InsideGameDirectory, target, previous, $"{target} is inside the game folder {Normalize(gameDirectory)}. Choose a folder outside it.");

        var sharedProfile = Normalize(_paths.GetSharedProfileRoot());
        if (IsSameOrInside(real, Resolve(sharedProfile)))
            return Refused(LibraryFolderChangeOutcome.InsideSharedProfile, target, previous, $"{target} is inside the game's profile folder {sharedProfile}. Choose a folder outside it.");

        return null;
    }

    private async Task<LibraryFolderChangeResult> ChangeCoreAsync(
        BoreaSettings saved,
        string previous,
        string target,
        IProgress<LibraryMoveProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (_isOtherBoreaRunning())
            return Refused(LibraryFolderChangeOutcome.BoreaRunning, target, previous, "Another Borea window or command is running. Close it first, then change the library folder.");

        var changedSettings = saved.WithLibraryFolder(SamePath(target, _defaultFolder) ? null : target);
        var previousHasInstances = (await ReadInstancesAsync(previous).ConfigureAwait(false)).Count > 0;
        var targetHasInstances = (await ReadInstancesAsync(target).ConfigureAwait(false)).Count > 0;

        if (targetHasInstances)
        {
            if (previousHasInstances)
                return Refused(LibraryFolderChangeOutcome.BothHaveInstances, target, previous, $"Both {previous} and {target} hold instances. Borea does not merge libraries.");

            await _settings.SaveAsync(changedSettings, cancellationToken).ConfigureAwait(false);
            return new LibraryFolderChangeResult(LibraryFolderChangeOutcome.Adopted, target, previous, $"Borea now uses the library in {target}.");
        }

        var moves = new[] { InstancesFolderName, BackupsFolderName }
            .Select(name => new FolderMove(Path.Combine(previous, name), Path.Combine(target, name)))
            .ToList();
        if (moves.Any(move => HasEntries(move.To)))
            return Refused(LibraryFolderChangeOutcome.TargetNotEmpty, target, previous, $"{target} already has an Instances or Backups folder without a Borea instance. Choose another folder.");

        moves.RemoveAll(move => !Directory.Exists(move.From));
        var instanceIds = InstanceFolderIds(previous);
        if (moves.Count > 0 && (instanceIds.Any(_launcher.IsRunning) || _isGameProcessRunning()))
            return Refused(LibraryFolderChangeOutcome.GameRunning, target, previous, "The game is running. Close the game first, then change the library folder.");

        using var held = _locks.TryHold(instanceIds);
        if (held is null)
            return Refused(LibraryFolderChangeOutcome.InstanceBusy, target, previous, "An instance is being changed. Wait until that finishes, then change the library folder.");

        var entries = moves.Select(move => new FolderCopy(move, Scan(move.From))).ToList();
        if (FindLockedFile(entries) is { } locked)
        {
            return new LibraryFolderChangeResult(LibraryFolderChangeOutcome.FileLocked, target, previous, $"{locked} is open in another program. Close the game first, then change the library folder.")
            {
                LockedFile = locked,
            };
        }

        foreach (var move in moves.Where(move => Directory.Exists(move.To)))
            Directory.Delete(move.To);

        cancellationToken.ThrowIfCancellationRequested();

        var message = $"Moved the instances and backups to {target}.";
        if (moves.Count == 0)
        {
            await _settings.SaveAsync(changedSettings, cancellationToken).ConfigureAwait(false);
            return new LibraryFolderChangeResult(LibraryFolderChangeOutcome.Moved, target, previous, message);
        }

        if (_isSameVolume(previous, target) && await TryRenameAsync(moves, changedSettings).ConfigureAwait(false))
            return new LibraryFolderChangeResult(LibraryFolderChangeOutcome.Moved, target, previous, message);

        await CopyAsync(entries, changedSettings, progress, cancellationToken).ConfigureAwait(false);
        if (RemoveOldFolders(entries, progress))
            return new LibraryFolderChangeResult(LibraryFolderChangeOutcome.Moved, target, previous, message);

        return new LibraryFolderChangeResult(LibraryFolderChangeOutcome.Moved, target, previous, $"{message} Borea could not delete every old file in {previous}.")
        {
            OldFilesRemain = true,
        };
    }

    /// <summary>False when the first folder cannot be renamed, which moves nothing, so a copy can take over.</summary>
    private async Task<bool> TryRenameAsync(IReadOnlyList<FolderMove> moves, BoreaSettings changedSettings)
    {
        var renamed = new List<FolderMove>();
        try
        {
            foreach (var move in moves)
            {
                try
                {
                    _moveDirectory(move.From, move.To);
                }
                catch (Exception exception) when (renamed.Count == 0 && exception is IOException or UnauthorizedAccessException)
                {
                    return false;
                }

                renamed.Add(move);
            }

            await _settings.SaveAsync(changedSettings, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception failure)
        {
            var rollbackFailures = new List<Exception>();
            foreach (var move in Enumerable.Reverse(renamed))
            {
                try
                {
                    _moveDirectory(move.To, move.From);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    rollbackFailures.Add(new IOException($"Borea could not move {move.To} back to {move.From}.", exception));
                }
            }

            ThrowWithRollbackFailures(failure, rollbackFailures);
            throw;
        }
    }

    private async Task CopyAsync(
        IReadOnlyList<FolderCopy> copies,
        BoreaSettings changedSettings,
        IProgress<LibraryMoveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var (totalBytes, totalFiles) = Totals(copies);
        var report = new CopyReport(progress, totalBytes, totalFiles);
        var started = new List<string>();
        try
        {
            report.Send();
            foreach (var (move, entries) in copies)
            {
                started.Add(move.To);
                Directory.CreateDirectory(move.To);
                foreach (var entry in entries)
                    await CopyEntryAsync(move, entry, report, cancellationToken).ConfigureAwait(false);
            }

            foreach (var (move, entries) in copies)
            {
                if (!Matches(entries, Scan(move.To)))
                    throw new IOException($"The copy in {move.To} does not match {move.From}.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            await _settings.SaveAsync(changedSettings, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            var rollbackFailures = new List<Exception>();
            foreach (var folder in started)
            {
                try
                {
                    DeleteTree(folder);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    rollbackFailures.Add(new IOException($"Borea could not delete the partial copy in {folder}.", exception));
                }
            }

            ThrowWithRollbackFailures(failure, rollbackFailures);
            throw;
        }
    }

    private static async Task CopyEntryAsync(FolderMove move, FolderEntry entry, CopyReport report, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var destination = Path.Combine(move.To, entry.RelativePath);
        switch (entry.Kind)
        {
            case EntryKind.Directory:
                Directory.CreateDirectory(destination);
                break;
            case EntryKind.DirectoryLink:
                Directory.CreateSymbolicLink(destination, entry.LinkTarget!);
                break;
            case EntryKind.FileLink:
                File.CreateSymbolicLink(destination, entry.LinkTarget!);
                break;
            default:
                await CopyFileAsync(Path.Combine(move.From, entry.RelativePath), destination, report, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private static async Task CopyFileAsync(string source, string destination, CopyReport report, CancellationToken cancellationToken)
    {
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize, FileOptions.Asynchronous))
        {
            var buffer = new byte[CopyBufferSize];
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                report.AddBytes(read);
            }
        }

        // the last played time falls back to when the game last wrote its log
        File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
        report.AddFile();
    }

    private static bool RemoveOldFolders(IReadOnlyList<FolderCopy> copies, IProgress<LibraryMoveProgress>? progress)
    {
        var (totalBytes, totalFiles) = Totals(copies);
        progress?.Report(new LibraryMoveProgress(LibraryMoveStage.RemovingOldFiles, totalBytes, totalBytes, totalFiles, totalFiles));

        var removed = true;
        foreach (var (move, _) in copies)
        {
            try
            {
                DeleteTree(move.From);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                removed = false;
            }
        }

        return removed;
    }

    private static void ThrowWithRollbackFailures(Exception failure, List<Exception> rollbackFailures)
    {
        if (rollbackFailures.Count > 0)
        {
            throw new AggregateException(
                "The library folder change failed, and Borea could not restore the previous library completely.",
                new[] { failure }.Concat(rollbackFailures));
        }
    }

    private static async Task<IReadOnlyList<Instance>> ReadInstancesAsync(string folder)
        => await new FileInstanceRepository(new GamePathProvider(gameDirectory: null, boreaRoot: folder, libraryFolder: folder))
            .GetAllAsync().ConfigureAwait(false);

    private static List<Guid> InstanceFolderIds(string folder)
    {
        var root = Path.Combine(folder, InstancesFolderName);
        if (!Directory.Exists(root))
            return [];

        return Directory.EnumerateDirectories(root)
            .Select(Path.GetFileName)
            .Select(name => Guid.TryParse(name, out var id) ? id : (Guid?)null)
            .OfType<Guid>()
            .ToList();
    }

    private static List<FolderEntry> Scan(string root)
    {
        var entries = new List<FolderEntry>();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        while (pending.Count > 0)
        {
            foreach (var info in pending.Pop().EnumerateFileSystemInfos("*", EveryEntry))
            {
                var relative = Path.GetRelativePath(root, info.FullName);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) && info.LinkTarget is { } linkTarget)
                {
                    entries.Add(new FolderEntry(relative, info is DirectoryInfo ? EntryKind.DirectoryLink : EntryKind.FileLink, 0, linkTarget));
                }
                else if (info is DirectoryInfo directory)
                {
                    entries.Add(new FolderEntry(relative, EntryKind.Directory, 0, null));
                    pending.Push(directory);
                }
                else
                {
                    entries.Add(new FolderEntry(relative, EntryKind.File, ((FileInfo)info).Length, null));
                }
            }
        }

        return entries;
    }

    private static (long Bytes, int Files) Totals(IReadOnlyList<FolderCopy> copies)
    {
        var files = copies.SelectMany(copy => copy.Entries).Where(entry => entry.Kind == EntryKind.File).ToList();
        return (files.Sum(file => file.Length), files.Count);
    }

    private static bool Matches(List<FolderEntry> source, List<FolderEntry> copy)
    {
        if (source.Count(entry => entry.Kind == EntryKind.File) != copy.Count(entry => entry.Kind == EntryKind.File))
            return false;

        var copied = copy.ToDictionary(entry => entry.RelativePath, StringComparer.Ordinal);
        return source.All(entry => copied.TryGetValue(entry.RelativePath, out var match) && match.Kind == entry.Kind && match.Length == entry.Length);
    }

    private static string? FindLockedFile(IReadOnlyList<FolderCopy> copies)
    {
        foreach (var (move, entries) in copies)
        {
            foreach (var entry in entries.Where(entry => entry.Kind == EntryKind.File))
            {
                var path = Path.Combine(move.From, entry.RelativePath);
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 1);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return path;
                }
            }
        }

        return null;
    }

    private static void DeleteTree(string folder)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, RecurseSubdirectories = true }))
        {
            if (file.Attributes.HasFlag(FileAttributes.ReadOnly))
                file.Attributes &= ~FileAttributes.ReadOnly;
        }

        Directory.Delete(folder, recursive: true);
    }

    private static bool HasEntries(string folder)
        => Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder, "*", EveryEntry).Any();

    /// <summary>The folders this call creates, deepest first, or null when the folder cannot be created.</summary>
    private static List<string>? CreateFolder(string folder)
    {
        var missing = new List<string>();
        for (var current = folder; current is not null && !Directory.Exists(current); current = Path.GetDirectoryName(current))
            missing.Add(current);

        try
        {
            Directory.CreateDirectory(folder);
            return missing;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            RemoveEmptyFolders(missing);
            return null;
        }
    }

    private static void RemoveEmptyFolders(List<string> folders)
    {
        foreach (var folder in folders)
        {
            try
            {
                if (Directory.Exists(folder) && !HasEntries(folder))
                    Directory.Delete(folder);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return;
            }
        }
    }

    private static bool CanWrite(string folder)
    {
        var probe = Path.Combine(folder, $".borea-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            stream.WriteByte(0);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsProcessRunning(string[] names)
    {
        foreach (var name in names)
        {
            var processes = Process.GetProcessesByName(name);
            var found = processes.Any(process => process.Id != Environment.ProcessId);
            foreach (var process in processes)
                process.Dispose();

            if (found)
                return true;
        }

        return false;
    }

    internal static bool OnSameVolume(string left, string right)
    {
        var leftVolume = VolumeRoot(left);
        return leftVolume is not null && string.Equals(leftVolume, VolumeRoot(right), PathComparison);
    }

    /// <summary>The longest mount point that holds the path, or its root when the mount points cannot be read.</summary>
    private static string? VolumeRoot(string path)
    {
        string? longest = null;
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                var root = Normalize(drive.RootDirectory.FullName);
                if (IsSameOrInside(path, root) && (longest is null || root.Length > longest.Length))
                    longest = root;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            longest = null;
        }

        return longest ?? Path.GetPathRoot(path);
    }

    private static LibraryFolderChangeResult Refused(LibraryFolderChangeOutcome outcome, string folder, string previous, string message)
        => new(outcome, folder, previous, message);

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>The path with each link among its existing folders replaced by the folder it points to.</summary>
    private static string Resolve(string path) => Resolve(path, depth: 0);

    private static string Resolve(string path, int depth)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root) || depth > MaxLinkDepth)
            return path;

        var names = path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var resolved = root;
        for (var index = 0; index < names.Length; index++)
        {
            var next = Path.Combine(resolved, names[index]);
            if (LinkTarget(next) is { } linked)
                return Resolve(Path.Combine([Normalize(linked), .. names[(index + 1)..]]), depth + 1);

            if (!Directory.Exists(next))
                return Path.Combine([next, .. names[(index + 1)..]]);

            resolved = next;
        }

        return resolved;
    }

    private static string? LinkTarget(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).ResolveLinkTarget(returnFinalTarget: true)?.FullName;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool SamePath(string left, string right) => string.Equals(left, right, PathComparison);

    private static bool IsSameOrInside(string path, string folder) => SamePath(path, folder) || IsInside(path, folder);

    private static bool IsInside(string path, string folder)
    {
        var prefix = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
        return path.Length > prefix.Length && path.StartsWith(prefix, PathComparison);
    }

    private static StringComparison PathComparison => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private sealed record FolderMove(string From, string To);

    private sealed record FolderCopy(FolderMove Move, List<FolderEntry> Entries);

    private enum EntryKind
    {
        File,
        Directory,
        FileLink,
        DirectoryLink,
    }

    private sealed record FolderEntry(string RelativePath, EntryKind Kind, long Length, string? LinkTarget);

    private sealed class CopyReport(IProgress<LibraryMoveProgress>? progress, long totalBytes, int totalFiles)
    {
        private long _bytes;
        private int _files;

        public void AddBytes(int count)
        {
            _bytes += count;
            Send();
        }

        public void AddFile()
        {
            _files++;
            Send();
        }

        public void Send() => progress?.Report(new LibraryMoveProgress(LibraryMoveStage.Copying, _bytes, totalBytes, _files, totalFiles));
    }
}
