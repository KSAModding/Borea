using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Borea.Core.Listings;
using Tomlyn;
using Tomlyn.Model;

namespace Borea.Storage.Listings;

/// <summary>
/// Authored documents in the layout of the listings of content-index: the keys of RFC 0031 in their order,
/// unknown keys after them in the order read, a table per section, and the description as a multi-line string.
/// </summary>
public sealed partial class TomlListingFormat : IListingFormat
{
    private const string AnyTable = "*";

    private static readonly string[] ImageRecordOrder = ["id", "url", "sha256", "width", "height", "size", "license", "attribution", "source"];

    private static readonly Dictionary<string, string[]> KeyOrder = new(StringComparer.Ordinal)
    {
        [""] =
        [
            "spec_version", "id", "type", "name", "authors", "abstract", "description", "license", "tags", "status", "superseded_by",
            "version", "released_at", "changelog",
            "releases", "links", "compatibility", "loader", "dependencies", "install", "provides", "images", "mods", "vehicles", "saves",
        ],
        ["releases"] = ["github", "spacedock", "authority"],
        ["links"] = ["forums"],
        ["compatibility"] = ["game_min", "game_max", "os"],
        ["loader"] = ["id", "min", "max"],
        ["dependencies"] = ["id", "kind", "min", "max", "any_of"],
        ["dependencies.any_of"] = ["id", "min", "max"],
        ["install"] = ["root", "target", "path", "manages", "steps", "uninstall"],
        ["provides"] = ["launch", "content-dir", "content-path", "configure", "instance", "platform"],
        ["provides.configure"] = ["file", "format", "game-path"],
        ["provides.instance"] = ["flag", "variable"],
        ["provides.platform." + AnyTable] = ["runtime", "launch"],
        ["images"] = ["icon", "description"],
        ["images.icon"] = ImageRecordOrder,
        ["images.description"] = ImageRecordOrder,
        ["mods"] = ["id", "version"],
    };

    /// <summary>Prose lists, one entry per line.</summary>
    private static readonly HashSet<string> ListPerLine = new(StringComparer.Ordinal) { "install.steps", "install.uninstall" };

    public string Write(AuthoredTable document, string? original = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var literal = original is not null && LiteralDescription().IsMatch(original);
        var output = new StringBuilder();
        WriteTable(output, document, [], isArrayElement: false, headerWritten: true, literal);
        return output.ToString();
    }

    public AuthoredTable Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        TomlTable table;
        try
        {
            table = TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
        }
        catch (TomlException exception)
        {
            throw new FormatException($"The document is not valid TOML. {exception.Message}", exception);
        }

