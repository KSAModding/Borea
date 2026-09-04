using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.Settings;
using Borea.Storage.Mods;

namespace Borea.Storage.ModLoaders;

/// <summary>
/// File-backed <see cref="ILoaderInstaller"/>.
/// </summary>
public sealed class FileLoaderInstaller : ILoaderInstaller
{
    private readonly IGamePathProvider _pathProvider;
    private readonly IModDownloader _downloader;
    private readonly IBoreaSettingsRepository _settings;
    private readonly ILoaderConfigurator _configurator;

    public FileLoaderInstaller(
        IGamePathProvider pathProvider,
        IModDownloader downloader,
        IBoreaSettingsRepository settings,
        ILoaderConfigurator configurator)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _configurator = configurator ?? throw new ArgumentNullException(nameof(configurator));
    }

    public async Task<LoaderInstallResult> InstallAsync(
        ModMetadata loader,
        ModVersionMetadata release,
        string? directory = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(release);
        RequireInstallable(loader, release);

        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false) ?? new BoreaSettings(null);
        var recorded = settings.LoaderDirectoryPaths.TryGetValue(loader.ModId, out var known) ? Path.GetFullPath(known) : null;

        var (target, path, root) = Describe(loader, release);
        var destination = Resolve(loader, target, path, directory, recorded, settings.GameDirectoryPath);

        var replacing = recorded is not null;
        if (replacing && !SamePath(recorded!, destination))
        {
            throw new InvalidOperationException(
                $"{loader.Name} is already installed at '{recorded}'. Install into that directory to replace it, remove it first, or correct its directory in the settings.");
        }

        if (!replacing && Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new InvalidOperationException($"'{destination}' already holds files that Borea did not install.");

        var configure = loader.Provides?.Configure;
        var gameDirectory = settings.GameDirectoryPath;
        if (configure?.GamePath is not null && gameDirectory is null)
            throw new InvalidOperationException($"Borea does not know where the game is installed, so it cannot configure {loader.Name}. Set the game directory first.");

        var configurationPath = configure is null
            ? null
            : Path.GetFullPath(Path.Combine(destination, configure.File.Replace('/', Path.DirectorySeparatorChar)));
        var archivePath = Path.Combine(Path.GetTempPath(), $"borea-download-{Guid.NewGuid():N}.zip");
        var created = !Directory.Exists(destination);

        try
        {
            var download = await _downloader.DownloadAsync(release, archivePath, progress, cancellationToken).ConfigureAwait(false);

            var kept = replacing && configurationPath is not null && File.Exists(configurationPath)
                ? await File.ReadAllBytesAsync(configurationPath, cancellationToken).ConfigureAwait(false)
                : null;

            Unpack(archivePath, root, destination, release);
            if (kept is not null)
                await File.WriteAllBytesAsync(configurationPath!, kept, cancellationToken).ConfigureAwait(false);

            RequireLaunchTarget(loader, release, destination);

            var configurationFile = configure?.GamePath is null
                ? null
                : await _configurator.ConfigureAsync(loader, destination, gameDirectory!, cancellationToken).ConfigureAwait(false);

            var paths = settings.LoaderDirectoryPaths.ToDictionary(p => p.Key, p => p.Value, ModIds.Comparer);
            paths[loader.ModId] = destination;
            await _settings.SaveAsync(new BoreaSettings(settings.GameDirectoryPath, paths), cancellationToken).ConfigureAwait(false);

            return new LoaderInstallResult(loader.ModId, release.Version, destination, download, configurationFile, replacing);
        }
        catch
        {
            // A first install is taken back; a replacement is left as far as it got.
            if (created)
                TryDeleteDirectory(destination);
            else if (!replacing)
                TryClearDirectory(destination);

            throw;
        }
        finally
        {
            TryDeleteFile(archivePath);
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

    private string Resolve(ModMetadata loader, InstallAnchor? target, string? path, string? directory, string? recorded, string? gameDirectory)
    {
        switch (target)
        {
            case null:
                throw new NotSupportedException($"The listing of {loader.Name} does not say where the loader goes, so Borea cannot install it and can only show its links.");

            case InstallAnchor.Standalone:
                if (directory is null && recorded is not null)
                    return recorded;

                var chosen = directory is null
                    ? Path.Combine(_pathProvider.GetLoadersRoot(), loader.ModId)
                    : Absolute(directory, nameof(directory));
                return Below(chosen, path);

            case InstallAnchor.GameRoot:
                if (directory is not null)
                    throw new ArgumentException($"Only a standalone loader lets Borea choose the directory; {loader.Name} goes below the game folder.", nameof(directory));

                if (gameDirectory is null)
                    throw new InvalidOperationException($"Borea does not know where the game is installed, so it cannot place {loader.Name}. Set the game directory first.");

                var gameRoot = Absolute(gameDirectory, nameof(gameDirectory));
                var placed = Below(gameRoot, path);
                if (SamePath(placed, gameRoot))
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

    private static bool SamePath(string left, string right)
    {
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            comparison);
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

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
