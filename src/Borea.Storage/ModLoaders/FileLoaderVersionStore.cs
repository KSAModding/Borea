using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.Settings;

namespace Borea.Storage.ModLoaders;

/// <summary>
/// File-backed <see cref="ILoaderVersionStore"/>. It lists the recorded installation of each loader and every version folder
/// below Loaders/&lt;id&gt;. It reads the settings again on each call, so a version that was installed after the services were built is found.
/// </summary>
public sealed class FileLoaderVersionStore : ILoaderVersionStore
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    private readonly IBoreaSettingsRepository _settings;
    private readonly IGamePathProvider _paths;

    public FileLoaderVersionStore(IBoreaSettingsRepository settings, IGamePathProvider paths)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task<IReadOnlyList<InstalledLoaderVersion>> ListAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        var records = settings?.LoaderInstallations ?? new Dictionary<string, LoaderInstallation>();

        var byPath = new Dictionary<string, InstalledLoaderVersion>(PathComparer);
        foreach (var (loaderId, record) in records)
        {
            byPath[Key(record.DirectoryPath)] = new InstalledLoaderVersion(
                loaderId, record.Version, record.RawVersion, record.DirectoryPath, record.IsAdopted, IsRecorded: true);
        }

        // A recorded id keeps the casing of its record, so the folder of a recorded loader does not show up as a second loader.
        var loaderIds = new Dictionary<string, string>(ModIds.Comparer);
        foreach (var loaderId in records.Keys)
            loaderIds.TryAdd(loaderId, loaderId);

        var root = _paths.GetLoadersRoot();
        foreach (var name in FolderNames(root))
        {
            if (ModIds.IsValid(name))
                loaderIds.TryAdd(name, name);
        }

        var found = new List<InstalledLoaderVersion>();
        foreach (var loaderId in loaderIds.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loaderFolder = Path.Combine(root, loaderId);

            // Ordinal order puts the canonical name first when two folders name the same version, as 0.4.7 and 0.4.7+build do.
            foreach (var name in FolderNames(loaderFolder).Order(StringComparer.Ordinal))
            {
                if (!ModVersion.TryParse(name, out var version))
                    continue;

                var directory = Path.Combine(loaderFolder, name);
                if (byPath.ContainsKey(Key(directory)) || !Directory.EnumerateFileSystemEntries(directory).Any())
                    continue;

                found.Add(new InstalledLoaderVersion(loaderId, version, RawVersion: null, directory, IsAdopted: false, IsRecorded: false));
            }
        }

        // The name of a folder is only a hint, because an older Borea updated a recorded folder in place. When two folders
        // claim one version, the recorded one stays, so a version always names one folder.
        var listed = byPath.Values.ToList();
        foreach (var candidate in found)
        {
            if (!listed.Any(v => ModIds.Equals(v.LoaderId, candidate.LoaderId) && v.Version == candidate.Version))
                listed.Add(candidate);
        }

        // A null version compares below every version, so the descending order puts the unknown versions last.
        return listed
            .OrderBy(v => v.LoaderId, ModIds.Comparer)
            .ThenByDescending(v => v.Version)
            .ThenBy(v => v.DirectoryPath, PathComparer)
            .ToList();
    }

    private static string Key(string directory) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));

    private static IEnumerable<string> FolderNames(string folder)
    {
        if (!Directory.Exists(folder))
            return [];

        return Directory.EnumerateDirectories(folder)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !name.StartsWith('.'))
            .ToList();
    }
}
