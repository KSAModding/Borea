using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Borea.App.ViewModels;

public enum GameLogLevel
{
    None,
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical,
}

/// <summary>
/// One line of the game log, split into the parts KSALogFormatter.MonitorLogFormatter
/// writes: the time, the level padded to five characters and the message, with a
/// leading mod tag such as "[AFC]" taken out of the message.
/// </summary>
public sealed partial record GameLogLine(string Time, string Level, string Tag, string Message, GameLogLevel Severity)
{
    public bool IsMuted => Severity is GameLogLevel.Trace or GameLogLevel.Debug;

    public bool IsInformation => Severity == GameLogLevel.Information;

    public bool IsWarning => Severity == GameLogLevel.Warning;

    public bool IsError => Severity is GameLogLevel.Error or GameLogLevel.Critical;

    /// <summary>
    /// A line without the time and level, such as a stack trace, continues the entry
    /// above it and keeps its severity.
    /// </summary>
    public static IReadOnlyList<GameLogLine> Parse(IEnumerable<string> lines)
    {
        var result = new List<GameLogLine>();
        var severity = GameLogLevel.None;
        foreach (var line in lines)
        {
            var entry = EntryPattern().Match(line);
            if (!entry.Success)
            {
                result.Add(new GameLogLine("", "", "", line, severity));
                continue;
            }

            var level = entry.Groups["level"].Value;
            severity = level.Trim() switch
            {
                "TRACE" => GameLogLevel.Trace,
                "DEBUG" => GameLogLevel.Debug,
                "INFO" => GameLogLevel.Information,
                "WARN" => GameLogLevel.Warning,
                "ERROR" => GameLogLevel.Error,
                _ => GameLogLevel.Critical,
            };
            var message = entry.Groups["message"].Value;
            var tag = TagPattern().Match(message);
            result.Add(new GameLogLine(
                entry.Groups["time"].Value + " ",
                level + " ",
                tag.Success ? tag.Value : "",
                message[tag.Length..],
                severity));
        }

        return result;
    }

    [GeneratedRegex(@"^(?<time>\d{2}:\d{2}:\d{2}\.\d{3}) (?<level>TRACE|DEBUG| INFO| WARN|ERROR| CRIT) (?<message>.*)$")]
    private static partial Regex EntryPattern();

    [GeneratedRegex(@"^\[[^\]\s]{1,32}\] ")]
    private static partial Regex TagPattern();
}
