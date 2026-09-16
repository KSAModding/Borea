using System.Text;
using System.Text.RegularExpressions;
using Borea.Core.Instances;

namespace Borea.Storage.Instances;

/// <param name="Session">Null when the log has no line with a time.</param>
/// <param name="Readable">False when the log has lines and none of them has a time.</param>
internal sealed record GameSessionLog(GameSession? Session, bool Readable);

/// <summary>
/// KSA.KSALogFormatter.MonitorLogFormatter starts a line with the time of day
/// and a level, and KSA.App.Run writes "Shutting down application" when the
/// game closes normally.
/// </summary>
internal static class GameSessionLogReader
{
    private const string ShutdownMessage = "Shutting down application";

    private static readonly TimeSpan NameTolerance = TimeSpan.FromMinutes(1);

    private static readonly Regex TimedLine = new(@"^(\d{2}):(\d{2}):(\d{2})\.(\d{3}) +(?:TRACE|DEBUG|INFO|WARN|ERROR|CRIT) ", RegexOptions.CultureInvariant);

    public static GameSessionLog Read(GameLogFile log)
    {
        TimeSpan? first = null;
        var last = TimeSpan.Zero;
        var hasLines = false;
        var closedCleanly = false;

        // shared, because a running game keeps its log open
        using (var stream = new FileStream(log.File.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0)
                    continue;

                hasLines = true;
                if (TimeOfDay(line) is not { } time)
                    continue;

                first ??= time;
                last = time;
                closedCleanly |= line.Contains(ShutdownMessage, StringComparison.Ordinal);
            }
        }

        if (first is not { } start)
            return new GameSessionLog(null, Readable: !hasLines);

        var length = last - start;
        if (length < TimeSpan.Zero)
            length += TimeSpan.FromDays(1);

        var (startedAt, endedAt) = Date(log, start, last, length);
        return new GameSessionLog(new GameSession(Offset(startedAt), Offset(endedAt), closedCleanly), Readable: true);
    }

    private static TimeSpan? TimeOfDay(string line)
    {
        var match = TimedLine.Match(line);
        if (!match.Success)
            return null;

        var hours = int.Parse(match.Groups[1].ValueSpan);
        var minutes = int.Parse(match.Groups[2].ValueSpan);
        var seconds = int.Parse(match.Groups[3].ValueSpan);
        return hours < 24 && minutes < 60 && seconds < 60
            ? new TimeSpan(0, hours, minutes, seconds, int.Parse(match.Groups[4].ValueSpan))
            : null;
    }

    /// <summary>
    /// Puts the times of day on a date. An archive is named with the day of its
    /// last write, which is the day of its last line. A run is named with the
    /// start of its process, just before its first line. An undated log takes
    /// the day of its last write.
    /// </summary>
    private static (DateTime Start, DateTime End) Date(GameLogFile log, TimeSpan first, TimeSpan last, TimeSpan length)
    {
        if (log.Kind == GameLogKind.Run && log.NameDate is { } processStart)
        {
            var start = processStart.Date + first;
            if (start < processStart - NameTolerance)
                start = start.AddDays(1);
            return (start, start + length);
        }

        if (log.Kind == GameLogKind.Archive && log.NameDate is { } writtenOn)
        {
            var archivedEnd = writtenOn.Date + last;
            return (archivedEnd - length, archivedEnd);
        }

        var writtenAt = DateTime.SpecifyKind(log.File.LastWriteTime, DateTimeKind.Unspecified);
        var end = writtenAt.Date + last;
        if (end > writtenAt + NameTolerance)
            end = end.AddDays(-1);
        return (end - length, end);
    }

    private static DateTimeOffset Offset(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, TimeZoneInfo.Local.GetUtcOffset(unspecified)).ToUniversalTime();
    }
}
