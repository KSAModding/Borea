namespace Borea.Storage.Files;

/// <summary>
/// Renames a folder. Windows refuses that rename while another program holds a file below the folder, and a virus
/// scanner or a search indexer reads the files Borea has just written for a moment. So on Windows the rename is tried
/// again for about two seconds before the error goes to the caller.
/// </summary>
internal static class DirectoryRename
{
    internal const int Attempts = 20;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    private const int AccessDenied = unchecked((int)0x80070005);
    private const int SharingViolation = unchecked((int)0x80070020);

    public static void Move(string source, string destination) => Move(source, destination, Thread.Sleep);

    /// <param name="pause">Waits between two attempts, which a test replaces with a gate.</param>
    internal static void Move(string source, string destination, Action<TimeSpan> pause)
    {
        for (var attempt = 1; !TryMove(source, destination, attempt); attempt++)
            pause(RetryDelay);
    }

    /// <returns>False when Windows refused the rename for a moment and another attempt is left.</returns>
    private static bool TryMove(string source, string destination, int attempt)
    {
        try
        {
            Directory.Move(source, destination);
            return true;
        }
        catch (IOException exception) when (OperatingSystem.IsWindows() && exception.HResult is AccessDenied or SharingViolation && attempt < Attempts)
        {
            return false;
        }
    }
}
