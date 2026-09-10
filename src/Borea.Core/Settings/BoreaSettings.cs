using Borea.Core.ModLoaders;
using Borea.Core.Mods;

namespace Borea.Core.Settings;

/// <summary>
/// Borea's own cross-platform configuration. No app level settings
/// are stored in this class.
/// </summary>
public sealed class BoreaSettings
{
    public string? GameDirectoryPath { get; }

    /// <summary>
    /// Installation state for each loader, keyed by loader id. Empty means none.
    /// </summary>
    public IReadOnlyDictionary<string, LoaderInstallation> LoaderInstallations { get; }

    public BoreaSettings(
        string? gameDirectoryPath,
        IReadOnlyDictionary<string, LoaderInstallation>? loaderInstallations = null)
    {
        if (gameDirectoryPath is not null && string.IsNullOrWhiteSpace(gameDirectoryPath))
            throw new ArgumentException("Game directory path, if provided, cannot be whitespace.", nameof(gameDirectoryPath));

        GameDirectoryPath = gameDirectoryPath;
        LoaderInstallations = Build(loaderInstallations, nameof(loaderInstallations));
    }

    /// <summary>
    /// A copy with the game directory replaced. The loaders stay as they are.
    /// </summary>
    public BoreaSettings WithGameDirectory(string? gameDirectoryPath)
        => new(gameDirectoryPath, LoaderInstallations);

    /// <summary>
    /// A copy with one loader installation set. The id is stored as given here,
    /// also when the loader was known under another casing.
    /// </summary>
    public BoreaSettings WithLoaderInstallation(string loaderId, LoaderInstallation installation)
    {
        ModIds.Validate(loaderId, nameof(loaderId));
        ArgumentNullException.ThrowIfNull(installation);

        // An assignment through the indexer keeps the key that is already
        // there, so the old casing goes first.
        var installations = new Dictionary<string, LoaderInstallation>(LoaderInstallations, ModIds.Comparer);
        installations.Remove(loaderId);
        installations[loaderId] = installation;

        return new BoreaSettings(GameDirectoryPath, installations);
    }

    private static IReadOnlyDictionary<string, LoaderInstallation> Build(
        IReadOnlyDictionary<string, LoaderInstallation>? installations,
        string paramName)
    {
        var built = new Dictionary<string, LoaderInstallation>(ModIds.Comparer);

        foreach (var (loaderId, installation) in installations ?? new Dictionary<string, LoaderInstallation>())
        {
            ModIds.Validate(loaderId, paramName);

            if (installation is null)
                throw new ArgumentException($"The installation for loader '{loaderId}' cannot be null.", paramName);

            if (!built.TryAdd(loaderId, installation))
                throw new ArgumentException($"Loader id '{loaderId}' appears more than once when compared case-insensitively.", paramName);
        }

        return built;
    }
}
