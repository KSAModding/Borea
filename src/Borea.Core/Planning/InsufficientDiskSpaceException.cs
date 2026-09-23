using System.Globalization;
using Borea.Core.Files;

namespace Borea.Core.Planning;

/// <summary>
/// The disk does not hold what an install plan writes. It is raised before the
/// first download starts, so nothing is written and nothing has to be cleaned
/// up.
/// </summary>
public sealed class InsufficientDiskSpaceException : IOException
{
    public InsufficientDiskSpaceException(string volumeName, long requiredBytes, long availableBytes)
        : base($"The install needs {Size(requiredBytes)} on {volumeName}, and only {Size(availableBytes)} is free.")
    {
        VolumeName = volumeName;
        RequiredBytes = requiredBytes;
        AvailableBytes = availableBytes;
    }

    /// <summary>The volume that is too full, as the operating system names it.</summary>
    public string VolumeName { get; }

    /// <summary>What the plan needs on that volume, margin included.</summary>
    public long RequiredBytes { get; }

    /// <summary>What the volume has left.</summary>
    public long AvailableBytes { get; }

    private static string Size(long bytes) => ByteSize.Format(bytes, CultureInfo.InvariantCulture);
}
