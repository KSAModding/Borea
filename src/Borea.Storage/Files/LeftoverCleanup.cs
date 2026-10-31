namespace Borea.Storage.Files;

/// <summary>
/// Cleanup that must not replace the outcome. What cannot be deleted stays
/// behind, and the error that caused the cleanup still wins.
/// </summary>
internal static class LeftoverCleanup
{
    public static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>A link is removed as a link, so its target keeps its files. False when the folder or a part of it stays.</summary>
    public static bool TryDeleteTree(string path)
    {
        try
        {
            DirectoryLinks.DeleteTreeWithoutFollowingLinks(path);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
