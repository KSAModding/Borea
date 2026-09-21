namespace Borea.Storage.Files;

/// <param name="VolumeName">The mount point that holds the path, so that two paths on one volume share its space.</param>
/// <param name="AvailableBytes">What is left on that volume for this user.</param>
public sealed record FreeSpace(string VolumeName, long AvailableBytes);

/// <summary>
/// Reads how much room is left where Borea is about to write.
/// </summary>
public interface IFreeSpaceProbe
{
    /// <summary>
    /// The free space of the volume that holds <paramref name="path"/>, which
    /// does not have to exist yet. Null when no volume can be read for it.
    /// </summary>
    FreeSpace? Measure(string path);
}
