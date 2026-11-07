namespace Borea.Storage.Files;

internal static class FileSystemPaths
{
    private const int MaxLinkDepth = 32;

    /// <summary>Paths compare the way the file system does: without case on Windows and macOS.</summary>
    public static StringComparison PathComparison => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    public static bool SamePath(string left, string right)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            PathComparison);
    }

    /// <summary>
    /// The full path with each link among its existing folders replaced by the
    /// folder it points to. The part below the first folder that does not exist
    /// is kept as it is, so a folder that is not created yet still resolves.
    /// </summary>
    public static string ResolveLinks(string path) => ResolveLinks(Normalize(path), depth: 0);

    private static string ResolveLinks(string path, int depth)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root) || depth > MaxLinkDepth)
            return path;

        var names = path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var resolved = root;
        for (var index = 0; index < names.Length; index++)
        {
            var next = Path.Combine(resolved, names[index]);
            if (LinkTarget(next) is { } linked)
                return ResolveLinks(Path.Combine([Normalize(linked), .. names[(index + 1)..]]), depth + 1);

            if (!Directory.Exists(next))
                return Path.Combine([next, .. names[(index + 1)..]]);

            resolved = next;
        }

        return resolved;
    }

    private static string? LinkTarget(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).ResolveLinkTarget(returnFinalTarget: true)?.FullName;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
