using System.Text.RegularExpressions;

namespace Borea.Core.Launch;

/// <summary>
/// Reads the log that Brutal.Monitor keeps of a run that crashed. Monitor.OnLastChance
/// writes the unhandled exception as "Unhandled exception " and its ToString, so the
/// lines from there on are the exception, its inner exceptions and their stacks.
/// </summary>
public static partial class GameCrashReport
{
    private const string UnhandledException = "Unhandled exception ";

    private const string InnerException = "---> ";

    // A frame in the runtime, the game or its loader names no mod, and a mod
    // folder that holds a copy of one of their assemblies must not be named
    // for every crash.
    private static readonly HashSet<string> HostNamespaces = new(StringComparer.OrdinalIgnoreCase) { "System", "Microsoft", "KSA", "Brutal", "StarMap" };

    /// <summary>
    /// The type and message of the innermost exception, such as
    /// "System.NullReferenceException: Object reference not set to an instance of an object.",
    /// or null when the log holds no unhandled exception. The innermost one is
    /// the cause, because StarMap starts the game through reflection, which
    /// wraps every exception of the game in a TargetInvocationException.
    /// </summary>
    public static string? ExceptionText(IReadOnlyList<string> log)
    {
        ArgumentNullException.ThrowIfNull(log);

        var lines = ExceptionLines(log);
        if (lines.Count == 0)
            return null;

        var innermost = lines[0];
        foreach (var line in lines.Skip(1))
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith(InnerException, StringComparison.Ordinal))
                break;
            innermost = trimmed[InnerException.Length..];
        }

        return string.IsNullOrWhiteSpace(innermost) ? null : innermost.Trim();
    }

    /// <summary>
    /// The assembly names the unhandled exception names, the way
    /// <see cref="LoaderCrashReport.AssemblyNames"/> reads them, and then the
    /// first namespace of each of its stack frames outside the runtime, the
    /// game and its loader, innermost frame first.
    /// </summary>
    public static IReadOnlyList<string> AssemblyNames(IReadOnlyList<string> log)
    {
        ArgumentNullException.ThrowIfNull(log);

        var lines = ExceptionLines(log);
        var names = LoaderCrashReport.AssemblyNames(lines).ToList();
        foreach (var line in lines)
        {
            var match = FramePattern().Match(line);
            if (!match.Success)
                continue;

            var root = match.Groups["root"].Value;
            if (!HostNamespaces.Contains(root) && !names.Contains(root, StringComparer.OrdinalIgnoreCase))
                names.Add(root);
        }

        return names;
    }

    /// <summary>The lines of the last unhandled exception, with the text before it on its first line cut off.</summary>
    private static IReadOnlyList<string> ExceptionLines(IReadOnlyList<string> log)
    {
        for (var index = log.Count - 1; index >= 0; index--)
        {
            var at = log[index]?.IndexOf(UnhandledException, StringComparison.Ordinal) ?? -1;
            if (at >= 0)
                return [log[index][(at + UnhandledException.Length)..], .. log.Skip(index + 1).Select(line => line ?? string.Empty)];
        }

        return [];
    }

    [GeneratedRegex(@"^\s*at\s+(?<root>[A-Za-z_][A-Za-z0-9_]*)\.")]
    private static partial Regex FramePattern();
}
