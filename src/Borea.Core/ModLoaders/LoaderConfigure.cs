using Borea.Core.Mods;

namespace Borea.Core.ModLoaders;

/// <summary>
/// The [provides.configure] table of RFC 0035.
/// </summary>
public sealed class LoaderConfigure
{
    /// <summary>Relative to the loader's install location.</summary>
    public string File { get; }

    public ConfigureFormat Format { get; }

    /// <summary>
    /// The key that receives the game directory, dot-separated from the root.
    /// </summary>
    public string? GamePath { get; }

    public LoaderConfigure(string file, ConfigureFormat format, string? gamePath = null)
    {
        if (string.IsNullOrWhiteSpace(file))
            throw new ArgumentException("The configuration file is required.", nameof(file));

        if (gamePath is not null && string.IsNullOrWhiteSpace(gamePath))
            throw new ArgumentException("The game path key, if provided, cannot be whitespace.", nameof(gamePath));

        File = RelativePaths.Contained(file, nameof(file))!;
        Format = format;
        GamePath = gamePath;
    }
}
