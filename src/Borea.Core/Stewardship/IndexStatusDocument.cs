using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Borea.Core.Stewardship;

/// <summary>
/// index-status.toml of content-index as text. An edit changes only the lines of one entry, so the comment header and
/// every other entry stay byte for byte. It reads the form that the file and its header describe: comments,
/// <c>entries = []</c> while there is no entry, and <c>[[entries]]</c> tables of string keys. Any other form is refused.
/// </summary>
public sealed partial class IndexStatusDocument
{
    public const string Path = "index-status.toml";

    private const string EmptyEntries = "entries = []";
    private const string Header = "[[entries]]";
    private const char ByteOrderMark = (char)0xFEFF;

    private static readonly string[] Keys = ["id", "state", "version", "since", "reason"];

    private readonly List<string> _lines;
    private readonly List<Span> _spans;
    private readonly int _emptyLine;
    private readonly string _newLine;

    private IndexStatusDocument(List<string> lines, List<Span> spans, IReadOnlyList<IndexStatusEntry> entries, int emptyLine, string newLine)
    {
        _lines = lines;
        _spans = spans;
        _emptyLine = emptyLine;
        _newLine = newLine;
        Entries = entries;
        Text = string.Concat(lines);
    }

    public string Text { get; }

    public IReadOnlyList<IndexStatusEntry> Entries { get; }

    /// <exception cref="FormatException">The text has a form that this class does not edit, or is no valid index-status.toml.</exception>
    public static IndexStatusDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = SplitLines(text);
        var newLine = lines.FirstOrDefault(line => line.EndsWith('\n'))?.EndsWith("\r\n", StringComparison.Ordinal) == true ? "\r\n" : "\n";
        var spans = new List<Span>();
        var entries = new List<IndexStatusEntry>();
        Dictionary<string, string>? values = null;
        var emptyLine = -1;
        var start = -1;
        var end = -1;

        void Close()
        {
            if (values is null)
                return;

            if (!values.TryGetValue("id", out var id) || !values.TryGetValue("state", out var state))
                throw new FormatException($"The entry in line {start + 1} of {Path} has no id or no state.");

            entries.Add(new IndexStatusEntry(id, state, values.GetValueOrDefault("version"), values.GetValueOrDefault("since"), values.GetValueOrDefault("reason")));
            spans.Add(new Span(start, end));
        }

        for (var index = 0; index < lines.Count; index++)
        {
            var content = Content(lines, index);
            if (content.Length == 0 || content[0] == '#')
                continue;

            if (HeaderLine().IsMatch(content))
            {
                Close();
                values = new Dictionary<string, string>(StringComparer.Ordinal);
                start = AttachedComments(lines, index, spans.Count > 0 ? spans[^1].End : -1);
                end = index;
                continue;
            }

            if (EmptyEntriesLine().IsMatch(content))
            {
                if (emptyLine >= 0 || values is not null)
                    throw Unsupported(index);

                emptyLine = index;
                continue;
            }

            var match = KeyLine().Match(content);
            if (!match.Success || values is null || !Keys.Contains(match.Groups["key"].Value))
                throw Unsupported(index);

            var key = match.Groups["key"].Value;
            if (!values.TryAdd(key, ParseString(match.Groups["value"].Value, index)))
                throw new FormatException($"Line {index + 1} of {Path} sets {key} a second time.");

            end = index;
        }

        Close();
        if ((emptyLine >= 0) == (entries.Count > 0))
            throw new FormatException($"{Path} needs either entries = [] or [[entries]] tables.");

