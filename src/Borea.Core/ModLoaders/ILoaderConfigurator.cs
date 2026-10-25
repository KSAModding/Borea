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
    /// Writes the game path again before a launch when the loader runs in the
    /// Wine prefix of the game and its file holds no game path or the host
    /// path of the game, which an older Borea wrote and the loader in the
    /// prefix cannot open. Any other value stays, so a path the player set is
    /// kept. Returns the file it wrote, or null when it wrote nothing, which is
    /// always so outside a prefix, for a format it cannot write, for a file it
    /// cannot read and for a directory that is not absolute.
    /// </summary>
    /// <exception cref="ArgumentException">The listing is not a mod loader.</exception>
    /// <exception cref="InvalidOperationException">
    /// The file cannot be written, or no drive of the Wine prefix around the
    /// game holds it. Nothing is written then.
    /// </exception>
    Task<string?> RefreshForWineAsync(
        ModMetadata loader,
        string loaderDirectory,
        string gameDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the loader in <paramref name="loaderDirectory"/> start the game in
    /// <paramref name="gameDirectory"/> before a launch. The configured value is
    /// resolved the way StarMap does it, so a folder means KSA.dll in that folder
    /// and any other value names the file. A folder Borea made is configured
    /// again when its file is missing or its value does not name KSA.dll in the
    /// game folder, because each version folder keeps its own game path and
    /// Borea owns that value. An adopted folder is configured again only when
    /// its file is missing, its value is empty, or its value names no KSA.dll
    /// that exists, because the loader would refuse that value anyway. In every
    /// other case it does what <see cref="RefreshForWineAsync"/> does. Returns
    /// the file it wrote, or null when it wrote nothing, which is always so for
    /// a listing without a game path key, a format it cannot write, a
    /// directory that is not absolute and a loader folder that is not there.
    /// </summary>
    /// <exception cref="ArgumentException">The listing is not a mod loader.</exception>
    /// <exception cref="InvalidOperationException">
    /// The file cannot be read as its stated format or cannot be written, or
    /// no drive of the Wine prefix around the game holds it. Nothing is
    /// written then.
    /// </exception>
    Task<string?> PrepareForLaunchAsync(
        ModMetadata loader,
        string loaderDirectory,
        string gameDirectory,
        bool isAdopted,
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
