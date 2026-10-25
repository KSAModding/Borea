using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.Settings;

namespace Borea.Storage.ModLoaders;

/// <summary>
/// File-backed <see cref="ILoaderUninstaller"/>. A folder is first renamed to a hidden sibling, then the record is saved, and
/// the sibling is deleted last. The rename is the one step that can fail on a locked file, and it changes nothing when it
/// fails. The version store skips hidden folders, so a sibling that could not be deleted is never listed, and the next
/// removal deletes it when it is inside the Loaders root.
/// </summary>
public sealed class FileLoaderUninstaller : ILoaderUninstaller
{
    private const string RemovingMarker = ".borea-removing-";

    private static readonly StringComparison PathComparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private readonly IBoreaSettingsRepository _settings;
    private readonly IGamePathProvider _paths;
    private readonly ILoaderVersionStore _versions;
    private readonly Func<bool> _isGameRunning;

    /// <param name="isGameRunning">
    /// Whether a game or loader runs. Nothing is then removed, because Windows locks the files in use and the loader that
    /// runs could be the one that is removed. Null means that nothing runs.
    /// </param>
    public FileLoaderUninstaller(
        IBoreaSettingsRepository settings,
        IGamePathProvider paths,
        ILoaderVersionStore versions,
        Func<bool>? isGameRunning = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _versions = versions ?? throw new ArgumentNullException(nameof(versions));
        _isGameRunning = isGameRunning ?? (static () => false);
    }

    public async Task<LoaderUninstallResult> UninstallAsync(
        string loaderId,
        CancellationToken cancellationToken = default)
    {
        ModIds.Validate(loaderId, nameof(loaderId));

        if (_isGameRunning())
            return new LoaderUninstallResult(loaderId, null, RecordRemoved: false, DirectoryRemoved: false, LoaderUninstallFailure.GameRunning);

        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        if (settings is null || !settings.LoaderInstallations.TryGetValue(loaderId, out var installation))
            return new LoaderUninstallResult(loaderId, null, RecordRemoved: false, DirectoryRemoved: false, LoaderUninstallFailure.NotInstalled);

        // the other versions stay, so the record moves to one of them and an older Borea still finds a loader
        var remaining = InstalledLoaderVersion.Of(await _versions.ListAsync(cancellationToken).ConfigureAwait(false), loaderId)
            .Where(v => !v.IsRecorded && !SamePath(v.DirectoryPath, installation.DirectoryPath) && v.Version is not null)
            .ToList();
        var removal = await RemoveAsync(
            loaderId,
            installation.DirectoryPath,
            installation.IsAdopted,
            settings,
            current => Successor(remaining) is { } next
                ? current.WithLoaderInstallation(loaderId, next)
                : current.WithoutLoaderInstallation(loaderId),
            cancellationToken).ConfigureAwait(false);

        return new LoaderUninstallResult(
            loaderId,
            installation.DirectoryPath,
            RecordRemoved: removal.Saved is { } saved && !saved.LoaderInstallations.ContainsKey(loaderId),
            removal.DirectoryRemoved,
            Version: installation.Version,
            FolderDeleted: removal.FolderDeleted);
    }

    public async Task<LoaderUninstallResult> UninstallVersionAsync(
        string loaderId,
        ModVersion version,
        CancellationToken cancellationToken = default)
    {
        ModIds.Validate(loaderId, nameof(loaderId));

        if (_isGameRunning())
            return new LoaderUninstallResult(loaderId, null, RecordRemoved: false, DirectoryRemoved: false, LoaderUninstallFailure.GameRunning, version);

        var installed = await _versions.ListAsync(cancellationToken).ConfigureAwait(false);
        if (InstalledLoaderVersion.Find(installed, loaderId, version) is not { } removed)
            return new LoaderUninstallResult(loaderId, null, RecordRemoved: false, DirectoryRemoved: false, LoaderUninstallFailure.NotInstalled, version);

        var remaining = InstalledLoaderVersion.Of(installed, removed.LoaderId)
            .Where(v => !ReferenceEquals(v, removed) && v.Version is not null)
            .ToList();
        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false) ?? new BoreaSettings(null);

        var removal = await RemoveAsync(
            removed.LoaderId,
            removed.DirectoryPath,
            removed.IsAdopted,
            settings,
            current => NextRecord(current, removed, remaining),
            cancellationToken).ConfigureAwait(false);

