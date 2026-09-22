using System.Globalization;
using System.Text.RegularExpressions;

namespace Borea.Storage.Instances;

internal enum GameLogKind
{
    /// <summary>KittenSpaceAgency.log, which carries no date in its name.</summary>
    Undated,

    /// <summary>A log named with the local time its process started.</summary>
    Run,

    /// <summary>A finished log in the archives, named with the local date of its last write.</summary>
    Archive,
}

/// <param name="NameDate">The start time of a run or the date of an archive, and null for an undated log.</param>
internal sealed record GameLogFile(FileInfo File, GameLogKind Kind, DateTime? NameDate);

/// <summary>
/// The session logs the game leaves next to its log path. KSA.Program hands the
/// folder to Brutal.Monitor, whose BackgroundLogWriter names a run
/// <c>&lt;name&gt;.&lt;yyMMdd-HHmmss&gt;.&lt;process id&gt;.log</c> and whose LogArchiver
/// moves a finished log to <c>Archives/&lt;name&gt;.&lt;yyMMdd&gt;.&lt;n&gt;.log</c>.
/// Recovered crash tails carry a third suffix and are not sessions.
/// </summary>
internal static class GameLogFiles
{
    private const string ArchivesFolder = "Archives";

    private static readonly Regex RunName = new(@"^[^.]+\.(\d{6}-\d{6})\.\d+\.log$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex ArchiveName = new(@"^[^.]+\.(\d{6})\.\d+\.log$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex TailName = new(@"^[^.]+\.\d{6}-\d{6}\.\d+\.[^.]+\.log$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// Whether the name is one the game writes that <see cref="Find"/> leaves
    /// out on purpose, such as the tail of a recovered crash. A caller that
    /// counts the logs of the game uses this to tell a name it knows and skips
    /// from a name it cannot place.
    /// </summary>
    public static bool IsKnownNonSession(string fileName) => TailName.IsMatch(fileName);

    public static IReadOnlyList<GameLogFile> Find(string gameLogPath)
    {
        var folder = Path.GetDirectoryName(gameLogPath)!;
        var undatedName = Path.GetFileName(gameLogPath);
        var logs = new List<GameLogFile>();

        foreach (var file in LogsIn(folder))
        {
            if (string.Equals(file.Name, undatedName, StringComparison.OrdinalIgnoreCase))
                logs.Add(new GameLogFile(file, GameLogKind.Undated, null));
            else if (NameDate(RunName, file.Name, "yyMMdd-HHmmss") is { } startedAt)
                logs.Add(new GameLogFile(file, GameLogKind.Run, startedAt));
        }

        foreach (var file in LogsIn(Path.Combine(folder, ArchivesFolder)))
        {
            if (NameDate(ArchiveName, file.Name, "yyMMdd") is { } writtenOn)
                logs.Add(new GameLogFile(file, GameLogKind.Archive, writtenOn));
        }

        return logs;
    }

    private static DateTime? NameDate(Regex pattern, string fileName, string format)
    {
        var match = pattern.Match(fileName);
        return match.Success && DateTime.TryParseExact(match.Groups[1].Value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static FileInfo[] LogsIn(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).GetFiles("*.log");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
