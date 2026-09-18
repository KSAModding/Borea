namespace Borea.Core.ModLoaders;

/// <summary>
/// The runtime of RFC 0067 that runs a loader's entry file.
/// </summary>
public enum LoaderRuntime
{
    /// <summary>The dotnet command, with the entry file as its first argument.</summary>
    Dotnet = 0,

    /// <summary>
    /// The index rejects it at publish time, a client keeps the listing and starts nothing.
    /// </summary>
    Unknown = 1,
}
