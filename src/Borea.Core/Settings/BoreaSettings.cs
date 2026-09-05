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
