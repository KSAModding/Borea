using System.Globalization;

namespace Borea.Core.Planning;

/// <summary>
/// The disk does not hold what an install plan writes. It is raised before the
/// first download starts, so nothing is written and nothing has to be cleaned
/// up.
/// </summary>
public sealed class InsufficientDiskSpaceException : IOException
{
    private static readonly string[] Units = ["KB", "MB", "GB", "TB"];

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

    /// <summary>A byte count in decimal units, the way the download progress counts them.</summary>
    private static string Size(long bytes)
    {
        if (bytes < 1000)
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";

        var value = bytes / 1000.0;
        var unit = 0;
        while (value >= 999.95 && unit < Units.Length - 1)
        {
            value /= 1000;
            unit++;
        }

        return value.ToString("0.0", CultureInfo.InvariantCulture) + " " + Units[unit];
    }
}
