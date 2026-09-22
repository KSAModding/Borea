namespace Borea.Storage.Updates;

/// <summary>
/// The claim one update holds on the folder Borea runs in while it changes the files there. The App
/// and the command line can both replace the same build, and two updates that run into each other
/// would take each other's program file away, which can leave the folder with no program at all.
/// </summary>
internal static class SelfUpdateLock
{
    /// <summary>The file that carries the claim. It is gone again when the update that made it lets go.</summary>
    public const string FileName = ".borea-update-lock";

    /// <summary>
    /// Takes the folder for one update, or returns null when another update holds it. The caller
    /// closes the stream to let go, and a process that ends lets go with it.
    /// </summary>
    public static FileStream? TryTake(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        try
        {
            return new FileStream(
                Path.Combine(folder, FileName),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }
}
