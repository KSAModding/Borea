using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.Settings;
using Borea.Storage.Files;
using Borea.Storage.Mods;

namespace Borea.Storage.ModLoaders;

/// <summary>
/// A standalone loader without a given directory goes into its own version folder,
/// Loaders/&lt;id&gt;/&lt;version&gt;, beside the versions that are already installed, and so does one with a given directory
/// inside the Loaders root. A given directory outside that root and a loader below the game folder keep one recorded
/// install that is replaced in place.
/// </summary>
public sealed class FileLoaderInstaller : ILoaderInstaller
{
    private readonly IGamePathProvider _pathProvider;
    private readonly IModDownloader _downloader;
    private readonly IBoreaSettingsRepository _settings;
    private readonly ILoaderConfigurator _configurator;
    private readonly Func<bool> _isGameRunning;
    private readonly LoaderLayoutMigration _layout;
    private readonly FileLoaderVersionStore _versions;

    /// <param name="isGameRunning">
    /// Whether a game or loader runs. A folder that it can run from is then never replaced or moved, because Windows locks
    /// the files in use and a half-replaced folder cannot start. Null means that nothing runs.
    /// </param>
    public FileLoaderInstaller(
        IGamePathProvider pathProvider,
        IModDownloader downloader,
        IBoreaSettingsRepository settings,
        ILoaderConfigurator configurator,
        Func<bool>? isGameRunning = null)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _configurator = configurator ?? throw new ArgumentNullException(nameof(configurator));
        _isGameRunning = isGameRunning ?? (static () => false);
        _layout = new LoaderLayoutMigration(settings, pathProvider);
        _versions = new FileLoaderVersionStore(settings, pathProvider);
    }

    private enum Mode
    {
        /// <summary>Unpack straight into a new or empty folder, which is the rule for a given directory.</summary>
        Fresh,

        /// <summary>Unpack beside the folder and swap it in, and keep the old one until the record is saved.</summary>
        Replace,

        /// <summary>Unpack into a hidden sibling and rename it into place, so the version store never lists a half-written version.</summary>
        Beside,
    }

    public async Task<LoaderInstallResult> InstallAsync(
        ModMetadata loader,
        ModVersionMetadata release,
        string? directory = null,
        bool replacesUnfitRecord = false,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(release);
        RequireInstallable(loader, release);

        await _layout.RecoverAsync(cancellationToken).ConfigureAwait(false);

        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false) ?? new BoreaSettings(null);
        var recordedInstallation = settings.LoaderInstallations.TryGetValue(loader.ModId, out var known) ? known : null;
        var recorded = recordedInstallation is null ? null : Path.GetFullPath(recordedInstallation.DirectoryPath);

        var (target, path, root) = Describe(loader, release);
        if (target == InstallAnchor.Standalone && directory is not null)
            directory = OutsideLoadersRoot(loader, directory, recorded);

        if (target == InstallAnchor.Standalone && directory is null)
            return await InstallVersionAsync(loader, release, path, root, settings, replacesUnfitRecord, progress, cancellationToken).ConfigureAwait(false);

        var destination = Resolve(loader, target, path, directory, settings.GameDirectoryPath);

        var replacing = recorded is not null;
        if (replacing && !FileSystemPaths.SamePath(recorded!, destination))
        {
            throw new InvalidOperationException(
                $"{loader.Name} is already installed at '{recorded}'. Install into that directory to replace it, remove it first, or correct its directory in the settings.");
        }

        if (!replacing && Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new InvalidOperationException($"'{destination}' already holds files that Borea did not install.");

        RequireConfigurable(loader, settings);
        if (replacing)
            RequireGameStopped(loader);

        var isAdopted = !IsBelowLoadersRoot(destination) && (recordedInstallation?.IsAdopted ?? Directory.Exists(destination));
        return await InstallIntoAsync(
            loader,
            release,
            root,
            destination,
            destination,
            replacing ? Mode.Replace : Mode.Fresh,
            replacing ? destination : null,
            current => current.WithLoaderInstallation(loader.ModId, new LoaderInstallation(destination, release.Version, rawVersion: null, isAdopted)),
            settings,
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The Loaders root is Borea's own data, so Borea chooses the version folder there and a given directory inside it only
    /// names the loader. Settings > Game fills in the recorded folder, which is a version folder, and an install into it would
    /// put another version into the folder that names the old one. Any other folder inside the root is refused.
    /// </summary>
    /// <returns>Null when Borea chooses the version folder, else the given directory.</returns>
    private string? OutsideLoadersRoot(ModMetadata loader, string directory, string? recorded)
    {
        var given = Absolute(directory, nameof(directory));
        if (!IsBelowLoadersRoot(given))
            return directory;

        var loaderFolder = Path.Combine(_pathProvider.GetLoadersRoot(), loader.ModId);
        var isLoaderFolder = (recorded is not null && FileSystemPaths.SamePath(given, recorded))
            || FileSystemPaths.SamePath(given, loaderFolder)
            || (Path.GetDirectoryName(given) is { } parent && FileSystemPaths.SamePath(parent, loaderFolder) && ModVersion.TryParse(Path.GetFileName(given), out _));
        if (!isLoaderFolder)
        {
            throw new InvalidOperationException(
                $"'{given}' is inside the Loaders folder of Borea, where Borea chooses the folder of each version. Install {loader.Name} without a directory, or choose a folder outside '{_pathProvider.GetLoadersRoot()}'.");
        }

        return null;
    }

    /// <summary>
    /// The same version is repaired in place. A new version goes into its own folder, and the flat folder of an older Borea
    /// is first moved into a version folder, so that no version folder is ever made inside a loader.
    /// </summary>
    private async Task<LoaderInstallResult> InstallVersionAsync(
        ModMetadata loader,
        ModVersionMetadata release,
        string? path,
        string? root,
        BoreaSettings settings,
        bool replacesUnfitRecord,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        RequireConfigurable(loader, settings);

        var versionDirectory = Path.GetFullPath(_pathProvider.GetLoaderVersionDirectoryPath(loader.ModId, release.Version));
        var loaderFolder = Path.GetDirectoryName(versionDirectory)!;
        var installed = await _versions.ListAsync(cancellationToken).ConfigureAwait(false);

        if (InstalledLoaderVersion.Find(installed, loader.ModId, release.Version) is { } same)
        {
            RequireGameStopped(loader);
            var folder = Path.GetFullPath(same.DirectoryPath);
            return await ReplaceVersionAsync(loader, release, root, folder, same.IsRecorded ? folder : Below(folder, path), loaderFolder, settings, replacesUnfitRecord, progress, cancellationToken).ConfigureAwait(false);
        }

        var recordedInstallation = settings.LoaderInstallations.TryGetValue(loader.ModId, out var known) ? known : null;
        var flat = _layout.Inspect(loader.ModId, loader.Provides?.Launch, recordedInstallation);
        if (flat.Kind == FlatLoaderFolderKind.Foreign)
        {
            throw new InvalidOperationException(
                $"'{loaderFolder}' holds files that are not a version of {loader.Name}, so Borea cannot add a version folder there. Move those files away first.");
        }

        if (flat.Kind == FlatLoaderFolderKind.Loader)
        {
            if (flat.Version is not { } flatVersion)
            {
                throw new InvalidOperationException(
                    $"Borea cannot tell which version of {loader.Name} is in '{loaderFolder}', so it cannot keep it beside {release.Version}. Uninstall it first, then install {release.Version} again.");
            }

            RequireGameStopped(loader);
            if (flatVersion == release.Version)
                return await ReplaceVersionAsync(loader, release, root, loaderFolder, loaderFolder, loaderFolder, settings, replacesUnfitRecord, progress, cancellationToken).ConfigureAwait(false);

            await _layout.MoveLegacyAsync(loader.ModId, flatVersion, cancellationToken).ConfigureAwait(false);
            settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false) ?? new BoreaSettings(null);
            installed = await _versions.ListAsync(cancellationToken).ConfigureAwait(false);
        }

        if (Directory.Exists(versionDirectory) && Directory.EnumerateFileSystemEntries(versionDirectory).Any())
        {
            var holder = installed.FirstOrDefault(v => FileSystemPaths.SamePath(v.DirectoryPath, versionDirectory));
            throw new InvalidOperationException(holder is null
                ? $"'{versionDirectory}' already holds files that Borea did not install."
                : $"'{versionDirectory}' holds {loader.Name} {holder.Version?.ToString() ?? "of an unknown version"}, not {release.Version}. Remove that version first, then install {release.Version} again.");
        }

        // The new version starts with the configuration of the recorded version, so the arguments the player added stay.
        var seed = settings.LoaderInstallations.TryGetValue(loader.ModId, out var record)
            ? record.DirectoryPath
            : InstalledLoaderVersion.Of(installed, loader.ModId).FirstOrDefault()?.DirectoryPath;
        var destination = Below(versionDirectory, path);
        var loaderFolderExisted = Directory.Exists(loaderFolder);
        try
        {
            return await InstallIntoAsync(
                loader,
                release,
                root,
                versionDirectory,
                destination,
                Mode.Beside,
                seed,
                current => Repoint(current, loader.ModId, release.Version, destination, loaderFolder, replacesUnfitRecord),
                settings,
                progress,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (!loaderFolderExisted)
                TryDeleteEmptyDirectory(loaderFolder);

            throw;
        }
    }

    /// <summary>
    /// Repairs an installed version in its own folder. The record keeps its owner when it names that folder, and else
    /// follows the rule of <see cref="Repoint"/>.
    /// </summary>
    private Task<LoaderInstallResult> ReplaceVersionAsync(
        ModMetadata loader,
        ModVersionMetadata release,
        string? root,
        string folder,
        string destination,
        string loaderFolder,
        BoreaSettings settings,
        bool replacesUnfitRecord,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken) =>
        InstallIntoAsync(
            loader,
            release,
            root,
            folder,
            destination,
            Mode.Replace,
            destination,
            current =>
            {
                if (!current.LoaderInstallations.TryGetValue(loader.ModId, out var record) || !FileSystemPaths.SamePath(record.DirectoryPath, destination))
                    return Repoint(current, loader.ModId, release.Version, destination, loaderFolder, replacesUnfitRecord);

                var isAdopted = !IsBelowLoadersRoot(destination) && record.IsAdopted;
                return current.WithLoaderInstallation(loader.ModId, new LoaderInstallation(destination, release.Version, rawVersion: null, isAdopted));
            },
            settings,
            progress,
            cancellationToken);

    /// <summary>
    /// The record names the version that an older Borea starts for every instance, so it stays on the newest version that
    /// Borea installed and an install of a lower version for one pinned instance does not move it. It moves to a lower
    /// version only when the recorded folder is gone or the recorded version does not fit the game, and from a prerelease to
    /// a release. It moves to a prerelease only when the record is a prerelease too or the channel of the player takes
    /// prereleases, so that a prerelease installed for one instance does not start every instance. A record outside the
    /// Loaders root belongs to the player and never moves. A record at the flat folder of a moved loader always moves,
    /// because an uninstall of that folder would delete every version.
    /// </summary>
    /// <returns>The settings to save, or null when the record stays.</returns>
    private BoreaSettings? Repoint(BoreaSettings current, string loaderId, ModVersion version, string destination, string loaderFolder, bool replacesUnfitRecord)
    {
        var installation = new LoaderInstallation(destination, version, rawVersion: null, isAdopted: false);
        if (!current.LoaderInstallations.TryGetValue(loaderId, out var record))
            return current.WithLoaderInstallation(loaderId, installation);

        if (!IsBelowLoadersRoot(record.DirectoryPath))
            return null;

        if (FileSystemPaths.SamePath(record.DirectoryPath, loaderFolder) || record.Version is not { } recordedVersion)
            return current.WithLoaderInstallation(loaderId, installation);

        if (version.PreRelease is not null && recordedVersion.PreRelease is null && current.ReleaseChannel == ReleaseChannel.Stable)
            return null;

        var moves = version.CompareTo(recordedVersion) >= 0
            || (recordedVersion.PreRelease is not null && version.PreRelease is null)
            || !Directory.Exists(record.DirectoryPath)
            || replacesUnfitRecord;
        return moves ? current.WithLoaderInstallation(loaderId, installation) : null;
    }

    /// <param name="folder">The folder that is created or swapped as a whole.</param>
    /// <param name="destination">The install root of the loader, which is the folder or a path below it.</param>
    /// <param name="keepConfigurationOf">The install whose configuration file is copied into the new files, or null.</param>
    /// <param name="record">The settings to save once the files are in place, or null to keep the record.</param>
    private async Task<LoaderInstallResult> InstallIntoAsync(
        ModMetadata loader,
        ModVersionMetadata release,
        string? root,
        string folder,
        string destination,
        Mode mode,
        string? keepConfigurationOf,
        Func<BoreaSettings, BoreaSettings?> record,
        BoreaSettings settings,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var configure = loader.Provides?.Configure;
        var archivePath = Path.Combine(Path.GetTempPath(), $"borea-download-{Guid.NewGuid():N}.zip");
        var created = !Directory.Exists(folder);
        var stagingDirectory = mode == Mode.Fresh ? folder : TransactionPath(folder, "staging");
        var backupDirectory = mode == Mode.Replace ? TransactionPath(folder, "backup") : null;
        var stagedDestination = Path.GetFullPath(Path.Combine(stagingDirectory, Path.GetRelativePath(folder, destination)));
        var activated = false;
        var replacementBackedUp = false;

        try
        {
            var download = await _downloader.DownloadAsync(release, archivePath, progress.ForDownload(release), cancellationToken).ConfigureAwait(false);

            var keptPath = keepConfigurationOf is null || configure is null
                ? null
                : Path.GetFullPath(Path.Combine(keepConfigurationOf, configure.File.Replace('/', Path.DirectorySeparatorChar)));
            var kept = keptPath is not null && File.Exists(keptPath)
                ? await File.ReadAllBytesAsync(keptPath, cancellationToken).ConfigureAwait(false)
                : null;

            progress.Report(release, InstallPhase.Extracting);
            Unpack(archivePath, root, stagedDestination, release);
            if (kept is not null)
            {
                var stagedConfigurationPath = Path.GetFullPath(Path.Combine(stagedDestination, configure!.File.Replace('/', Path.DirectorySeparatorChar)));
                Directory.CreateDirectory(Path.GetDirectoryName(stagedConfigurationPath)!);
                await File.WriteAllBytesAsync(stagedConfigurationPath, kept, cancellationToken).ConfigureAwait(false);
            }

            RequireLaunchTarget(loader, release, stagedDestination);

            if (configure?.GamePath is not null)
                progress.Report(release, InstallPhase.Configuring);

            var stagedConfigurationFile = configure?.GamePath is null
                ? null
                : await _configurator.ConfigureAsync(loader, stagedDestination, settings.GameDirectoryPath!, cancellationToken).ConfigureAwait(false);

            var configurationFile = stagedConfigurationFile is null
                ? null
                : Path.GetFullPath(Path.Combine(destination, Path.GetRelativePath(stagedDestination, stagedConfigurationFile)));

            progress.Report(release, InstallPhase.Finishing);
            if (mode == Mode.Replace)
            {
                replacementBackedUp = ActivateReplacement(folder, stagingDirectory, backupDirectory!);
                activated = true;
            }
            else if (mode == Mode.Beside)
            {
                // The caller refuses a folder with entries, so only an empty folder can be in the way here.
                if (Directory.Exists(folder))
                    Directory.Delete(folder);
                DirectoryRename.Move(stagingDirectory, folder);
                activated = true;
            }

            if (record(settings) is { } updated)
                await _settings.SaveAsync(updated, cancellationToken).ConfigureAwait(false);

            if (replacementBackedUp)
                TryDeleteDirectory(backupDirectory!);

            return new LoaderInstallResult(loader.ModId, release.Version, destination, download, configurationFile, Replaced: mode == Mode.Replace);
        }
        catch (Exception installError)
        {
            if (mode == Mode.Replace && activated)
                RestoreReplacement(folder, backupDirectory!, replacementBackedUp, installError);
            else if (mode == Mode.Beside && activated)
                TryDeleteDirectory(folder);
            else if (mode == Mode.Fresh && created)
                TryDeleteDirectory(folder);
            else if (mode == Mode.Fresh)
                TryClearDirectory(folder);

            throw;
        }
        finally
        {
            LeftoverCleanup.TryDeleteFile(archivePath);
            if (mode != Mode.Fresh)
                TryDeleteDirectory(stagingDirectory);
        }
    }

    private void RequireConfigurable(ModMetadata loader, BoreaSettings settings)
    {
        if (loader.Provides?.Configure?.GamePath is null)
            return;

        if (settings.GameDirectoryPath is null)
            throw new InvalidOperationException($"Borea does not know where the game is installed, so it cannot configure {loader.Name}. Set the game directory first.");

        // A game path the loader could not open stops the install before the download.
        _ = _configurator.GamePathValue(settings.GameDirectoryPath);
    }

    private void RequireGameStopped(ModMetadata loader)
    {
        if (_isGameRunning())
            throw new InvalidOperationException($"The game is running, so Borea does not change the files of {loader.Name} now. Close the game, then try again.");
    }

    private bool IsBelowLoadersRoot(string directory) => LoaderLayoutMigration.IsBelowLoadersRoot(_pathProvider.GetLoadersRoot(), directory);

    private static string TransactionPath(string destination, string purpose)
    {
        var parent = Path.GetDirectoryName(destination);
        var name = Path.GetFileName(destination);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            throw new NotSupportedException("A loader installed at a file-system root cannot be replaced safely.");

        return Path.Combine(parent, $".{name}.borea-{purpose}-{Guid.NewGuid():N}");
    }

    private static bool ActivateReplacement(string destination, string stagingDirectory, string backupDirectory)
    {
        var backedUp = Directory.Exists(destination);
        if (backedUp)
            DirectoryRename.Move(destination, backupDirectory);

        try
        {
            DirectoryRename.Move(stagingDirectory, destination);
            return backedUp;
        }
        catch (Exception activationError)
        {
            if (!backedUp)
                throw;

            try
            {
                DirectoryRename.Move(backupDirectory, destination);
            }
            catch (Exception rollbackError)
            {
                throw new AggregateException(
                    "The loader replacement could not be activated, and Borea could not restore the previous directory.",
                    activationError,
                    rollbackError);
            }

            throw;
        }
    }

    private static void RestoreReplacement(string destination, string backupDirectory, bool backedUp, Exception installError)
    {
        var failedDirectory = TransactionPath(destination, "failed");
        try
        {
            if (Directory.Exists(destination))
                DirectoryRename.Move(destination, failedDirectory);

            if (backedUp)
                DirectoryRename.Move(backupDirectory, destination);

            TryDeleteDirectory(failedDirectory);
        }
        catch (Exception rollbackError)
        {
            throw new AggregateException(
                "The loader replacement failed, and Borea could not restore the previous directory.",
                installError,
                rollbackError);
        }
    }

    private static void RequireInstallable(ModMetadata loader, ModVersionMetadata release)
    {
        if (loader.Type != ContentType.ModLoader)
            throw new ArgumentException($"'{loader.ModId}' is a {loader.Type}, and only a mod loader installs this way.", nameof(loader));

        if (release.Type != ContentType.ModLoader)
            throw new NotSupportedException($"'{release.ModId}' is a {release.Type}, and only a mod loader installs by its own descriptor.");

        if (!ModIds.Equals(loader.ModId, release.ModId))
            throw new ArgumentException($"The release belongs to '{release.ModId}' and not to '{loader.ModId}'.", nameof(release));

        if (!ModArchive.IsZip(release.Download.ContentType))
            throw new NotSupportedException($"'{release.ModId}' is served as '{release.Download.ContentType}', and only a zip archive can be unpacked.");

        if (loader.Provides?.Configure is { Format: not (ConfigureFormat.Json or ConfigureFormat.Toml) })
            throw new NotSupportedException($"The listing of {loader.Name} keeps its configuration in a format this version of Borea cannot write.");
    }

    private static (InstallAnchor? Target, string? Path, string? Root) Describe(ModMetadata loader, ModVersionMetadata release)
    {
        if (release.Install is { } stamped)
            return (stamped.Target, stamped.Path, stamped.Root);

        if (loader.Install is { } authored)
            return (authored.Target, authored.Path, authored.Root);

        return (null, null, null);
    }

    /// <summary>
    /// A standalone loader comes here only with a given directory, because Borea chooses the version folder itself.
    /// </summary>
    private static string Resolve(ModMetadata loader, InstallAnchor? target, string? path, string? directory, string? gameDirectory)
    {
        switch (target)
        {
            case null:
                throw new NotSupportedException($"The listing of {loader.Name} does not say where the loader goes, so Borea cannot install it and can only show its links.");

            case InstallAnchor.Standalone:
                return Below(Absolute(directory!, nameof(directory)), path);

            case InstallAnchor.GameRoot:
                if (directory is not null)
                    throw new ArgumentException($"Only a standalone loader lets Borea choose the directory; {loader.Name} goes below the game folder.", nameof(directory));

                if (gameDirectory is null)
                    throw new InvalidOperationException($"Borea does not know where the game is installed, so it cannot place {loader.Name}. Set the game directory first.");

                var gameRoot = Absolute(gameDirectory, nameof(gameDirectory));
                var placed = Below(gameRoot, path);
                if (FileSystemPaths.SamePath(placed, gameRoot))
                    throw new NotSupportedException($"{loader.Name} installs into the game folder itself, which Borea cannot take back out again.");

                return placed;

            case InstallAnchor.Mods:
            case InstallAnchor.UserData:
                throw new NotSupportedException($"{loader.Name} installs into the {Anchor(target.Value)} of an instance, and Borea records a loader once and not per instance.");

            default:
                throw new NotSupportedException($"The listing of {loader.Name} names an install target this version of Borea does not know, so it must not guess.");
        }
    }

    private static string Below(string anchor, string? path) =>
        Path.TrimEndingDirectorySeparator(path is null
            ? Path.GetFullPath(anchor)
            : Path.GetFullPath(Path.Combine(anchor, path.Replace('/', Path.DirectorySeparatorChar))));

    private static string Anchor(InstallAnchor anchor) => anchor == InstallAnchor.Mods ? "mods folder" : "user data root";

    /// <summary>
    /// No derived root for a loader (RFC 0035 rule 9).
    /// </summary>
    private static void Unpack(string archivePath, string? root, string destination, ModVersionMetadata release)
    {
        var files = ModArchive.Extract(archivePath, root, destination);
        if (files == 0)
        {
            throw new InvalidOperationException(root is null
                ? $"The archive of '{release.ModId}' {release.Version} holds no files."
                : $"The archive of '{release.ModId}' {release.Version} holds nothing under '{root}'.");
        }
    }

    /// <summary>
    /// RFC 0035 rule 3: the launch target must be in the release.
    /// </summary>
    private static void RequireLaunchTarget(ModMetadata loader, ModVersionMetadata release, string destination)
    {
        var launch = loader.Provides?.Launch;
        if (launch is null)
            return;

        var executable = Path.Combine(destination, launch.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(executable))
        {
            throw new InvalidOperationException(
                $"The archive of '{release.ModId}' {release.Version} has no '{launch}' at its install root, so nothing could start {loader.Name}.");
        }
    }

    private static string Absolute(string path, string paramName)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A directory is required.", paramName);

        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("The directory must be absolute, because the loader resolves its own files against it.", paramName);

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    /// <summary>
    /// Cleanup must not replace the error that caused it, here and below.
    /// </summary>
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                Directory.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryClearDirectory(string path)
    {
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                if (Directory.Exists(entry))
                    Directory.Delete(entry, recursive: true);
                else
                    File.Delete(entry);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
