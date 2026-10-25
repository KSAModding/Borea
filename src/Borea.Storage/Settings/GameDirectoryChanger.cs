using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Settings;
using Borea.Storage.Files;

namespace Borea.Storage.Settings;

public sealed class GameDirectoryChanger : IGameDirectoryChanger
{
    private readonly IBoreaSettingsRepository _settings;
    private readonly IModRepository _mods;
    private readonly ILoaderConfigurator _configurator;
    private readonly ILoaderVersionStore _versions;

    public GameDirectoryChanger(
        IBoreaSettingsRepository settings,
        IModRepository mods,
        ILoaderConfigurator configurator,
        ILoaderVersionStore versions)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _mods = mods ?? throw new ArgumentNullException(nameof(mods));
        _configurator = configurator ?? throw new ArgumentNullException(nameof(configurator));
        _versions = versions ?? throw new ArgumentNullException(nameof(versions));
    }

    public async Task ChangeAsync(string gameDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
            throw new ArgumentException("A game directory is required.", nameof(gameDirectory));

        if (!Path.IsPathFullyQualified(gameDirectory))
            throw new ArgumentException("The game directory must be absolute.", nameof(gameDirectory));

        var fullGameDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDirectory));
        var previousSettings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false)
            ?? new BoreaSettings(gameDirectoryPath: null);
        var changedSettings = previousSettings.WithGameDirectory(fullGameDirectory);

        // The store lists the recorded folder of each loader, so a player without a loader still skips the listings here.
        var versions = await _versions.ListAsync(cancellationToken).ConfigureAwait(false);
        if (versions.Count == 0)
        {
            await _settings.SaveAsync(changedSettings, cancellationToken).ConfigureAwait(false);
            return;
        }

        var listings = await _mods.GetAvailableModsAsync(cancellationToken).ConfigureAwait(false);
        var changes = FindChanges(versions, listings);
        if (changes.Count > 0)
            _ = _configurator.GamePathValue(fullGameDirectory);

        var snapshots = await SnapshotAsync(changes, cancellationToken).ConfigureAwait(false);
        var settingsSaveStarted = false;

        try
        {
            foreach (var change in changes)
            {
                await _configurator.ConfigureAsync(
                    change.Loader,
                    change.LoaderDirectory,
                    fullGameDirectory,
                    cancellationToken).ConfigureAwait(false);
            }

            settingsSaveStarted = true;
            await _settings.SaveAsync(changedSettings, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            var rollbackFailures = await RestoreAsync(snapshots).ConfigureAwait(false);
            if (settingsSaveStarted)
            {
                var settingsFailure = await TryRestoreSettingsAsync(previousSettings).ConfigureAwait(false);
                if (settingsFailure is not null)
                    rollbackFailures.Add(settingsFailure);
            }

            if (rollbackFailures.Count > 0)
            {
                throw new AggregateException(
                    "The game directory change failed, and rollback could not restore a consistent state.",
                    new[] { failure }.Concat(rollbackFailures));
            }

            throw;
        }
    }

    /// <summary>
    /// One change per configuration file. Every version folder keeps its own game path, so Borea writes each version the store
    /// lists, which is the recorded folder of each loader, adopted or not, and every version folder Borea made below Loaders.
    /// </summary>
    private static IReadOnlyList<ConfigurationChange> FindChanges(
        IReadOnlyList<InstalledLoaderVersion> versions,
        IReadOnlyList<ModMetadata> listings)
    {
        var changes = new List<ConfigurationChange>();
        var seen = new HashSet<string>(PathComparer);
        foreach (var version in versions)
        {
            var loader = listings.FirstOrDefault(candidate =>
                candidate.Type == ContentType.ModLoader &&
                ModIds.Equals(candidate.ModId, version.LoaderId));
            var configure = loader?.Provides?.Configure;
            if (configure?.GamePath is null)
                continue;

            if (!Path.IsPathFullyQualified(version.DirectoryPath))
                throw new InvalidOperationException($"The saved directory for loader '{version.LoaderId}' is not absolute.");

            var loaderDirectory = Path.GetFullPath(version.DirectoryPath);
            var configurationPath = Path.GetFullPath(Path.Combine(
                loaderDirectory,
                configure.File.Replace('/', Path.DirectorySeparatorChar)));
            if (seen.Add(configurationPath))
                changes.Add(new ConfigurationChange(loader!, loaderDirectory, configurationPath));
        }

        return changes;
    }

    private static async Task<IReadOnlyList<ConfigurationSnapshot>> SnapshotAsync(
        IReadOnlyList<ConfigurationChange> changes,
        CancellationToken cancellationToken)
    {
        var snapshots = new List<ConfigurationSnapshot>();
        foreach (var path in changes.Select(change => change.ConfigurationPath).Distinct(PathComparer))
        {
            var content = File.Exists(path)
                ? await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false)
                : null;
            snapshots.Add(new ConfigurationSnapshot(path, content));
        }

        return snapshots;
    }

    private async Task<Exception?> TryRestoreSettingsAsync(BoreaSettings settings)
    {
        try
        {
            await _settings.SaveAsync(settings, CancellationToken.None).ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            return new InvalidOperationException("Borea could not restore its previous settings.", exception);
        }
    }

    private static async Task<List<Exception>> RestoreAsync(IReadOnlyList<ConfigurationSnapshot> snapshots)
    {
        var failures = new List<Exception>();
        foreach (var snapshot in snapshots.Reverse())
        {
            try
            {
                if (snapshot.Content is null)
                {
                    if (File.Exists(snapshot.Path))
                        File.Delete(snapshot.Path);
                }
                else
                {
                    await AtomicFile.WriteAllBytesAsync(snapshot.Path, snapshot.Content).ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                failures.Add(new InvalidOperationException(
                    $"Borea could not restore loader configuration '{snapshot.Path}'.",
                    exception));
            }
        }

        return failures;
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed record ConfigurationChange(ModMetadata Loader, string LoaderDirectory, string ConfigurationPath);

    private sealed record ConfigurationSnapshot(string Path, byte[]? Content);
}
