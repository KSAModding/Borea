namespace Borea.Core.Settings;

/// <summary>
/// Changes the folder that holds the Instances and Backups folders.
/// </summary>
public interface ILibraryFolderChanger
{
    /// <summary>
    /// Returns a refusal without changing anything, and throws when a started
    /// move fails or is cancelled, which keeps the previous library in use.
    /// </summary>
    /// <param name="folder">An absolute folder, or null for Borea's own folder.</param>
    Task<LibraryFolderChangeResult> ChangeAsync(
        string? folder,
        IProgress<LibraryMoveProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
