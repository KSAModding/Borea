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
    /// Where each installed mod loader lives, keyed by loader id. Empty means none.
    /// </summary>
    public IReadOnlyDictionary<string, string> LoaderDirectoryPaths { get; }

    public BoreaSettings(string? gameDirectoryPath, IReadOnlyDictionary<string, string>? loaderDirectoryPaths = null)
    {
        if (gameDirectoryPath is not null && string.IsNullOrWhiteSpace(gameDirectoryPath))
            throw new ArgumentException("Game directory path, if provided, cannot be whitespace.", nameof(gameDirectoryPath));

        GameDirectoryPath = gameDirectoryPath;
        LoaderDirectoryPaths = Build(loaderDirectoryPaths, nameof(loaderDirectoryPaths));
    }

    /// <summary>
    /// A copy with the game directory replaced. The loaders stay as they are.
    /// </summary>
    public BoreaSettings WithGameDirectory(string? gameDirectoryPath)
        => new(gameDirectoryPath, LoaderDirectoryPaths);

    /// <summary>
    /// A copy with one loader's directory set. The id is stored as given here,
    /// also when the loader was known under another casing.
    /// </summary>
    public BoreaSettings WithLoaderDirectory(string loaderId, string directoryPath)
    {
        ModIds.Validate(loaderId, nameof(loaderId));

        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentException("Loader directory path cannot be null or whitespace.", nameof(directoryPath));

        // An assignment through the indexer keeps the key that is already
        // there, so the old casing goes first.
        var paths = new Dictionary<string, string>(LoaderDirectoryPaths, ModIds.Comparer);
        paths.Remove(loaderId);
        paths[loaderId] = directoryPath;

        return new BoreaSettings(GameDirectoryPath, paths);
    }

    private static IReadOnlyDictionary<string, string> Build(IReadOnlyDictionary<string, string>? paths, string paramName)
    {
        var built = new Dictionary<string, string>(ModIds.Comparer);

        foreach (var (loaderId, path) in paths ?? new Dictionary<string, string>())
        {
            ModIds.Validate(loaderId, paramName);

            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException($"The path for loader '{loaderId}' cannot be whitespace.", paramName);

            if (!built.TryAdd(loaderId, path))
                throw new ArgumentException($"Loader id '{loaderId}' appears more than once when compared case-insensitively.", paramName);
        }

        return built;
    }
}
