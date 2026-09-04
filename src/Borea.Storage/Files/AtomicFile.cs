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
    public static async Task WriteAllTextAsync(string path, string text, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Written beside the file and moved onto it: the move is atomic in one
        // directory, and the unique name keeps a second writer off this file.
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, text, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            TryDeleteLeftover(tempPath);
        }
    }

    /// <summary>
    /// Clears the temporary file without replacing the error that caused it.
    /// </summary>
    private static void TryDeleteLeftover(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
