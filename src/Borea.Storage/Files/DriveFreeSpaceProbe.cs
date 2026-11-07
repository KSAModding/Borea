namespace Borea.Storage.Files;

/// <summary>
/// <see cref="IFreeSpaceProbe"/> over <see cref="DriveInfo"/>. The volume of a
/// path is the longest mount point that holds it, so a library folder and a
/// temporary folder on one mount are seen as one volume.
/// </summary>
public sealed class DriveFreeSpaceProbe : IFreeSpaceProbe
{
    public FreeSpace? Measure(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            return null;

        try
        {
            var drives = DriveInfo.GetDrives();
            var mountPoint = MountPointOf(path, drives.Select(drive => drive.RootDirectory.FullName));
            var holder = mountPoint is null ? null : drives.First(drive => string.Equals(Normalize(drive.RootDirectory.FullName), mountPoint, PathComparison));

            return holder is { IsReady: true }
                ? new FreeSpace(holder.Name, holder.AvailableFreeSpace)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// The longest of <paramref name="mountPoints"/> that holds the path, or null when none does.
    /// Links in the path are resolved first, because the mount table holds real paths: on an atomic
    /// Linux system /home is a link into /var, and the read-only root above it reports no free space.
    /// </summary>
    internal static string? MountPointOf(string path, IEnumerable<string> mountPoints)
    {
        var full = Normalize(FileSystemPaths.ResolveLinks(path));
        string? holder = null;
        foreach (var mountPoint in mountPoints)
        {
            var root = Normalize(mountPoint);
            if (IsSameOrInside(full, root) && (holder is null || root.Length > holder.Length))
                holder = root;
        }

        return holder;
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsSameOrInside(string path, string folder)
    {
        if (string.Equals(path, folder, PathComparison))
            return true;

        var prefix = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
        return path.Length > prefix.Length && path.StartsWith(prefix, PathComparison);
    }

    private static StringComparison PathComparison => FileSystemPaths.PathComparison;
}
