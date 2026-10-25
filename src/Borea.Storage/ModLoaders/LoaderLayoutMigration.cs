using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.Settings;

namespace Borea.Storage.ModLoaders;

/// <summary>
/// Moves the flat folder of a loader, Loaders/&lt;id&gt;, into the version folder Loaders/&lt;id&gt;/&lt;version&gt; once, and repairs
/// a move that stopped half way. Borea 0.5.1 and older kept one version per loader in the flat folder, and an older Borea still
/// reads the record that the move writes.
/// </summary>
public sealed class LoaderLayoutMigration
{
    private const string MovingMarker = ".borea-moving-";

    private static readonly StringComparison PathComparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private readonly IBoreaSettingsRepository _settings;
    private readonly IGamePathProvider _paths;

    public LoaderLayoutMigration(IBoreaSettingsRepository settings, IGamePathProvider paths)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    /// <summary>
    /// True when the directory is inside the Loaders root. That root is Borea's own data, so a loader there is Borea-owned
    /// even when the player pointed Borea at it.
    /// </summary>
    public static bool IsBelowLoadersRoot(string loadersRoot, string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loadersRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(loadersRoot)) + Path.DirectorySeparatorChar;
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        return path.Length > root.Length && path.StartsWith(root, PathComparison);
    }

    /// <summary>
    /// Finishes or rolls back each move that a crash interrupted, and points a record that still names the flat folder at the
    /// version folder that the move made. A record at the flat folder would otherwise let an uninstall delete every version.
    /// </summary>
    /// <returns>The number of repairs.</returns>
    public async Task<int> RecoverAsync(CancellationToken cancellationToken = default)
    {
        var root = _paths.GetLoadersRoot();
        if (!Directory.Exists(root))
            return 0;

        var repairs = 0;
        foreach (var moving in Directory.EnumerateDirectories(root, ".*" + MovingMarker + "*").ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (MovedLoaderId(Path.GetFileName(moving)) is { } loaderId && await RecoverMoveAsync(moving, loaderId, cancellationToken).ConfigureAwait(false))
                repairs++;
        }

        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        if (settings is null)
            return repairs;

        var repaired = settings;
        foreach (var (loaderId, record) in settings.LoaderInstallations)
        {
            var folder = Path.Combine(root, loaderId);
            if (!SamePath(record.DirectoryPath, folder) || MovedVersionFolder(folder, record.Version) is not { } moved)
                continue;

            repaired = repaired.WithLoaderInstallation(
                loaderId,
                new LoaderInstallation(moved.Directory, record.Version ?? moved.Version, record.RawVersion, isAdopted: false));
            repairs++;
        }

        if (!ReferenceEquals(repaired, settings))
            await _settings.SaveAsync(repaired, cancellationToken).ConfigureAwait(false);

        return repairs;
    }

    /// <summary>
    /// What the flat folder Loaders/&lt;id&gt; holds. The launch file decides, and not the record, because the player can have put
    /// a loader there by hand or adopted it.
    /// </summary>
    internal FlatLoaderFolder Inspect(string loaderId, string? launch, LoaderInstallation? record)
    {
        var folder = FlatFolder(loaderId);
        if (!Directory.Exists(folder))
            return new FlatLoaderFolder(FlatLoaderFolderKind.None, null);

        var launchFile = launch is null ? null : Path.Combine(folder, launch.Replace('/', Path.DirectorySeparatorChar));
        if (launchFile is not null && File.Exists(launchFile))
        {
            var version = record is not null && SamePath(record.DirectoryPath, folder) ? record.Version : null;
            if (version is null && FileLoaderAdopter.TryParseFileVersion(FileLoaderAdopter.ReadRawVersion(launchFile), out var fileVersion))
                version = fileVersion;

            return new FlatLoaderFolder(FlatLoaderFolderKind.Loader, version);
        }

        var foreign = Directory.EnumerateFileSystemEntries(folder).Any(entry => !IsVersionFolderOrHidden(entry));
        return new FlatLoaderFolder(foreign ? FlatLoaderFolderKind.Foreign : FlatLoaderFolderKind.None, null);
    }

    /// <summary>
    /// Moves the flat folder to Loaders/&lt;id&gt;/&lt;version&gt; through a sibling, because a folder cannot be moved into itself.
    /// A record that names the flat folder is saved last and becomes Borea-owned. A failure puts the flat folder back.
    /// </summary>
    internal async Task MoveLegacyAsync(string loaderId, ModVersion version, CancellationToken cancellationToken)
    {
        var folder = FlatFolder(loaderId);
        var moving = Path.Combine(_paths.GetLoadersRoot(), $".{Path.GetFileName(folder)}{MovingMarker}{Guid.NewGuid():N}");
        var target = Path.Combine(folder, version.ToString());

        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false) ?? new BoreaSettings(null);
        var recorded = settings.LoaderInstallations.FirstOrDefault(pair => SamePath(pair.Value.DirectoryPath, folder));

        Directory.Move(folder, moving);
        var created = false;
        var moved = false;
        try
        {
            Directory.CreateDirectory(folder);
            created = true;
            Directory.Move(moving, target);
            moved = true;

            if (recorded.Value is { } record)
            {
                await _settings.SaveAsync(
                    settings.WithLoaderInstallation(recorded.Key, new LoaderInstallation(target, version, record.RawVersion, isAdopted: false)),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception moveError)
        {
            try
            {
                if (moved)
                    Directory.Move(target, moving);
                if (created)
                    Directory.Delete(folder);
                Directory.Move(moving, folder);
            }
            catch (Exception rollbackError)
            {
                throw new AggregateException(
                    $"Borea could not move '{folder}' into a version folder, and it could not put it back. The loader is now in '{moving}'.",
                    moveError,
                    rollbackError);
            }

            throw;
        }
    }

    private string FlatFolder(string loaderId) => Path.Combine(_paths.GetLoadersRoot(), loaderId);

    /// <summary>
    /// A move with a known version is finished, because the record still names the flat folder and only the version folder
    /// can satisfy it. Without a version the move is rolled back, which is safe while the flat folder is missing or empty.
    /// </summary>
    private async Task<bool> RecoverMoveAsync(string moving, string loaderId, CancellationToken cancellationToken)
    {
        var folder = FlatFolder(loaderId);
        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        var recorded = settings?.LoaderInstallations.FirstOrDefault(pair => SamePath(pair.Value.DirectoryPath, folder)) ?? default;

        if (recorded.Value?.Version is { } version && !Directory.Exists(Path.Combine(folder, version.ToString())))
        {
            var target = Path.Combine(folder, version.ToString());
            Directory.CreateDirectory(folder);
            Directory.Move(moving, target);
            await _settings.SaveAsync(
                settings!.WithLoaderInstallation(recorded.Key, new LoaderInstallation(target, version, recorded.Value.RawVersion, isAdopted: false)),
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            return false;

        if (Directory.Exists(folder))
            Directory.Delete(folder);
        Directory.Move(moving, folder);
        return true;
    }

    /// <summary>
    /// The version folder that a record at the flat folder belongs to, when the flat folder holds only version folders. The
    /// folder of the recorded version wins, else the newest. A loader in the flat folder always holds a file or a folder
    /// whose name is not a version, so this never matches a flat install.
    /// </summary>
    private static (string Directory, ModVersion Version)? MovedVersionFolder(string folder, ModVersion? recordedVersion)
    {
        if (!Directory.Exists(folder))
            return null;

        var versions = new List<(string Directory, ModVersion Version)>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
        {
            var name = Path.GetFileName(entry);
            if (!IsVersionFolderOrHidden(entry))
                return null;

            if (!name.StartsWith('.') && ModVersion.TryParse(name, out var version) && Directory.EnumerateFileSystemEntries(entry).Any())
                versions.Add((entry, version));
        }

        if (versions.Count == 0)
            return null;

        var named = versions.FindIndex(v => v.Version == recordedVersion);
        return named >= 0 ? versions[named] : versions.MaxBy(v => v.Version);
    }

    private static string? MovedLoaderId(string name)
    {
        var marker = name.LastIndexOf(MovingMarker, StringComparison.Ordinal);
        if (marker <= 1)
            return null;

        var loaderId = name[1..marker];
        return ModIds.IsValid(loaderId) ? loaderId : null;
    }

    /// <summary>
    /// Hidden entries are Borea's own transaction folders or files that the system adds, as .DS_Store on macOS, so they are
    /// never part of a loader.
    /// </summary>
    private static bool IsVersionFolderOrHidden(string entry)
    {
        var name = Path.GetFileName(entry);
        return name.StartsWith('.') || (Directory.Exists(entry) && ModVersion.TryParse(name, out _));
    }

    private static bool SamePath(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        PathComparison);
}

internal enum FlatLoaderFolderKind
{
    /// <summary>The flat folder is missing, empty, or holds only version folders and hidden folders.</summary>
    None,

    /// <summary>The flat folder holds the launch file, so it is one loader version in the layout of Borea 0.5.1.</summary>
    Loader,

    /// <summary>The flat folder holds other files, so Borea must not put a version folder into it.</summary>
    Foreign,
}

/// <param name="Version">The version of the loader in the flat folder, or null when neither the record nor the file tells it.</param>
internal readonly record struct FlatLoaderFolder(FlatLoaderFolderKind Kind, ModVersion? Version);
