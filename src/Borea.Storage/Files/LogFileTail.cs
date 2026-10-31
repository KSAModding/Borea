using System.Text;

namespace Borea.Storage.Files;

/// <summary>
/// Reads the last lines of a log file that another program can still write to. Only the last
/// maxBytes bytes are read, so the first line of that window can be cut, and it is dropped.
/// Nothing is caught here, because each caller treats a missing or unreadable file in its own way.
/// </summary>
internal static class LogFileTail
{
    public static IReadOnlyList<string> ReadLastLines(string path, int maxBytes, int maxLines)
    {
        // shared, because the program that writes the log keeps it open
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var start = Math.Max(0, stream.Length - maxBytes);
        stream.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: start == 0);

        var lines = reader.ReadToEnd().Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        if (start > 0)
            lines.RemoveAt(0);
        if (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        return lines.TakeLast(maxLines).ToList();
    }
}
