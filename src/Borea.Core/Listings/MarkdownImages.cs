using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Borea.Core.Listings;

/// <summary>
/// Finds the images of a CommonMark description, as the image reference check of content-index does:
/// inline and reference images outside code, and raw HTML images.
/// </summary>
public static partial class MarkdownImages
{
    public const string Scheme = "ksa-image:";

    private const char EscapedBase = '\uE000';

    /// <summary>The destination of every Markdown image, and the number of raw HTML images.</summary>
    public static (IReadOnlyList<string> Destinations, int HtmlImages) Scan(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var text = WithoutCode(markdown.Replace("\r\n", "\n", StringComparison.Ordinal));
        var definitions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match definition in Definition().Matches(text))
            definitions.TryAdd(Label(definition.Groups[1].Value), Destination(definition.Groups[2].Value));

        text = Definition().Replace(text, string.Empty);
        var destinations = new List<string>();
        foreach (Match image in Image().Matches(text))
        {
            if (image.Groups["inline"].Success)
            {
                destinations.Add(Destination(image.Groups["inline"].Value));
                continue;
            }

            var label = image.Groups["reference"].Success && image.Groups["reference"].Value.Length > 0
                ? image.Groups["reference"].Value
                : image.Groups["alt"].Value;
            if (definitions.TryGetValue(Label(label), out var destination))
                destinations.Add(destination);
        }

        return (destinations, HtmlImage().Matches(text).Count);
    }

    /// <summary>The ids of the ksa-image references, in the order they appear.</summary>
    public static IReadOnlyList<string> References(string markdown) =>
        Scan(markdown).Destinations
            .Where(destination => destination.StartsWith(Scheme, StringComparison.Ordinal))
            .Select(destination => destination[Scheme.Length..])
            .ToList();

    private static string Label(string label) => Whitespace().Replace(label.Trim(), " ").ToUpperInvariant();

    private static string Destination(string destination)
    {
        var bare = destination.Length >= 2 && destination[0] == '<' && destination[^1] == '>' ? destination[1..^1] : destination;
        var decoded = WebUtility.HtmlDecode(bare);
        return string.Create(decoded.Length, decoded, static (span, text) =>
        {
            for (var index = 0; index < text.Length; index++)
                span[index] = text[index] is >= EscapedBase and < (char)(EscapedBase + 128) ? (char)(text[index] - EscapedBase) : text[index];
        });
    }

    /// <summary>
    /// The text with fenced code blocks and code spans blanked out. An escaped character becomes a private-use
    /// character, so it has no Markdown meaning but a destination still gets it back.
    /// </summary>
    private static string WithoutCode(string markdown)
    {
        var builder = new StringBuilder(markdown.Length);
        string? fence = null;
        foreach (var line in markdown.Split('\n'))
        {
            var opening = Fence().Match(line);
            if (fence is null && opening.Success)
            {
                fence = opening.Groups[1].Value;
                builder.Append('\n');
                continue;
            }

            if (fence is not null)
            {
                var trimmed = line.Trim();
                if (trimmed.Length >= fence.Length && trimmed.All(character => character == fence[0]))
                    fence = null;
                builder.Append('\n');
                continue;
            }

            builder.Append(line).Append('\n');
        }

        var text = Escaped().Replace(builder.ToString(), match => ((char)(EscapedBase + match.Value[1])).ToString());
        return string.Concat(BlankLine().Split(text).Select(block => CodeSpan().Replace(block, match => new string(' ', match.Length))));
    }

    [GeneratedRegex(@"^ {0,3}(`{3,}|~{3,})")]
    private static partial Regex Fence();

    [GeneratedRegex(@"\\[!-/:-@\[-`{-~]")]
    private static partial Regex Escaped();

    [GeneratedRegex(@"(?<!`)(`+)(?!`)[\s\S]*?(?<!`)\1(?!`)")]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"(\n[ \t]*\n)")]
    private static partial Regex BlankLine();

    [GeneratedRegex(@"^ {0,3}\[([^\]\n]+)\]:[ \t]*\n?[ \t]*(<[^>\n]*>|\S+)[^\n]*$", RegexOptions.Multiline)]
    private static partial Regex Definition();

    [GeneratedRegex(@"!\[(?<alt>(?:[^\[\]\n]|(?<open>\[)|(?<-open>\]))*(?(open)(?!)))\](?:\(\s*(?<inline><[^>\n]*>|[^\s)]*)(?:\s+(?:""[^""]*""|'[^']*'|\([^)]*\)))?\s*\)|\[(?<reference>[^\]\n]*)\])?")]
    private static partial Regex Image();

    [GeneratedRegex(@"<(?:img|image|picture|svg)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlImage();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
