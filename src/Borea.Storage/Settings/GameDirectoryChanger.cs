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

    public GameDirectoryChanger(
        IBoreaSettingsRepository settings,
        IModRepository mods,
        ILoaderConfigurator configurator)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _mods = mods ?? throw new ArgumentNullException(nameof(mods));
        _configurator = configurator ?? throw new ArgumentNullException(nameof(configurator));
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

        if (previousSettings.LoaderInstallations.Count == 0)
        {
            await _settings.SaveAsync(changedSettings, cancellationToken).ConfigureAwait(false);
            return;
        }

        var listings = await _mods.GetAvailableModsAsync(cancellationToken).ConfigureAwait(false);
        var changes = FindChanges(previousSettings, listings);
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

    private static IReadOnlyList<ConfigurationChange> FindChanges(
        BoreaSettings settings,
        IReadOnlyList<ModMetadata> listings)
    {
        var changes = new List<ConfigurationChange>();
        foreach (var (loaderId, installation) in settings.LoaderInstallations)
        {
            var loader = listings.FirstOrDefault(candidate =>
                candidate.Type == ContentType.ModLoader &&
                ModIds.Equals(candidate.ModId, loaderId));
            var configure = loader?.Provides?.Configure;
            if (configure?.GamePath is null)
                continue;

            if (!Path.IsPathFullyQualified(installation.DirectoryPath))
                throw new InvalidOperationException($"The saved directory for loader '{loaderId}' is not absolute.");

            var loaderDirectory = Path.GetFullPath(installation.DirectoryPath);
            var configurationPath = Path.GetFullPath(Path.Combine(
                loaderDirectory,
                configure.File.Replace('/', Path.DirectorySeparatorChar)));
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
