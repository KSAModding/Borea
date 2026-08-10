namespace Borea.Core.Mods;

/// <summary>
/// The maturity of one release, derived at stamp time.
/// </summary>
public enum ReleaseStatus
{
    /// <summary>
    /// A regular release.
    /// </summary>
    Stable = 0,

    /// <summary>
    /// A pre-release the author published for testing.
    /// </summary>
    Testing = 1,

    /// <summary>
    /// A development build.
    /// </summary>
    Dev = 2,

    /// <summary>
    /// A value this client version does not know.
    /// </summary>
    Unknown = 3,
}
