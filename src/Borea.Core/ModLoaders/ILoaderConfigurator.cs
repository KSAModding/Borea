using Borea.Core.Mods;

namespace Borea.Core.ModLoaders;

/// <summary>
/// Writes the loader's own configuration file per <c>[provides.configure]</c>
/// of RFC 0035.
/// </summary>
public interface ILoaderConfigurator
{
    /// <summary>
    /// Writes the absolute game directory to the key <c>game-path</c> names, in
    /// the file below <paramref name="loaderDirectory"/>, and returns the
    /// file's absolute path, or null when the listing names no key.
    /// </summary>
    /// <exception cref="ArgumentException">The listing is not a mod loader, or a directory is not absolute.</exception>
    /// <exception cref="InvalidOperationException">
    /// The file cannot be read as its stated format, holds a key twice, or a
    /// value sits where the key path expects a table.
    /// </exception>
    /// <exception cref="NotSupportedException">The listing names a format this configurator cannot write.</exception>
    Task<string?> ConfigureAsync(
        ModMetadata loader,
        string loaderDirectory,
        string gameDirectory,
        CancellationToken cancellationToken = default);
}
