using Borea.Core.Mods;

namespace Borea.Core.ModLoaders;

/// <summary>
/// Writes the loader's own configuration file per <c>[provides.configure]</c>
/// of RFC 0035.
/// </summary>
public interface ILoaderConfigurator
{
    /// <summary>
    /// Writes the game directory, as <see cref="GamePathValue"/> gives it, to
    /// the key <c>game-path</c> names, in the file below
    /// <paramref name="loaderDirectory"/>, and returns the file's absolute
    /// path, or null when the listing names no key.
    /// </summary>
    /// <exception cref="ArgumentException">The listing is not a mod loader, or a directory is not absolute.</exception>
    /// <exception cref="InvalidOperationException">
    /// The file cannot be read as its stated format, holds a key twice, a
    /// value sits where the key path expects a table, or no drive of the Wine
    /// prefix around the game holds it. Nothing is written then.
    /// </exception>
    /// <exception cref="NotSupportedException">The listing names a format this configurator cannot write.</exception>
    Task<string?> ConfigureAsync(
        ModMetadata loader,
        string loaderDirectory,
        string gameDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The value <see cref="ConfigureAsync"/> writes for
    /// <paramref name="gameDirectory"/>: the absolute host path, or the Windows
    /// path through the drives of the Wine prefix that holds the game, because
    /// the loader runs in that prefix too. A native Linux build in a prefix
    /// keeps the host path, because the host starts it and its loader. It
    /// writes nothing, so a caller can stop before it changes a file.
    /// </summary>
    /// <exception cref="ArgumentException">The directory is not absolute.</exception>
    /// <exception cref="InvalidOperationException">The game is in a Wine prefix, and no drive of the prefix holds it.</exception>
    string GamePathValue(string gameDirectory);
}
