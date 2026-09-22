namespace Borea.Core.Game;

public enum GameShapeStatus
{
    /// <summary>Every assumption that could be checked holds, on a build the assumptions were verified against.</summary>
    Verified,

    /// <summary>Every assumption holds, but the build is newer than the newest verified one.</summary>
    Untested,

    /// <summary>At least one assumption does not hold.</summary>
    Broken,

    /// <summary>There was nothing to check, because no game installation was found.</summary>
    Unknown,
}
