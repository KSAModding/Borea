namespace Borea.Core.Files;

/// <summary>
/// Makes one folder on disk reachable under a second path without a copy, and
/// removes such a link again.
/// </summary>
public interface IDirectoryLinker
{
    /// <summary>
    /// Points <paramref name="linkPath"/> at <paramref name="targetPath"/>.
    /// Never throws because the platform, the filesystem, or the target does not
    /// support a link, so a caller that cannot link is free to copy instead.
    /// </summary>
    DirectoryLinkResult TryCreate(string linkPath, string targetPath);

    /// <summary>
    /// True when the path is a link and not a folder of its own, including a
    /// link whose target is gone.
    /// </summary>
    bool IsLink(string path);

    /// <summary>
    /// The target the link holds, or null when the path is no link. The target
    /// is returned as the link stores it.
    /// </summary>
    string? GetTarget(string path);

    /// <summary>
    /// Removes the link and leaves its target and the files below it untouched.
    /// Does nothing when the path does not exist, and throws when it is a folder
    /// of its own.
    /// </summary>
    void Remove(string linkPath);
}
