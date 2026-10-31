namespace Borea.Storage.Files;

internal static class FileSystemPaths
{
    /// <summary>Paths compare the way the file system does: without case on Windows and macOS.</summary>
    public static StringComparison PathComparison => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    public static bool SamePath(string left, string right)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            PathComparison);
    }
}
