using Borea.Core.Mods;

namespace Borea.Core.ModLoaders;

/// <summary>
/// Reads the game path named by a loader's authored configuration contract.
/// </summary>
public interface ILoaderConfigurationReader
{
    /// <summary>
    /// Returns the configured game path without changing the loader's file.
    /// Null means that no path is declared, the file is absent, or the key is
    /// absent.
    /// </summary>
    Task<string?> ReadConfiguredGamePathAsync(
        ModMetadata loader,
        string loaderDirectory,
        CancellationToken cancellationToken = default);
}
