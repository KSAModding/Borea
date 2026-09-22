namespace Borea.Core.Updates;

/// <summary>Where a self-update stopped, so that each program can say it in its own words.</summary>
public enum SelfUpdateFailure
{
    /// <summary>The build may not replace itself. <see cref="SelfUpdateReadiness"/> says why.</summary>
    Blocked = 0,

    /// <summary>The release publishes no archive for this build.</summary>
    NoArchive = 1,

    /// <summary>The archive did not arrive.</summary>
    Download = 2,

    /// <summary>The release publishes no checksum for the archive, or the archive does not match it.</summary>
    Checksum = 3,

    /// <summary>The archive arrived but could not be unpacked into a build.</summary>
    Unpack = 4,

    /// <summary>The new build is unpacked and did not take the place of the running one, which keeps working.</summary>
    Install = 5,

    /// <summary>The new build is in place and did not start, and the replaced build was put back.</summary>
    Start = 6,

    /// <summary>The program file was moved aside and could not be put back, so Borea has to be repaired by hand.</summary>
    Restore = 7,
}
