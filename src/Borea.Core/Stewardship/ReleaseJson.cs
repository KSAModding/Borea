using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Borea.Core.Stewardship;

/// <summary>
/// A release file of content-index-releases as JSON, written byte for byte as the stamper writes it:
/// <c>json.dumps(document, indent=2, ensure_ascii=False)</c> and a final newline. Every encoder of System.Text.Json escapes
/// characters that Python writes as they are, such as <c>&lt;&gt;&amp;'+</c> and every character outside the BMP, so this class writes the text itself.
/// </summary>
internal static class ReleaseJson
{
    private static readonly JsonDocumentOptions Options = new() { AllowDuplicateProperties = false };

    /// <exception cref="FormatException">The text is no JSON, or an object has a key twice.</exception>
    public static JsonNode? Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text, documentOptions: Options);
        }
        catch (JsonException exception)
        {
            throw new FormatException(exception.Message, exception);
        }
    }

    public static string Write(JsonNode? node)
    {
        var text = new StringBuilder();
        Write(text, node, 0);
        return text.Append('\n').ToString();
    }

    public static string? StringOf(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    /// <summary>The value of a JSON integer, which Python reads as an int. A number with a fraction or an exponent is a float there, so it gives null.</summary>
    public static long? IntegerOf(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.Number
            && value.ToJsonString() is var token && token.IndexOfAny(['.', 'e', 'E']) < 0
            && long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    public static bool IsTrue(JsonNode? node) => node?.GetValueKind() == JsonValueKind.True;

    /// <summary>The value as Python's str() shows it in a message.</summary>
    public static string Show(JsonNode? node) => node is null ? "None" : StringOf(node) ?? node.ToJsonString();

    private static void Write(StringBuilder text, JsonNode? node, int depth)
    {
        switch (node)
        {
            case null:
                text.Append("null");
                break;
            case JsonObject members:
                WriteItems(text, '{', '}', members.Select(member => ((string?)member.Key, member.Value)), depth);
                break;
            case JsonArray items:
                WriteItems(text, '[', ']', items.Select(item => ((string?)null, item)), depth);
                break;
            default:
                switch (node.GetValueKind())
                {
                    case JsonValueKind.String:
                        WriteString(text, node.GetValue<string>());
                        break;
                    case JsonValueKind.Number:
                        text.Append(NumberOf(node.ToJsonString()));
                        break;
                    default:
                        text.Append(node.ToJsonString());
                        break;
                }

                break;
        }
    }

    private static void WriteItems(StringBuilder text, char open, char close, IEnumerable<(string? Key, JsonNode? Value)> items, int depth)
    {
        text.Append(open);
        var empty = true;
        foreach (var (key, value) in items)
        {
            text.Append(empty ? "\n" : ",\n").Append(' ', 2 * (depth + 1));
            empty = false;
            if (key is not null)
            {
                WriteString(text, key);
                text.Append(": ");
            }

            Write(text, value, depth + 1);
        }

        if (!empty)
            text.Append('\n').Append(' ', 2 * depth);
        text.Append(close);
    }

    private static void WriteString(StringBuilder text, string value)
    {
        text.Append('"');
        foreach (var character in value)
        {
            _ = character switch
            {
                '"' => text.Append("\\\""),
                '\\' => text.Append("\\\\"),
                '\n' => text.Append("\\n"),
                '\r' => text.Append("\\r"),
                '\t' => text.Append("\\t"),
                '\b' => text.Append("\\b"),
                '\f' => text.Append("\\f"),
                < ' ' => text.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture)),
                _ => text.Append(character),
            };
        }

        text.Append('"');
    }

    /// <summary>A number as Python writes what json.loads read from <paramref name="token"/>: an integer keeps its digits, and anything else is a float in the form of repr().</summary>
    private static string NumberOf(string token)
    {
        if (token.IndexOfAny(['.', 'e', 'E']) < 0)
            return token == "-0" ? "0" : token;

        var value = double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (double.IsInfinity(value))
            return value > 0 ? "Infinity" : "-Infinity";
        if (value == 0)
            return double.IsNegative(value) ? "-0.0" : "0.0";

        var (digits, point) = ShortestDigits(value);

        var sign = value < 0 ? "-" : string.Empty;
        if (point is > -4 and <= 16)
        {
            return sign + (point <= 0 ? "0." + new string('0', -point) + digits
                : point >= digits.Length ? digits + new string('0', point - digits.Length) + ".0"
                : digits[..point] + "." + digits[point..]);
        }

        var power = point - 1;
        return sign + digits[..1] + (digits.Length > 1 ? "." + digits[1..] : string.Empty)
            + (power < 0 ? "e-" : "e+") + Math.Abs(power).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>The shortest digits that read back as the same double, as repr() uses them, with the decimal point after <c>Point</c> digits.</summary>
    private static (string Digits, int Point) ShortestDigits(double value)
    {
        var shortest = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        var exponentAt = shortest.IndexOf('E', StringComparison.Ordinal);
        var mantissa = exponentAt < 0 ? shortest : shortest[..exponentAt];
        var exponent = exponentAt < 0 ? 0 : int.Parse(shortest[(exponentAt + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var dot = mantissa.IndexOf('.', StringComparison.Ordinal);
        var whole = dot < 0 ? mantissa : mantissa[..dot];
        var all = whole + (dot < 0 ? string.Empty : mantissa[(dot + 1)..]);
        var digits = all.TrimStart('0');
        var point = whole.Length - (all.Length - digits.Length) + exponent;
        digits = digits.TrimEnd('0');
        return (digits, point);
    }
}
