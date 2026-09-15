using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

namespace Borea.App.Views;

public enum MarkdownBlockKind
{
    Paragraph,
    Heading,
    ListItem,
    Code,
}

/// <summary>
/// One block of a description. <see cref="Level"/> is the heading level;
/// <see cref="Marker"/> is "•" for a bullet or the number of a numbered item.
/// </summary>
public sealed record MarkdownBlock(MarkdownBlockKind Kind, string Text, int Level = 0, string? Marker = null);

public enum MarkdownSpanKind
{
    Text,
    Bold,
    Italic,
    Code,
    Link,
    Image,
}

/// <summary>For an image, <see cref="Text"/> is the alternative text and <see cref="Destination"/> is the destination as written.</summary>
public sealed record MarkdownSpan(MarkdownSpanKind Kind, string Text, string? Destination = null);

/// <summary>
/// Splits the CommonMark subset that listing descriptions use into blocks and
/// inline spans. <see cref="MarkdownView"/> turns them into controls.
/// </summary>
public static partial class MarkdownParser
{
    public static IReadOnlyList<MarkdownBlock> Parse(string? markdown)
    {
        var blocks = new List<MarkdownBlock>();
        if (string.IsNullOrWhiteSpace(markdown))
            return blocks;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var definitionLines = new HashSet<int>();
        var definitions = ReadDefinitions(lines, definitionLines);
        var paragraph = new List<string>();
        var index = 0;

        while (index < lines.Length)
        {
            var line = lines[index];

            if (definitionLines.Contains(index))
            {
                index++;
                continue;
            }

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                Flush();
                var code = new List<string>();
                index++;
                while (index < lines.Length && !lines[index].StartsWith("```", StringComparison.Ordinal))
                    code.Add(lines[index++]);
                index++;
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Code, string.Join("\n", code)));
                continue;
            }

            var heading = HeadingPattern().Match(line);
            if (heading.Success)
            {
                Flush();
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Heading, Resolve(heading.Groups[2].Value, definitions), heading.Groups[1].Value.Length));
                index++;
                continue;
            }

            var item = ListItemPattern().Match(line);
            if (item.Success)
            {
                Flush();
                var marker = char.IsDigit(item.Groups[1].Value[0]) ? item.Groups[1].Value : "•";
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.ListItem, Resolve(item.Groups[2].Value, definitions), Marker: marker));
                index++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
                Flush();
            else
                paragraph.Add(line.Trim());
            index++;
        }

        Flush();
        return blocks;

        void Flush()
        {
            if (paragraph.Count == 0)
                return;

            blocks.Add(new MarkdownBlock(MarkdownBlockKind.Paragraph, Resolve(string.Join(" ", paragraph), definitions)));
            paragraph.Clear();
        }
    }

    /// <summary>
    /// Splits one block of text into spans. A link keeps only its label, and an
    /// image in raw HTML becomes its alternative text.
    /// </summary>
    public static IReadOnlyList<MarkdownSpan> ParseInline(string text)
    {
        var spans = new List<MarkdownSpan>();
        var position = 0;
        foreach (Match match in InlinePattern().Matches(text))
        {
            if (match.Index > position)
                spans.Add(new MarkdownSpan(MarkdownSpanKind.Text, text[position..match.Index]));

            if (match.Groups["source"].Success)
                spans.Add(new MarkdownSpan(MarkdownSpanKind.Image, match.Groups["alt"].Value, Unbracket(match.Groups["source"].Value)));
            else if (match.Groups["html"].Success)
                AddHtmlImage(spans, match.Groups["html"].Value);
            else if (match.Groups["bold"].Success)
                AddEnclosing(spans, MarkdownSpanKind.Bold, match.Groups["bold"].Value);
            else if (match.Groups["italic"].Success)
                AddEnclosing(spans, MarkdownSpanKind.Italic, match.Groups["italic"].Value);
            else if (match.Groups["code"].Success)
                spans.Add(new MarkdownSpan(MarkdownSpanKind.Code, match.Groups["code"].Value));
            else if (match.Groups["link"].Success)
                AddEnclosing(spans, MarkdownSpanKind.Link, match.Groups["link"].Value);

            position = match.Index + match.Length;
        }

        if (position < text.Length)
            spans.Add(new MarkdownSpan(MarkdownSpanKind.Text, text[position..]));

        return spans;
    }

    /// <summary>Emphasis or a link label that holds an image keeps its kind for its text, and each image inside becomes a span of its own.</summary>
    private static void AddEnclosing(List<MarkdownSpan> spans, MarkdownSpanKind kind, string inner)
    {
        if (!inner.Contains("![", StringComparison.Ordinal) && !inner.Contains("<im", StringComparison.OrdinalIgnoreCase))
        {
            spans.Add(new MarkdownSpan(kind, inner));
            return;
        }

        foreach (var span in ParseInline(inner))
            spans.Add(span.Kind == MarkdownSpanKind.Image ? span : span with { Kind = kind });
    }

    private static void AddHtmlImage(List<MarkdownSpan> spans, string attributes)
    {
        var alternativeText = WebUtility.HtmlDecode(HtmlAltPattern().Match(attributes).Groups["alt"].Value);
        if (alternativeText.Length > 0)
            spans.Add(new MarkdownSpan(MarkdownSpanKind.Text, alternativeText));
    }

    /// <summary>CommonMark accepts a link reference definition only where no paragraph is open.</summary>
    private static Dictionary<string, string> ReadDefinitions(string[] lines, HashSet<int> definitionLines)
    {
        var definitions = new Dictionary<string, string>(StringComparer.Ordinal);
        var inParagraph = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                index++;
                while (index < lines.Length && !lines[index].StartsWith("```", StringComparison.Ordinal))
                    index++;
                inParagraph = false;
            }
            else if (!inParagraph && DefinitionPattern().Match(line) is { Success: true } definition)
            {
                definitions.TryAdd(NormalizeLabel(definition.Groups["label"].Value), Unbracket(definition.Groups["destination"].Value));
                definitionLines.Add(index);
            }
            else
            {
                inParagraph = !string.IsNullOrWhiteSpace(line) && !HeadingPattern().IsMatch(line) && !ListItemPattern().IsMatch(line);
            }
        }

        return definitions;
    }

    /// <summary>Writes reference-style links and images in their inline form, so the inline pattern reads one form.</summary>
    private static string Resolve(string text, IReadOnlyDictionary<string, string> definitions)
    {
        if (definitions.Count == 0)
            return text;

        // images first, so a link around an image reference sees the image in its inline form
        var withImages = ReferencePattern().Replace(text, match => ResolveReference(match, definitions, image: true));
        return ReferencePattern().Replace(withImages, match => ResolveReference(match, definitions, image: false));
    }

    private static string ResolveReference(Match match, IReadOnlyDictionary<string, string> definitions, bool image)
    {
        if (!match.Groups["text"].Success || match.Groups["bang"].Length > 0 != image)
            return match.Value;

        var text = match.Groups["text"].Value;
        var label = match.Groups["label"] is { Success: true, Length: > 0 } full ? full.Value : null;
        if (label is null && text.Contains("![", StringComparison.Ordinal))
            return match.Value;

        return definitions.TryGetValue(NormalizeLabel(label ?? text), out var destination)
            ? $"{match.Groups["bang"].Value}[{text}](<{destination}>)"
            : match.Value;
    }

    private static string NormalizeLabel(string label)
        => string.Join(' ', label.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static string Unbracket(string destination)
        => destination is ['<', .., '>'] ? destination[1..^1] : destination;

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^\s*([-*+]|\d+\.)\s+(.*)$")]
    private static partial Regex ListItemPattern();

    [GeneratedRegex("""^ {0,3}\[(?<label>[^\]]*[^\]\s][^\]]*)\]:[ \t]*(?<destination><[^>]*>|\S+)(?:[ \t]+(?:"[^"]*"|'[^']*'|\([^)]*\)))?[ \t]*$""")]
    private static partial Regex DefinitionPattern();

    // an inline image as a part of a longer span, without the capture groups of a top-level image
    private const string NestedImage = """!\[[^\]]*\]\(\s*(?:<[^>]*>|[^\s()]*)(?:\s+(?:"[^"]*"|'[^']*'|\([^)]*\)))?\s*\)""";

    [GeneratedRegex($$"""(?<code>`[^`]+`)|(?<bang>!?)\[(?<text>(?>(?:{{NestedImage}}|[^\[\]])*))\](?:\[(?<label>[^\[\]]*)\])?(?![(\[])""")]
    private static partial Regex ReferencePattern();

    [GeneratedRegex($$"""
        !\[(?<alt>[^\]]*)\]\(\s*(?<source><[^>]*>|[^\s()]*)(?:\s+(?:"[^"]*"|'[^']*'|\([^)]*\)))?\s*\)
        | (?i:<(?:img|image)\b)(?<html>[^>]*)>
        | \*\*(?<bold>(?>(?:{{NestedImage}}|[^*])+))\*\*
        | (?<![\p{L}\p{N}_])__(?<bold>(?>(?:{{NestedImage}}|[^_])+))__(?![\p{L}\p{N}_])
        | \*(?<italic>(?>(?:{{NestedImage}}|[^*])+))\*
        | (?<![\p{L}\p{N}_])_(?<italic>(?>(?:{{NestedImage}}|[^_])+))_(?![\p{L}\p{N}_])
        | `(?<code>[^`]+)`
        | \[(?<link>(?>(?:{{NestedImage}}|[^\]])+))\]\([^)]+\)
        """, RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex InlinePattern();

    [GeneratedRegex("""\balt\s*=\s*(?:"(?<alt>[^"]*)"|'(?<alt>[^']*)'|(?<alt>[^\s"'>]+))""", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlAltPattern();
}
