namespace Borea.Storage.Files;

/// <summary>
/// The filesystem primitives behind a directory link, shared by the linker and
/// by every delete that must not follow a link.
/// </summary>
internal static class DirectoryLinks
{
    /// <summary>
    /// True for a junction and for a symbolic link, and still true when the
    /// target is gone, because the link holds its target either way. Every other
    /// reparse point is a folder of its own, so it keeps its files and is never
    /// removed as a link.
    /// </summary>
    public static bool IsLink(FileSystemInfo entry) => entry.LinkTarget is not null;

    /// <summary>
    /// Removes the link itself. Windows removes a junction as the directory
    /// entry it is, while unlink removes a symbolic link everywhere else.
    /// </summary>
    public static void DeleteLink(string path)
    {
        if (OperatingSystem.IsWindows())
            Directory.Delete(path);
        else
            File.Delete(path);
    }

    /// <summary>
    /// Removes the folder and everything below it, and removes a link below it
    /// as a link, so a link target keeps its files.
    /// </summary>
    public static void DeleteTreeWithoutFollowingLinks(string root)
    {
        var info = new DirectoryInfo(root);
        if (IsLink(info))
        {
            DeleteLink(root);
            return;
        }

        if (!info.Exists)
            return;

        foreach (var entry in info.EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo directory && !IsLink(directory))
                DeleteTreeWithoutFollowingLinks(directory.FullName);
            else if (entry is DirectoryInfo)
                DeleteLink(entry.FullName);
            else
                File.Delete(entry.FullName);
        }

        Directory.Delete(root);
    }
}
