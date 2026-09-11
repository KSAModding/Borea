namespace Borea.Core.ModLoaders;

public interface ILoaderUninstaller
{
    /// <summary>
    /// Removes the loader record. The directory is removed only when Borea
    /// created it.
    /// </summary>
    Task<LoaderUninstallResult> UninstallAsync(
        string loaderId,
        CancellationToken cancellationToken = default);
}

public sealed record LoaderUninstallResult(
    string LoaderId,
    string? Directory,
    bool RecordRemoved,
    bool DirectoryRemoved);