        return new IndexStatusDocument(lines, spans, entries, emptyLine, newLine);
    }

    /// <summary>
    /// The document with the change, checked as tools/check_status.py checks the file against the documents of the same commit.
    /// A new entry is written with the id as its document spells it, and a retracted version as its file name spells it.
    /// </summary>
    /// <param name="now">The time the new state starts, written as its since.</param>
    /// <exception cref="IndexStatusRefusedException">The checks of content-index would refuse the file, or a lift names a state the file does not have.</exception>
    public IndexStatusDocument Apply(IndexStatusChange change, IndexContents contents, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(contents);
        if (!IndexStatusChange.IsValidReason(change.Reason))
            throw new IndexStatusRefusedException(IndexStatusRefusal.InvalidReason, change.Id, change.Version);

        var target = Resolve(change, contents);
        if (target.Lift is { } index)
            return Remove(index);

        var since = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        return Add(new IndexStatusEntry(target.Id, change.State, target.Version, since, change.Reason.Trim()));
    }

    /// <summary>
    /// Checks the change with every rule of <see cref="Apply"/> except the reason, which the steward can still be typing.
    /// It changes nothing.
    /// </summary>
    /// <exception cref="IndexStatusRefusedException">The checks of content-index would refuse the file, or a lift names a state the file does not have.</exception>
    public void Check(IndexStatusChange change, IndexContents contents)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(contents);
        _ = Resolve(change, contents);
    }

    /// <summary>The entry that a lift removes, or the id and version of the new entry as the documents spell them.</summary>
    private Target Resolve(IndexStatusChange change, IndexContents contents)
    {
        if (change.Action == IndexStatusAction.Lift)
        {
            var index = Entries.ToList().FindIndex(entry => entry.Names(change.Id, change.State, change.Version));
            return index >= 0 ? new Target(index, change.Id, change.Version) : throw new IndexStatusRefusedException(IndexStatusRefusal.NotInFile, change.Id, change.Version);
        }

        var retracted = change.Action == IndexStatusAction.Retract;
        if (!retracted && change.Version is not null)
            throw new ArgumentException("Only a retracted state names a version.", nameof(change));
        if (retracted && string.IsNullOrWhiteSpace(change.Version))
            throw new IndexStatusRefusedException(IndexStatusRefusal.MissingVersion, change.Id);

        var version = retracted ? contents.PackVersion(change.Id, change.Version!) ?? change.Version : null;
        if (Entries.Any(entry => Scope(entry) is { } scope && scope.Id.Equals(change.Id, StringComparison.OrdinalIgnoreCase) && scope.Version == version))
            throw new IndexStatusRefusedException(IndexStatusRefusal.Duplicate, change.Id, version);

        string id;
        if (retracted)
        {
            id = contents.PackId(change.Id) ?? throw new IndexStatusRefusedException(IndexStatusRefusal.NotAPack, change.Id, version);
            if (!contents.HasPackVersion(id, version!))
                throw new IndexStatusRefusedException(IndexStatusRefusal.UnknownVersion, change.Id, version);
        }
        else
        {
            id = contents.ListingId(change.Id) ?? contents.PackId(change.Id) ?? throw new IndexStatusRefusedException(IndexStatusRefusal.UnknownId, change.Id);
        }

        return new Target(null, id, version);
    }

    /// <param name="Lift">The index of the entry a lift removes, else null.</param>
    private readonly record struct Target(int? Lift, string Id, string? Version);

    /// <summary>What an entry claims, as check_status.py keys it: the id, and the version of a retracted state. Null for a retracted state without a version, which keys nothing.</summary>
    private static (string Id, string? Version)? Scope(IndexStatusEntry entry) =>
        entry.State != IndexStatusEntry.Retracted ? (entry.Id, null)
        : entry.Version is { } version ? (entry.Id, version)
        : null;

    /// <summary>The first entry takes the place of <c>entries = []</c>, and a later one follows the last entry after a blank line.</summary>
    private IndexStatusDocument Add(IndexStatusEntry entry)
    {
        var lines = _lines.ToList();
        var block = Block(entry);
        if (Entries.Count == 0)
        {
            var terminator = Terminator(lines[_emptyLine]);
            lines.RemoveAt(_emptyLine);
            lines.InsertRange(_emptyLine, block.Select((line, index) => line + (index == block.Count - 1 ? terminator : _newLine)));
        }
        else
        {
            var last = _spans[^1].End;
            if (Terminator(lines[last]).Length == 0)
                lines[last] += _newLine;

            lines.InsertRange(last + 1, [_newLine, .. block.Select(line => line + _newLine)]);
        }

        return Parse(string.Concat(lines));
    }

    /// <summary>Removes the lines of one entry and the blank line that parted it from its neighbour. The last entry leaves <c>entries = []</c> in its place.</summary>
    private IndexStatusDocument Remove(int index)
    {
        var lines = _lines.ToList();
        var span = _spans[index];
        if (Entries.Count == 1)
        {
            var terminator = Terminator(lines[span.End]);
            lines.RemoveRange(span.Start, span.End - span.Start + 1);
            lines.Insert(span.Start, EmptyEntries + terminator);
            return Parse(string.Concat(lines));
        }

        var first = span.Start;
        var count = span.End - span.Start + 1;
        if (index > 0 && Content(lines, first - 1).Length == 0)
        {
            first--;
            count++;
        }
        else if (index == 0 && span.End + 1 < lines.Count && Content(lines, span.End + 1).Length == 0)
        {
            count++;
        }

        lines.RemoveRange(first, count);
        return Parse(string.Concat(lines));
    }

    private static List<string> Block(IndexStatusEntry entry)
    {
        var block = new List<string> { Header, $"id = {Quote(entry.Id)}", $"state = {Quote(entry.State)}" };
        if (entry.Version is not null)
            block.Add($"version = {Quote(entry.Version)}");
        if (entry.Since is not null)
            block.Add($"since = {Quote(entry.Since)}");
        if (entry.Reason is not null)
            block.Add($"reason = {Quote(entry.Reason)}");

        return block;
    }

    /// <summary>The comment lines right above a header belong to its entry, but never those above the first entry, which are the header of the file.</summary>
    private static int AttachedComments(List<string> lines, int header, int previousEnd)
    {
        if (previousEnd < 0)
            return header;

        var start = header;
        while (start - 1 > previousEnd && Content(lines, start - 1) is { Length: > 0 } above && above[0] == '#')
            start--;

        return start;
    }

    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n')
                continue;

            lines.Add(text[start..(index + 1)]);
            start = index + 1;
        }

        if (start < text.Length)
            lines.Add(text[start..]);

        return lines;
    }

    private static string Terminator(string line) =>
        line.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : line.EndsWith('\n') ? "\n" : string.Empty;

    /// <summary>The line without its terminator and the spaces around it, and without a byte order mark on the first line.</summary>
    private static string Content(List<string> lines, int index)
    {
        var line = lines[index];
        var content = line[..^Terminator(line).Length];
        if (index == 0 && content.StartsWith(ByteOrderMark))
            content = content[1..];

        return content.Trim(' ', '\t');
    }

    /// <summary>A basic or literal TOML string on one line, followed by nothing but a comment.</summary>
    private static string ParseString(string text, int index)
    {
        if (text.StartsWith("\"\"\"", StringComparison.Ordinal) || text.StartsWith("'''", StringComparison.Ordinal))
            throw Unsupported(index);

        var value = new StringBuilder();
        int end;
        if (text.StartsWith('\''))
        {
            end = text.IndexOf('\'', 1);
            if (end < 0)
                throw Unsupported(index);

            value.Append(text, 1, end - 1);
        }
        else if (text.StartsWith('"'))
        {
            end = ReadBasicString(text, value, index);
        }
        else
        {
            throw Unsupported(index);
        }

        var rest = text[(end + 1)..].TrimStart(' ', '\t');
        if (rest.Length > 0 && rest[0] != '#')
            throw Unsupported(index);

        return value.ToString();
    }

    /// <summary>Appends the characters of the basic string that opens <paramref name="text"/> and returns the position of its closing quote.</summary>
    private static int ReadBasicString(string text, StringBuilder value, int index)
    {
        var end = 1;
        while (true)
        {
            if (end >= text.Length)
                throw Unsupported(index);

            var c = text[end];
            if (c == '"')
                return end;

            if (c == '\\')
            {
                end = Unescape(text, end, value, index);
                continue;
            }

            if (char.IsControl(c) && c != '\t')
                throw Unsupported(index);

            value.Append(c);
            end++;
        }
    }

    /// <summary>Appends the escape at <paramref name="at"/> and returns the position after it.</summary>
    private static int Unescape(string text, int at, StringBuilder value, int index)
    {
        if (at + 1 >= text.Length)
            throw Unsupported(index);

        var code = text[at + 1];
        var simple = code switch
        {
            'b' => "\b",
            't' => "\t",
            'n' => "\n",
            'f' => "\f",
            'r' => "\r",
            '"' => "\"",
            '\\' => "\\",
            _ => null,
        };
        if (simple is not null)
        {
            value.Append(simple);
            return at + 2;
        }

        var digits = code switch
        {
            'u' => 4,
            'U' => 8,
            _ => throw Unsupported(index),
        };
        if (at + 2 + digits > text.Length
            || !int.TryParse(text.AsSpan(at + 2, digits), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var scalar)
            || !Rune.IsValid(scalar))
        {
            throw Unsupported(index);
        }

        value.Append(char.ConvertFromUtf32(scalar));
        return at + 2 + digits;
    }

    /// <summary>A basic string. Every control character is escaped, which TOML requires for all but the tab.</summary>
    private static string Quote(string value)
    {
        var quoted = new StringBuilder("\"");
        foreach (var c in value)
        {
            if (c is '"' or '\\')
                quoted.Append('\\').Append(c);
            else if (char.IsControl(c))
                quoted.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}");
            else
                quoted.Append(c);
        }

        return quoted.Append('"').ToString();
    }

    private static FormatException Unsupported(int index) =>
        new($"Line {index + 1} of {Path} has a form that Borea does not edit.");

    [GeneratedRegex(@"^\[\[[ \t]*entries[ \t]*\]\][ \t]*(#.*)?$")]
    private static partial Regex HeaderLine();

    [GeneratedRegex(@"^entries[ \t]*=[ \t]*\[[ \t]*\][ \t]*(#.*)?$")]
    private static partial Regex EmptyEntriesLine();

    [GeneratedRegex(@"^(?<key>[A-Za-z0-9_-]+)[ \t]*=[ \t]*(?<value>.*)$")]
    private static partial Regex KeyLine();

    /// <summary>The lines of one entry: from its attached comments or its header to its last key.</summary>
    private sealed record Span(int Start, int End);
}