        return new LoaderUninstallResult(
            removed.LoaderId,
            removed.DirectoryPath,
            RecordRemoved: removal.Saved is { } saved && !saved.LoaderInstallations.ContainsKey(removed.LoaderId),
            removal.DirectoryRemoved,
            Version: version,
            FolderDeleted: removal.FolderDeleted);
    }

    /// <summary>
    /// The record keeps naming a version that an older Borea can start. A release wins over a prerelease, so the removal of a
    /// release never makes a prerelease start every instance, and a version the player only pointed Borea at comes last.
    /// </summary>
    /// <returns>The settings to save, or null when the record names another version and stays.</returns>
    private static BoreaSettings? NextRecord(BoreaSettings current, InstalledLoaderVersion removed, IReadOnlyList<InstalledLoaderVersion> remaining)
    {
        if (!current.LoaderInstallations.TryGetValue(removed.LoaderId, out var record) || !SamePath(record.DirectoryPath, removed.DirectoryPath))
            return null;

        return Successor(remaining) is { } next
            ? current.WithLoaderInstallation(removed.LoaderId, next)
            : current.WithoutLoaderInstallation(removed.LoaderId);
    }

    /// <summary>The newest remaining release that Borea installed, else the newest remaining version, or null when none remains.</summary>
    /// <param name="remaining">The versions that stay, newest first, each with a known version.</param>
    private static LoaderInstallation? Successor(IReadOnlyList<InstalledLoaderVersion> remaining)
    {
        var next = remaining.FirstOrDefault(v => !v.IsAdopted && v.Version!.Value.PreRelease is null) ?? remaining.FirstOrDefault();
        return next is null ? null : new LoaderInstallation(next.DirectoryPath, next.Version, next.RawVersion, next.IsAdopted);
    }

    /// <summary>Renames the folder away, saves the record, and deletes the folder. A failed save renames the folder back.</summary>
    private async Task<(bool DirectoryRemoved, bool FolderDeleted, BoreaSettings? Saved)> RemoveAsync(
        string loaderId,
        string directory,
        bool isAdopted,
        BoreaSettings settings,
        Func<BoreaSettings, BoreaSettings?> record,
        CancellationToken cancellationToken)
    {
        var folder = FolderToRemove(loaderId, directory, isAdopted);
        string? removing = null;
        if (folder is not null)
        {
            SweepLeftovers(loaderId);
            if (Directory.Exists(folder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                removing = RemovingPath(folder);
                Directory.Move(folder, removing);
            }
        }

        var saved = record(settings);
        try
        {
            if (saved is not null)
                await _settings.SaveAsync(saved, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception saveError) when (removing is not null)
        {
            try
            {
                Directory.Move(removing, folder!);
            }
            catch (Exception rollbackError)
            {
                throw new AggregateException(
                    $"Borea could not save the loader record, and it could not put '{folder}' back. The loader is now in '{removing}'.",
                    saveError,
                    rollbackError);
            }

            throw;
        }

        return (removing is not null, removing is not null && TryDeleteDirectory(removing), saved);
    }

    /// <summary>
    /// The folder that holds the installation, or null when it must stay. A folder outside the Loaders root that the player
    /// owns stays. Inside the root, a version folder is removed as a whole, also when the install root of the loader is a
    /// path below it, and the loader folder Loaders/&lt;id&gt; stays while it holds version folders, because it would take
    /// every version with it.
    /// </summary>
    private string? FolderToRemove(string loaderId, string directory, bool isAdopted)
    {
        if (!Path.IsPathFullyQualified(directory))
        {
            if (isAdopted)
                return null;

            throw new InvalidOperationException($"The recorded directory for '{loaderId}' is not absolute, so Borea will not delete it.");
        }

        var root = _paths.GetLoadersRoot();
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (!LoaderLayoutMigration.IsBelowLoadersRoot(root, full))
            return isAdopted ? null : full;

        var loaderFolder = Path.Combine(root, loaderId);
        if (SamePath(full, loaderFolder))
            return HoldsVersionFolders(loaderFolder) ? null : full;

        if (!LoaderLayoutMigration.IsBelowLoadersRoot(loaderFolder, full))
            return full;

        var versionFolder = Path.GetRelativePath(loaderFolder, full)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)[0];
        return Path.Combine(loaderFolder, versionFolder);
    }

    /// <summary>
    /// Deletes the siblings that an earlier removal could not delete, next to a legacy loader folder in the Loaders root and
    /// next to a version folder in Loaders/&lt;id&gt;.
    /// </summary>
    private void SweepLeftovers(string loaderId)
    {
        var root = _paths.GetLoadersRoot();
        foreach (var parent in new[] { root, Path.Combine(root, loaderId) })
        {
            foreach (var leftover in Leftovers(parent))
                TryDeleteDirectory(leftover);
        }
    }

    private static IEnumerable<string> Leftovers(string parent)
    {
        try
        {
            if (!Directory.Exists(parent))
                return [];

            return Directory.EnumerateDirectories(parent, ".*" + RemovingMarker + "*")
                .Where(path => Path.GetFileName(path) is { } name && name.StartsWith('.') && name.Contains(RemovingMarker, StringComparison.Ordinal))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool HoldsVersionFolders(string folder) =>
        Directory.Exists(folder)
        && Directory.EnumerateDirectories(folder).Any(entry => Path.GetFileName(entry) is { } name && !name.StartsWith('.') && ModVersion.TryParse(name, out _));

    private static string RemovingPath(string folder)
    {
        var parent = Path.GetDirectoryName(folder);
        var name = Path.GetFileName(folder);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            throw new NotSupportedException("A loader installed at a file-system root cannot be removed safely.");

        return Path.Combine(parent, $".{name}{RemovingMarker}{Guid.NewGuid():N}");
    }

    /// <returns>True when the folder is gone.</returns>
    private static bool TryDeleteDirectory(string path)
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

        return !Directory.Exists(path);
    }

    private static bool SamePath(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        PathComparison);
}
