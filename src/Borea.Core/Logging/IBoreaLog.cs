namespace Borea.Core.Logging;

/// <summary>
/// Borea's own log, one file per day, shared by the App and the CLI. Paths
/// under the user profile are written with "~". Writing never throws, because
/// a log that cannot be written must not stop the operation it describes.
/// </summary>
public interface IBoreaLog
{
    /// <summary>Today's file. It does not exist before the first line of the day.</summary>
    string CurrentFilePath { get; }

    void Write(string message);

    /// <summary>Writes the message and the exception with its stack trace.</summary>
    void Write(string message, Exception exception);

    /// <summary>The last lines of today's file, oldest first. Empty when the file is missing or cannot be read.</summary>
    IReadOnlyList<string> ReadRecentLines(int maxLines);
}
