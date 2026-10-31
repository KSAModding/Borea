using Borea.Core.Files;

namespace Borea.Storage.Files;

/// <summary>
/// Writes a whole text file so a reader sees the old content or the new one,
/// never a truncated file.
/// </summary>
internal static class AtomicFile
{
    /// <summary>
    /// Writes UTF-8 without a byte order mark, creating the directory if
    /// needed. Replaces an existing file, and leaves it untouched when the
    /// write does not finish.
    /// </summary>
    /// <remarks>Not safe against a second writer of the same path.</remarks>
    public static Task WriteAllTextAsync(string path, string text, CancellationToken cancellationToken = default) =>
        ReplaceThroughTempFileAsync(path, tempPath => File.WriteAllTextAsync(tempPath, text, cancellationToken), cancellationToken);

    public static Task WriteAllBytesAsync(string path, byte[] content, CancellationToken cancellationToken = default) =>
        ReplaceThroughTempFileAsync(path, tempPath => File.WriteAllBytesAsync(tempPath, content, cancellationToken), cancellationToken);

    public static void WriteAllBytes(string path, byte[] content)
    {
        var tempPath = TempFileBeside(path);
        try
        {
            File.WriteAllBytes(tempPath, content);
            FileReplace.MoveOver(tempPath, path);
        }
        finally
        {
            LeftoverCleanup.TryDeleteFile(tempPath);
        }
    }

    private static async Task ReplaceThroughTempFileAsync(string path, Func<string, Task> writeTempFile, CancellationToken cancellationToken)
    {
        var tempPath = TempFileBeside(path);
        try
        {
            await writeTempFile(tempPath).ConfigureAwait(false);
            await FileReplace.MoveOverAsync(tempPath, path, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            LeftoverCleanup.TryDeleteFile(tempPath);
        }
    }

    private static string TempFileBeside(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Written beside the file and moved onto it: the move is atomic in one
        // directory, and the unique name keeps a second writer off this file.
        return $"{path}.{Guid.NewGuid():N}.tmp";
    }
}
