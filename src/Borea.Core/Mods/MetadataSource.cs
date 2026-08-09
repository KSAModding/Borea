namespace Borea.Core.Mods;

/// <summary>
/// Where an entry in a generated release file came from.
/// </summary>
public enum MetadataSource
{
    /// <summary>
    /// Written by the author in the authored index file.
    /// </summary>
    Authored = 0,

    /// <summary>
    /// Derived by tooling from the release archive's own mod.toml.
    /// </summary>
    Derived = 1,

    /// <summary>
    /// A value this client version does not know.
    /// </summary>
    Unknown = 2,
}
