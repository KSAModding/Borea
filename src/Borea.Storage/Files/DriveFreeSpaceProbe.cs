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
            var full = Normalize(path);
            DriveInfo? holder = null;
            foreach (var drive in DriveInfo.GetDrives())
            {
                var root = Normalize(drive.RootDirectory.FullName);
                if (IsSameOrInside(full, root) && (holder is null || root.Length > Normalize(holder.RootDirectory.FullName).Length))
                    holder = drive;
            }

            return holder is { IsReady: true }
                ? new FreeSpace(holder.Name, holder.AvailableFreeSpace)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsSameOrInside(string path, string folder)
    {
        if (string.Equals(path, folder, PathComparison))
            return true;

        var prefix = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
        return path.Length > prefix.Length && path.StartsWith(prefix, PathComparison);
    }

    private static StringComparison PathComparison => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
}
