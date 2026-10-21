using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Paths;
using Borea.Core.Settings;
using Borea.Storage.Files;

namespace Borea.Storage.Game;

/// <summary>
/// Copies the game folder of the settings to the Game Builds folder of the library, once per build. The copy goes
/// to a temporary folder that is renamed only after it matched, so a folder of a kept build is never a partial copy.
/// </summary>
public sealed class GameBuildKeeper : IGameBuildKeeper
{
    private const string TemporarySuffix = ".copying";

    private const string RemovingSuffix = ".removing";

    private static readonly EnumerationOptions EveryEntry = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
    };

    // static, because a settings change builds new services, and with them a new keeper, while a copy can still run
    private static readonly SemaphoreSlim Keeping = new(1, 1);

    private readonly IGamePathProvider _paths;
    private readonly IInstanceRepository _instances;
    private readonly Func<bool> _isGameProcessRunning;
    private readonly Func<Guid, bool> _isLaunched;
    private readonly IWinePrefixProbe _wine;
    private readonly IFreeSpaceProbe _freeSpace;

    /// <param name="isLaunched">Whether a launch by Borea still runs for the instance. The process probe does not see every one of them.</param>
    public GameBuildKeeper(IGamePathProvider paths, IInstanceRepository instances, Func<bool> isGameProcessRunning, Func<Guid, bool> isLaunched)
        : this(paths, instances, isGameProcessRunning, isLaunched, new WinePrefixProbe(), new DriveFreeSpaceProbe())
    {
    }

    internal GameBuildKeeper(IGamePathProvider paths, IInstanceRepository instances, Func<bool> isGameProcessRunning, Func<Guid, bool> isLaunched, IWinePrefixProbe wine, IFreeSpaceProbe freeSpace)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _isGameProcessRunning = isGameProcessRunning ?? throw new ArgumentNullException(nameof(isGameProcessRunning));
        _isLaunched = isLaunched ?? throw new ArgumentNullException(nameof(isLaunched));
        _wine = wine ?? throw new ArgumentNullException(nameof(wine));
        _freeSpace = freeSpace ?? throw new ArgumentNullException(nameof(freeSpace));
    }

    public GameBuildKeepCheck Check(Instance instance) => Plan(instance).Check;

    public async Task<GameBuildKeepCheck> KeepAsync(Guid instanceId, GameVersion expected, IProgress<LibraryMoveProgress>? progress, CancellationToken cancellationToken)
    {
        // a second keep of the same build waits here and then finds the copy of the first one
        await Keeping.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DeleteUnfinishedCopies();
            var instance = await _instances.GetByIdAsync(instanceId).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"The instance {instanceId} does not exist.");
            var plan = Plan(instance);
            if (plan.Check is not { Refusal: GameBuildKeepRefusal.None, Version: { } version, Folder: { } folder })
                return plan.Check;

            // the client can update the game folder while the player reads what the copy needs
            if (version != expected)
                return new GameBuildKeepCheck(GameBuildKeepRefusal.BuildChanged, version, folder, 0, null);

            if (plan.Files.Count > 0)
                Copy(plan, version, folder, progress, cancellationToken);

            await _instances.UpdateAsync(
                instanceId,
                saved =>
                {
                    saved.SetGameBuild(version);
                    return true;
                },
                CancellationToken.None).ConfigureAwait(false);
            return plan.Check;
        }
        finally
        {
            Keeping.Release();
        }
    }

    private KeepPlan Plan(Instance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (instance.GameBuild is { } kept)
            return Refused(GameBuildKeepRefusal.AlreadyKept, kept, _paths.GetGameBuildFolder(kept));

        var configured = _paths.GetGameDirectoryPath();
        if (string.IsNullOrWhiteSpace(configured) || !Path.IsPathFullyQualified(configured)
            || InstalledGameVersionProvider.Read(configured)?.Version is not { } version)
            return Refused(GameBuildKeepRefusal.NoGame, null, null);

        var gameDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured));
        var folder = _paths.GetGameBuildFolder(version);
        if (_wine.Find(gameDirectory)?.Wrapper is not null)
            return Refused(GameBuildKeepRefusal.WineWrapper, version, folder);

        if (IsGameRunning())
            return Refused(GameBuildKeepRefusal.GameRunning, version, folder);

        if (Directory.Exists(folder) || File.Exists(folder))
            return InstalledGameVersionProvider.Read(folder)?.Version == version
                ? new KeepPlan(new GameBuildKeepCheck(GameBuildKeepRefusal.None, version, folder, 0, null), gameDirectory, [], [])
                : Refused(GameBuildKeepRefusal.FolderTaken, version, folder);

        if (IsSameOrInside(Path.GetFullPath(_paths.GetGameBuildsRoot()), gameDirectory))
            return Refused(GameBuildKeepRefusal.LibraryInsideGame, version, folder);

        var directories = new List<string>();
        var files = new List<GameFile>();
        Scan(gameDirectory, string.Empty, directories, files);
        var bytes = files.Sum(file => file.Length);
        var free = _freeSpace.Measure(_paths.GetGameBuildsRoot())?.AvailableBytes;
        var refusal = free < bytes ? GameBuildKeepRefusal.NotEnoughSpace : GameBuildKeepRefusal.None;
        return new KeepPlan(new GameBuildKeepCheck(refusal, version, folder, bytes, free), gameDirectory, directories, files);
    }

    public async Task<GameBuildChangeRefusal> UseCurrentBuildAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        // a keep holds the gate for its whole copy, so the player gets an answer now instead of a silent wait
        if (!await Keeping.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return GameBuildChangeRefusal.Busy;

        try
        {
            if (IsGameRunning())
                return GameBuildChangeRefusal.GameRunning;

            await _instances.UpdateAsync(
                instanceId,
                saved =>
                {
                    saved.SetGameBuild(null);
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
            return GameBuildChangeRefusal.None;
        }
        finally
        {
            Keeping.Release();
        }
    }

    public async Task<IReadOnlyList<KeptGameBuild>> ListKeptAsync(CancellationToken cancellationToken)
    {
        var kept = KeptVersions();
        if (kept.Count == 0)
            return [];

        var instances = await _instances.GetAllAsync().ConfigureAwait(false);
        var settingsFolders = SettingsGameFolders();
        var builds = new List<KeptGameBuild>();
        foreach (var version in kept)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = _paths.GetGameBuildFolder(version);
            long bytes;
            try
            {
                bytes = FolderBytes(folder);
            }
            catch (DirectoryNotFoundException)
            {
                // a remove took the folder after it was listed
                continue;
            }

            var names = instances
                .Where(instance => instance.GameBuild is { } build && IsFolderOf(folder, build))
                .Select(instance => instance.Name)
                .Order(StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            builds.Add(new KeptGameBuild(version, folder, bytes, names, IsSettingsGameFolder(settingsFolders, folder)));
        }

        return builds;
    }

    public async Task<GameBuildChangeRefusal> RemoveAsync(GameVersion build, CancellationToken cancellationToken)
    {
        // under the gate, so no keep sets this build for an instance while its folder goes, and without a wait,
        // because a keep holds the gate for its whole copy
        if (!await Keeping.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return GameBuildChangeRefusal.Busy;

        try
        {
            DeleteUnfinishedCopies();
            if (IsGameRunning())
                return GameBuildChangeRefusal.GameRunning;

            // every instance without a kept build starts from the game folder of the settings
            var folder = _paths.GetGameBuildFolder(build);
            if (IsSettingsGameFolder(SettingsGameFolders(), folder))
                return GameBuildChangeRefusal.GameFolderOfSettings;

            foreach (var id in InstanceIds())
            {
                Instance? instance;
                try
                {
                    instance = await _instances.GetByIdAsync(id).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // a kept build cannot be fetched again, so a record that may name it keeps it
                    return GameBuildChangeRefusal.InstanceUnreadable;
                }

                if (instance?.GameBuild is { } used && IsFolderOf(folder, used))
                    return GameBuildChangeRefusal.InUse;
            }

            if (!Directory.Exists(folder))
                return GameBuildChangeRefusal.None;

            cancellationToken.ThrowIfCancellationRequested();

            // on Windows an open file fails the move and leaves the build whole, and on every system a delete that
            // stops halfway leaves only a dot folder, which no listing reads and the next keep or remove deletes
            var removing = Path.Combine(Path.GetDirectoryName(folder)!, "." + Path.GetFileName(folder) + RemovingSuffix);
            DirectoryLinks.DeleteTreeWithoutFollowingLinks(removing);
            Directory.Move(folder, removing);
            TryDelete(removing);
            return GameBuildChangeRefusal.None;
        }
        finally
        {
            Keeping.Release();
        }
    }

    private bool IsGameRunning() => _isGameProcessRunning() || InstanceIds().Any(_isLaunched);

    private bool IsFolderOf(string folder, GameVersion build)
        => string.Equals(_paths.GetGameBuildFolder(build), folder, StringComparison.Ordinal);

    /// <summary>
    /// The full game folder of the settings, and the folder it links to when it is a link. Empty when the settings
    /// name no absolute folder.
    /// </summary>
    private List<string> SettingsGameFolders()
    {
        var configured = _paths.GetGameDirectoryPath();
        if (string.IsNullOrWhiteSpace(configured) || !Path.IsPathFullyQualified(configured))
            return [];

        var gameDirectory = Path.GetFullPath(configured);
        try
        {
            if (new DirectoryInfo(gameDirectory).ResolveLinkTarget(returnFinalTarget: true) is { } target)
                return [gameDirectory, target.FullName];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // a link that Borea cannot read leaves only the folder name to compare
        }

        return [gameDirectory];
    }

    private static bool IsSettingsGameFolder(List<string> settingsFolders, string folder)
        => settingsFolders.Exists(path => IsSameOrInside(path, folder));

    /// <summary>
    /// The builds whose folder is named the way <see cref="IGamePathProvider.GetGameBuildFolder"/> names it, so an
    /// instance on such a build starts from that folder. Dot folders are copies or removals that did not finish.
    /// </summary>
    private List<GameVersion> KeptVersions()
    {
        var root = _paths.GetGameBuildsRoot();
        if (!Directory.Exists(root))
            return [];

        var versions = new List<GameVersion>();
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(folder);
            if (name.StartsWith('.'))
                continue;

            if (InstalledGameVersionProvider.Read(folder)?.Version is { } version
                && string.Equals(Path.GetFileName(_paths.GetGameBuildFolder(version)), name, StringComparison.Ordinal))
                versions.Add(version);
        }

        return versions.OrderDescending().ToList();
    }

    private static long FolderBytes(string folder)
        => new DirectoryInfo(folder)
            .EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Sum(file => file.Length);

    private static KeepPlan Refused(GameBuildKeepRefusal refusal, GameVersion? version, string? folder)
        => new(new GameBuildKeepCheck(refusal, version, folder, 0, null), string.Empty, [], []);

    private List<Guid> InstanceIds()
    {
        var root = _paths.GetInstancesRoot();
        if (!Directory.Exists(root))
            return [];

        return Directory.EnumerateDirectories(root)
            .Select(Path.GetFileName)
            .Select(name => Guid.TryParse(name, out var id) ? id : (Guid?)null)
            .OfType<Guid>()
            .ToList();
    }

    /// <summary>
    /// Deletes the temporary folders that a crash or a kill left behind. The gate is held, so no copy or removal uses
    /// one of them now. The build of such a folder is usually gone from the game folder after the next client update,
    /// so a later keep of the same build cannot be the one that deletes it.
    /// </summary>
    private void DeleteUnfinishedCopies()
    {
        var root = _paths.GetGameBuildsRoot();
        if (!Directory.Exists(root))
            return;

        foreach (var folder in Directory.EnumerateDirectories(root, ".*" + TemporarySuffix).Concat(Directory.EnumerateDirectories(root, ".*" + RemovingSuffix)))
            TryDelete(folder);
    }

    /// <summary>
    /// A file link is copied as the file it points to, which is what the game reads through it. A folder link is
    /// refused, because following it can loop and copying it as a link would tie the copy to the folder the client
    /// changes.
    /// </summary>
    private static void Scan(string folder, string relative, List<string> directories, List<GameFile> files)
    {
        foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", EveryEntry))
        {
            var path = Path.Combine(relative, entry.Name);
            if (entry is DirectoryInfo directory)
            {
                if (DirectoryLinks.IsLink(directory))
                    throw new IOException($"The game folder holds the folder link {directory.FullName}, so Borea does not copy it.");

                directories.Add(path);
                Scan(directory.FullName, path, directories, files);
            }
            else if (entry is FileInfo file)
            {
                var source = DirectoryLinks.IsLink(file) ? file.ResolveLinkTarget(returnFinalTarget: true) as FileInfo ?? file : file;
                files.Add(new GameFile(source.FullName, path, source.Length));
            }
        }
    }

    private static void Copy(KeepPlan plan, GameVersion version, string folder, IProgress<LibraryMoveProgress>? progress, CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(folder)!, "." + Path.GetFileName(folder) + TemporarySuffix);
        DirectoryLinks.DeleteTreeWithoutFollowingLinks(temporary);
        try
        {
            var totalBytes = plan.Check.BytesToCopy;
            long bytes = 0;
            var copied = 0;
            Directory.CreateDirectory(temporary);
            foreach (var directory in plan.Directories)
                Directory.CreateDirectory(Path.Combine(temporary, directory));

            progress?.Report(new LibraryMoveProgress(LibraryMoveStage.Copying, 0, totalBytes, 0, plan.Files.Count));
            foreach (var file in plan.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Path.Combine(temporary, file.RelativePath);
                File.Copy(file.SourcePath, destination);
                if (OperatingSystem.IsWindows())
                {
                    // a read-only copy could not be deleted later
                    var attributes = File.GetAttributes(destination);
                    if (attributes.HasFlag(FileAttributes.ReadOnly))
                        File.SetAttributes(destination, attributes & ~FileAttributes.ReadOnly);
                }
                else
                {
                    // the app host of a Linux build needs its execute bit
                    File.SetUnixFileMode(destination, File.GetUnixFileMode(file.SourcePath));
                }

                bytes += file.Length;
                copied++;
                progress?.Report(new LibraryMoveProgress(LibraryMoveStage.Copying, bytes, totalBytes, copied, plan.Files.Count));
            }

            var copiedDirectories = new List<string>();
            var copiedFiles = new List<GameFile>();
            Scan(temporary, string.Empty, copiedDirectories, copiedFiles);
            if (copiedFiles.Count != plan.Files.Count || copiedFiles.Sum(file => file.Length) != totalBytes)
                throw new IOException($"The copy of KSA {version} in {temporary} does not match the game folder {plan.GameDirectory}.");

            if (InstalledGameVersionProvider.Read(temporary)?.Version != version)
                throw new IOException($"The KSA.dll in the copy of KSA {version} does not read that version.");

            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(temporary, folder);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    // a folder that stays is a dot folder, which no kept build reads, and the next keep or remove deletes it
    private static void TryDelete(string folder)
    {
        try
        {
            DirectoryLinks.DeleteTreeWithoutFollowingLinks(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool IsSameOrInside(string path, string folder)
    {
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        path = Path.TrimEndingDirectorySeparator(path);
        var prefix = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
        return string.Equals(path, folder, comparison) || path.StartsWith(prefix, comparison);
    }

    private sealed record GameFile(string SourcePath, string RelativePath, long Length);

    private sealed record KeepPlan(GameBuildKeepCheck Check, string GameDirectory, IReadOnlyList<string> Directories, IReadOnlyList<GameFile> Files);
}
