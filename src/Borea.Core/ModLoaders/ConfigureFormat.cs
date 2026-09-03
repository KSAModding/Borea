namespace Borea.Core.ModLoaders;

/// <summary>
/// The format of a loader's own configuration file.
/// </summary>
public enum ConfigureFormat
{
    Json = 0,

    Toml = 1,

    /// <summary>
    /// The index rejects it at publish time, a client keeps the listing.
    /// </summary>
    Unknown = 2,
}
