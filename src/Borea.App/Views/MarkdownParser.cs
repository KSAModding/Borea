using System;
using System.Collections.Generic;
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
}

public sealed record MarkdownSpan(MarkdownSpanKind Kind, string Text);

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
        var paragraph = new List<string>();
        var index = 0;

        while (index < lines.Length)
        {
            var line = lines[index];

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
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Heading, heading.Groups[2].Value, heading.Groups[1].Value.Length));
                index++;
                continue;
            }

            var item = ListItemPattern().Match(line);
            if (item.Success)
            {
                Flush();
                var marker = char.IsDigit(item.Groups[1].Value[0]) ? item.Groups[1].Value : "•";
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.ListItem, item.Groups[2].Value, Marker: marker));
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

            blocks.Add(new MarkdownBlock(MarkdownBlockKind.Paragraph, string.Join(" ", paragraph)));
            paragraph.Clear();
        }
    }

    /// <summary>
    /// Splits one block of text into spans. A link keeps only its label.
    /// </summary>
    public static IReadOnlyList<MarkdownSpan> ParseInline(string text)
    {
        var spans = new List<MarkdownSpan>();
        var position = 0;
        foreach (Match match in InlinePattern().Matches(text))
        {
            if (match.Index > position)
                spans.Add(new MarkdownSpan(MarkdownSpanKind.Text, text[position..match.Index]));

            if (match.Groups["bold"].Success)
                spans.Add(new MarkdownSpan(MarkdownSpanKind.Bold, match.Groups["bold"].Value));
            else if (match.Groups["italic"].Success)
                spans.Add(new MarkdownSpan(MarkdownSpanKind.Italic, match.Groups["italic"].Value));
            else if (match.Groups["code"].Success)
                spans.Add(new MarkdownSpan(MarkdownSpanKind.Code, match.Groups["code"].Value));
            else if (match.Groups["link"].Success)
                spans.Add(new MarkdownSpan(MarkdownSpanKind.Link, match.Groups["link"].Value));

            position = match.Index + match.Length;
        }

        if (position < text.Length)
            spans.Add(new MarkdownSpan(MarkdownSpanKind.Text, text[position..]));

        return spans;
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^\s*([-*+]|\d+\.)\s+(.*)$")]
    private static partial Regex ListItemPattern();

    [GeneratedRegex(@"\*\*(?<bold>[^*]+)\*\*|__(?<bold>[^_]+)__|\*(?<italic>[^*]+)\*|_(?<italic>[^_]+)_|`(?<code>[^`]+)`|\[(?<link>[^\]]+)\]\([^)]+\)")]
    private static partial Regex InlinePattern();
}
