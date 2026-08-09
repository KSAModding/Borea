namespace Borea.Core.Mods;

/// <summary>
/// The author's declaration about their own listing.
/// </summary>
public enum ModStatus
{
    /// <summary>
    /// The listing is maintained. The default.
    /// </summary>
    Active = 0,

    /// <summary>
    /// The author has stopped maintaining it. Clients warn, never block.
    /// </summary>
    Deprecated = 1,

    /// <summary>
    /// A value this client version does not know.
    /// </summary>
    Unknown = 2,
}
