using Borea.Core.Files;

namespace Borea.Storage.Files;

/// <summary>
/// Links a directory with a junction on Windows and with a directory symbolic
/// link on Linux and macOS. Windows uses a junction because it needs neither
/// Developer Mode nor elevation and can point at another local volume.
/// </summary>
public sealed class DirectoryLinker : IDirectoryLinker
{
    public DirectoryLinkResult TryCreate(string linkPath, string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        string link;
        string target;
        try
        {
            link = Path.TrimEndingDirectorySeparator(Path.GetFullPath(linkPath));
            target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetPath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return DirectoryLinkResult.NotLinked(exception.Message);
        }

        if (!Directory.Exists(target))
            return DirectoryLinkResult.NotLinked($"The link target '{target}' is no folder.");

        if (Directory.Exists(link) || File.Exists(link) || IsLink(link))
            return DirectoryLinkResult.NotLinked($"'{link}' exists already.");

        var parent = Path.GetDirectoryName(link);
        var created = MissingFolders(parent);
        try
        {
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            if (OperatingSystem.IsWindows())
                WindowsJunction.Create(link, target);
            else
                Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            RemoveEmptyFolders(created);
            return DirectoryLinkResult.NotLinked(exception.Message);
        }

        // Windows writes a junction without ever reading its target, so the link
        // is proved to lead somewhere before a caller is told it can use it.
        if (!Directory.Exists(link))
        {
            TryRemoveUnusableLink(link);
            RemoveEmptyFolders(created);
            return DirectoryLinkResult.NotLinked($"The link at '{link}' does not reach '{target}'.");
        }

        return DirectoryLinkResult.Created;
    }

    public bool IsLink(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return DirectoryLinks.IsLink(new DirectoryInfo(path));
    }

    public string? GetTarget(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new DirectoryInfo(path).LinkTarget;
    }

    public void Remove(string linkPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkPath);

        var info = new DirectoryInfo(linkPath);
        if (!DirectoryLinks.IsLink(info))
        {
            if (info.Exists)
                throw new InvalidOperationException($"'{linkPath}' is a folder of its own and not a link.");

            return;
        }

        DirectoryLinks.DeleteLink(linkPath);
    }

    /// <summary>The folders a create of <paramref name="folder"/> would add, deepest first.</summary>
    private static List<string> MissingFolders(string? folder)
    {
        var missing = new List<string>();
        for (var current = folder; !string.IsNullOrEmpty(current) && !Directory.Exists(current); current = Path.GetDirectoryName(current))
            missing.Add(current);

        return missing;
    }

    /// <summary>
    /// Takes the folders back that were made for a link that was not made, and
    /// stops at the first one that now holds something of its own.
    /// </summary>
    private static void RemoveEmptyFolders(List<string> folders)
    {
        foreach (var folder in folders)
        {
            try
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return;
            }
        }
    }

    private void TryRemoveUnusableLink(string link)
    {
        try
        {
            Remove(link);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
        }
    }
}
