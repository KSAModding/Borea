using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Borea.App.Views;

/// <summary>
/// Renders the CommonMark subset that listing descriptions use: headings,
/// paragraphs, bullet and numbered lists, fenced code, and inline bold,
/// italic, code and links. Anything else shows as its source text, which is
/// still readable. A full renderer can replace this without touching callers.
/// </summary>
public sealed partial class MarkdownView : StackPanel
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public MarkdownView()
    {
        Spacing = 12;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty)
            Rebuild();
    }

    private void Rebuild()
    {
        Children.Clear();
        var text = Markdown;
        if (string.IsNullOrWhiteSpace(text))
            return;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var paragraph = new List<string>();
        var index = 0;

        while (index < lines.Length)
        {
            var line = lines[index];

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph(paragraph);
                var code = new List<string>();
                index++;
                while (index < lines.Length && !lines[index].StartsWith("```", StringComparison.Ordinal))
                    code.Add(lines[index++]);
                index++;
                Children.Add(CodeBlock(string.Join("\n", code)));
                continue;
            }

            var heading = HeadingPattern().Match(line);
            if (heading.Success)
            {
                FlushParagraph(paragraph);
                Children.Add(Block(heading.Groups[2].Value, heading.Groups[1].Value.Length switch
                {
                    1 => "heading-lg",
                    2 => "heading-md",
                    _ => "heading-sm",
                }));
                index++;
                continue;
            }

            var bullet = BulletPattern().Match(line);
            if (bullet.Success)
            {
                FlushParagraph(paragraph);
                var marker = char.IsDigit(bullet.Groups[1].Value[0]) ? bullet.Groups[1].Value + " " : "•  ";
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(20, 0, 0, 0) };
                var markerBlock = new TextBlock { Text = marker, Classes = { "body-md" }, FontSize = 16, LineHeight = 26 };
                var content = Block(bullet.Groups[2].Value, "body-md");
                Grid.SetColumn(content, 1);
                row.Children.Add(markerBlock);
                row.Children.Add(content);
                Children.Add(row);
                index++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
                FlushParagraph(paragraph);
            else
                paragraph.Add(line.Trim());
            index++;
        }

        FlushParagraph(paragraph);
    }

    private void FlushParagraph(List<string> paragraph)
    {
        if (paragraph.Count == 0)
            return;

        Children.Add(Block(string.Join(" ", paragraph), "body-md"));
        paragraph.Clear();
    }

    private static TextBlock Block(string markdown, string textClass)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, Classes = { textClass } };
        if (textClass == "body-md")
        {
            block.FontSize = 16;
            block.LineHeight = 26;
        }

        foreach (var inline in Inlines(markdown))
            block.Inlines!.Add(inline);
        return block;
    }

    private static SelectableTextBlock CodeBlock(string code) => new()
    {
        Text = code,
        Classes = { "label-md" },
        FontSize = 13,
        TextWrapping = TextWrapping.Wrap,
        Padding = new Thickness(12),
        Background = Brushes.Black,
    };

    /// <summary>
    /// Splits one line of text into runs. Patterns are tried at each position
    /// in order, so a bold span may hold a link but not the other way round.
    /// </summary>
    private static IEnumerable<Inline> Inlines(string text)
    {
        var position = 0;
        foreach (Match match in InlinePattern().Matches(text))
        {
            if (match.Index > position)
                yield return new Run(text[position..match.Index]);

            if (match.Groups["bold"].Success)
                yield return new Bold { Inlines = { new Run(match.Groups["bold"].Value) } };
            else if (match.Groups["italic"].Success)
                yield return new Italic { Inlines = { new Run(match.Groups["italic"].Value) } };
            else if (match.Groups["code"].Success)
                yield return new Run(match.Groups["code"].Value) { FontFamily = new FontFamily("avares://Borea.App/Assets/Fonts#IBM Plex Mono"), Background = Brushes.Black };
            else if (match.Groups["link"].Success)
                yield return new Run(match.Groups["link"].Value) { TextDecorations = TextDecorations.Underline };

            position = match.Index + match.Length;
        }

        if (position < text.Length)
            yield return new Run(text[position..]);
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^\s*([-*+]|\d+\.)\s+(.*)$")]
    private static partial Regex BulletPattern();

    [GeneratedRegex(@"\*\*(?<bold>[^*]+)\*\*|__(?<bold>[^_]+)__|\*(?<italic>[^*]+)\*|_(?<italic>[^_]+)_|`(?<code>[^`]+)`|\[(?<link>[^\]]+)\]\([^)]+\)")]
    private static partial Regex InlinePattern();
}