        return ToAuthored(table);
    }

    private static AuthoredTable ToAuthored(TomlTable table)
    {
        var authored = new AuthoredTable();
        foreach (var (key, value) in table)
            authored.Set(key, ToAuthored(value));
        return authored;
    }

    private static object ToAuthored(object? value) => value switch
    {
        TomlTable table => ToAuthored(table),
        TomlTableArray tables => tables.Select(item => (object)ToAuthored(item)).ToList(),
        TomlArray array => array.Select(ToAuthored).ToList(),
        string or long or double or bool => value,
        null => throw new FormatException("The document holds a value without a type."),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static void WriteTable(StringBuilder output, AuthoredTable table, IReadOnlyList<string> path, bool isArrayElement, bool headerWritten, bool literal)
    {
        var entries = Ordered(table, path);
        var inline = entries.Where(entry => !IsSection(entry.Value)).ToList();
        var sections = entries.Where(entry => IsSection(entry.Value)).ToList();

        if (!headerWritten && (isArrayElement || inline.Count > 0 || sections.Count == 0))
            WriteHeader(output, path, isArrayElement);

        foreach (var (key, value) in inline)
        {
            output.Append(Key(key)).Append(" = ");
            WriteValue(output, value, [.. path, key], literal);
            output.Append('\n');
        }

        foreach (var (key, value) in sections)
        {
            var childPath = new List<string>(path) { key };
            if (value is AuthoredTable child)
            {
                WriteTable(output, child, childPath, isArrayElement: false, headerWritten: false, literal);
                continue;
            }

            foreach (var element in (IReadOnlyList<object>)value)
                WriteTable(output, (AuthoredTable)element, childPath, isArrayElement: true, headerWritten: false, literal);
        }
    }

    private static void WriteHeader(StringBuilder output, IReadOnlyList<string> path, bool isArrayElement)
    {
        if (output.Length > 0)
            output.Append('\n');

        var name = string.Join('.', path.Select(Key));
        output.Append(isArrayElement ? $"[[{name}]]" : $"[{name}]").Append('\n');
    }

    /// <summary>A table or a non-empty list of tables gets a header of its own, everything else is written inline.</summary>
    private static bool IsSection(object value) =>
        value is AuthoredTable || value is IReadOnlyList<object> { Count: > 0 } list && list.All(item => item is AuthoredTable);

    private static List<KeyValuePair<string, object>> Ordered(AuthoredTable table, IReadOnlyList<string> path)
    {
        var order = OrderFor(path);
        return table.Entries
            .Select((entry, index) => (Entry: entry, Rank: Array.IndexOf(order, entry.Key), Index: index))
            .OrderBy(item => item.Rank < 0 ? int.MaxValue : item.Rank)
            .ThenBy(item => item.Index)
            .Select(item => item.Entry)
            .ToList();
    }

    private static string[] OrderFor(IReadOnlyList<string> path)
    {
        var name = string.Join('.', path);
        if (KeyOrder.TryGetValue(name, out var order))
            return order;

        if (path.Count > 0 && KeyOrder.TryGetValue(string.Join('.', path.Take(path.Count - 1).Append(AnyTable)), out order))
            return order;

        return [];
    }

    private static void WriteValue(StringBuilder output, object value, IReadOnlyList<string> path, bool literal)
    {
        switch (value)
        {
            case string text:
                output.Append(path.Count == 1 && path[0] == "description" || text.Contains('\n') ? MultiLineString(text, literal) : BasicString(text));
                break;
            case long number:
                output.Append(number.ToString(CultureInfo.InvariantCulture));
                break;
            case double number:
                output.Append(Float(number));
                break;
            case bool flag:
                output.Append(flag ? "true" : "false");
                break;
            case AuthoredTable table:
                WriteInlineTable(output, table, path, literal);
                break;
            case IReadOnlyList<object> list when ListPerLine.Contains(string.Join('.', path)) && list.Count > 0:
                output.Append("[\n");
                foreach (var item in list)
                {
                    output.Append("  ");
                    WriteValue(output, item, path, literal);
                    output.Append(",\n");
                }

                output.Append(']');
                break;
            case IReadOnlyList<object> list:
                output.Append('[');
                for (var index = 0; index < list.Count; index++)
                {
                    if (index > 0)
                        output.Append(", ");
                    WriteValue(output, list[index], path, literal);
                }

                output.Append(']');
                break;
            default:
                throw new ArgumentException($"A value of type {value.GetType().Name} cannot be written.", nameof(value));
        }
    }

    private static void WriteInlineTable(StringBuilder output, AuthoredTable table, IReadOnlyList<string> path, bool literal)
    {
        if (table.Count == 0)
        {
            output.Append("{}");
            return;
        }

        output.Append("{ ");
        var first = true;
        foreach (var (key, value) in Ordered(table, path))
        {
            if (!first)
                output.Append(", ");
            first = false;
            output.Append(Key(key)).Append(" = ");
            WriteValue(output, value, [.. path, key], literal);
        }

        output.Append(" }");
    }

    private static string Key(string key) => BareKey().IsMatch(key) ? key : BasicString(key);

    private static string Float(double number) => number switch
    {
        double.PositiveInfinity => "inf",
        double.NegativeInfinity => "-inf",
        double.NaN => "nan",
        _ when Math.Floor(number) == number && Math.Abs(number) < 1e15 => number.ToString("0.0", CultureInfo.InvariantCulture),
        _ => number.ToString("R", CultureInfo.InvariantCulture),
    };

    private static string BasicString(string text)
    {
        var output = new StringBuilder(text.Length + 2).Append('"');
        foreach (var character in text)
            AppendEscaped(output, character, allowNewline: false);
        return output.Append('"').ToString();
    }

    /// <summary>A literal string when one is preferred and the text allows it, and a basic string otherwise.</summary>
    private static string MultiLineString(string text, bool literal)
    {
        if (literal
            && !text.Contains("'''", StringComparison.Ordinal)
            && !text.EndsWith('\'')
            && text.All(character => character is '\t' or '\n' || !char.IsControl(character)))
            return $"'''\n{text}'''";

        var output = new StringBuilder(text.Length + 8).Append("\"\"\"\n");
        var quotes = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                quotes++;
                var atEnd = index == text.Length - 1;
                output.Append(quotes == 3 || atEnd ? "\\\"" : "\"");
                if (quotes == 3)
                    quotes = 0;
                continue;
            }

            quotes = 0;
            AppendEscaped(output, character, allowNewline: true);
        }

        return output.Append("\"\"\"").ToString();
    }

    private static void AppendEscaped(StringBuilder output, char character, bool allowNewline)
    {
        switch (character)
        {
            case '\\':
                output.Append("\\\\");
                break;
            case '"' when !allowNewline:
                output.Append("\\\"");
                break;
            case '\n' when allowNewline:
                output.Append('\n');
                break;
            case '\t':
                output.Append(allowNewline ? "\t" : "\\t");
                break;
            case '\n':
                output.Append("\\n");
                break;
            case '\r':
                output.Append("\\r");
                break;
            case '\b':
                output.Append("\\b");
                break;
            case '\f':
                output.Append("\\f");
                break;
            default:
                if (char.IsControl(character))
                    output.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                else
                    output.Append(character);
                break;
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex BareKey();

    [GeneratedRegex(@"^description[ \t]*=[ \t]*'''", RegexOptions.Multiline)]
    private static partial Regex LiteralDescription();
}
