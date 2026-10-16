namespace Borea.Core.Game;

/// <summary>
/// How a release fits the installed game (RFC 0017, amended by RFC 0091).
/// </summary>
public enum GameCompatibility
{
    /// <summary>
    /// The installed revision is inside the stated range.
    /// </summary>
    Compatible = 0,

    /// <summary>
    /// The installed revision is below the lower bound or above a stated upper bound.
    /// </summary>
    Incompatible = 2,

    /// <summary>
    /// No usable compatibility data.
    /// </summary>
    Unknown = 3,
}
