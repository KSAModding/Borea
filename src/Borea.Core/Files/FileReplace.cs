namespace Borea.Core.Files;

/// <summary>
/// Moves a finished temporary file onto the file it replaces. Windows refuses that move while another handle reads
/// the old file, even one that shares delete, and the readers in Borea hold a file only for a moment. So the move is
/// tried again for about half a second before the error goes to the caller.
/// </summary>
public static class FileReplace
{
    private const int Attempts = 20;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(25);

    public static async Task MoveOverAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; !TryMove(sourcePath, destinationPath, attempt); attempt++)
            await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
    }

    public static void MoveOver(string sourcePath, string destinationPath)
    {
        for (var attempt = 1; !TryMove(sourcePath, destinationPath, attempt); attempt++)
            Thread.Sleep(RetryDelay);
    }

    /// <returns>False when the move failed and another attempt is left.</returns>
    private static bool TryMove(string sourcePath, string destinationPath, int attempt)
    {
        try
        {
            File.Move(sourcePath, destinationPath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && attempt < Attempts && File.Exists(sourcePath))
        {
            return false;
        }
    }
}
