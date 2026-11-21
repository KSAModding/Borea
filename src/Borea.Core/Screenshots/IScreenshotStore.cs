namespace Borea.Core.Screenshots;

/// <summary>
/// Lists and copies the pictures in a screenshots folder, such as the one of an instance or of the shared profile.
/// </summary>
public interface IScreenshotStore
{
    /// <summary>
    /// The images (png, jpg, jpeg) directly in the folder, newest first. An absent folder gives an empty list,
    /// and a file that vanishes or cannot be read during the scan is left out.
    /// </summary>
    Task<IReadOnlyList<Screenshot>> ListAsync(string folder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies the files into the folder and keeps the originals. A name that is taken gets " (2)", " (3)" and so on
    /// before the extension, so no file is overwritten.
    /// </summary>
    /// <exception cref="IOException">A file cannot be copied.</exception>
    /// <exception cref="UnauthorizedAccessException">A file or the folder cannot be accessed.</exception>
    Task<ScreenshotCopyResult> CopyAsync(IReadOnlyList<Screenshot> screenshots, string targetFolder, CancellationToken cancellationToken = default);
}
