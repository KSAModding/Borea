using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Borea.App.ViewModels;
using Borea.Core.Listings;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Inline = Markdig.Syntax.Inlines.Inline;

namespace Borea.App.Views;

/// <summary>
/// Writes the inlines of one block as text. An image that the description shows takes a line of its own,
/// so it ends one text block and the text after it starts the next. Raw HTML draws only as its text.
/// </summary>
internal sealed partial class MarkdownText
{
    private const string LineFeed = "\n";

    private static readonly HashSet<string> BlockTags = new(
        ["p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "li", "ul", "ol", "dl", "dt", "dd", "tr", "table", "blockquote", "details", "summary", "center", "pre", "hr", "section", "figure", "figcaption"],
        StringComparer.OrdinalIgnoreCase);

    private static Cursor? _hand;

    private readonly Controls _target;
    private readonly string? _textClass;
    private readonly DescriptionImages? _images;
    private readonly MarkdownMetrics _metrics;
    private readonly List<MarkdownLink> _links = [];
    private TextBlock _text;
    private int _length;

    private MarkdownText(Controls target, string? textClass, DescriptionImages? images, MarkdownMetrics metrics)
    {
        _metrics = metrics;
        _target = target;
        _textClass = textClass;
        _images = images;
        _text = NewText();
    }

    /// <param name="textClass">The text class of a heading, or null for body text.</param>
    public static void Write(Controls target, ContainerInline? inlines, string? textClass, DescriptionImages? images, MarkdownMetrics metrics)
    {
        var writer = new MarkdownText(target, textClass, images, metrics);
        if (inlines is not null)
            writer.WriteChildren(inlines, default);
        writer.Flush();
    }

    /// <summary>The text classes style only a TextBlock, so the selectable body text sets its size, line height and color itself.</summary>
    public static SelectableTextBlock Body(MarkdownMetrics metrics) => new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontSize = metrics.BodySize,
        LineHeight = metrics.BodyLineHeight,
        Foreground = MarkdownView.Resource<IBrush>("Brush.Text"),
    };

    private TextBlock NewText() => _textClass is null
        ? Body(_metrics)
        : new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, Classes = { _textClass } };

    private void WriteChildren(ContainerInline container, RunStyle style)
    {
        foreach (var child in container)
            Write(child, style);
    }

    private void Write(Inline inline, RunStyle style)
    {
        switch (inline)
        {
            case LiteralInline literal:
                Append(literal.Content.ToString(), style);
                break;
            case HtmlEntityInline entity:
                Append(entity.Transcoded.ToString(), style);
                break;
            case LineBreakInline lineBreak:
                Append(lineBreak.IsHard ? LineFeed : " ", style);
                break;
            case CodeInline code:
                Add(new Run(code.Content) { FontFamily = MarkdownView.MonoFont, FontSize = _metrics.InlineCodeSize, Background = MarkdownView.Resource<IBrush>("Brush.SurfaceRaised") }, style.Link);
                break;
            case AutolinkInline autolink:
                Append(autolink.Url, style with { Link = MainViewModel.WebLink(autolink.Url) });
                break;
            case HtmlInline html:
                Append(HtmlInlineText(html.Tag), style);
                break;
            case LinkInline { IsImage: true } image:
                Image(image, style);
                break;
            case LinkInline link:
                WriteChildren(link, style with { Link = MainViewModel.WebLink(link.Url) });
                break;
            case EmphasisInline emphasis:
                WriteChildren(emphasis, emphasis.DelimiterCount >= 2 ? style with { Bold = true } : style with { Italic = true });
                break;
            case ContainerInline container:
                WriteChildren(container, style);
                break;
        }
    }

    /// <summary>Only a ksa-image reference of a description draws as a picture, as RFC 0058 says, and any other image shows as its alternative text.</summary>
    private void Image(LinkInline image, RunStyle style)
    {
        var alternativeText = MarkdownSyntax.PlainText(image);
        if (_images is null || !MarkdownImages.TryGetId(image.Url, out var id))
        {
            Append(alternativeText, style);
            return;
        }

        Flush();
        _target.Add(_images.Find(id) is { } found ? MarkdownView.Figure(found, alternativeText) : MarkdownView.MissingImage(alternativeText));
    }

    private void Append(string text, RunStyle style)
    {
        if (text.Length == 0)
            return;

        var run = new Run(text);
        if (style.Bold)
            run.FontWeight = FontWeight.Bold;
        if (style.Italic)
            run.FontStyle = FontStyle.Italic;
        Add(run, style.Link);
    }

    private void Add(Run run, string? link)
    {
        var length = run.Text!.Length;
        if (link is not null)
        {
            run.Foreground = MarkdownView.Resource<IBrush>("Brush.Accent");
            run.TextDecorations = TextDecorations.Underline;
            if (_links is [.., var last] && last.Url == link && last.End == _length)
                _links[^1] = last with { End = _length + length };
            else
                _links.Add(new MarkdownLink(_length, _length + length, link));
        }

        _text.Inlines!.Add(run);
        _length += length;
    }

    private void Flush()
    {
        if (_text.Inlines!.OfType<Run>().Any(run => !string.IsNullOrWhiteSpace(run.Text)))
        {
            if (_links.Count > 0)
                Attach(_text, _links.ToArray());
            _target.Add(_text);
        }

        _text = NewText();
        _links.Clear();
        _length = 0;
    }

    /// <summary>
    /// A click opens a link only when it pressed and released on the same link and selected nothing,
    /// so a drag that selects text over a link does not open it. The press reads its link while it tunnels,
    /// because a hit test after the text block moved its selection finds no text under the pointer.
    /// </summary>
    private static void Attach(TextBlock text, MarkdownLink[] links)
    {
        string? pressed = null;
        text.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
            pressed = e.GetCurrentPoint(text).Properties.IsLeftButtonPressed ? LinkAt(text, links, e.GetPosition(text)) : null, RoutingStrategies.Tunnel);
        text.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
        {
            var clicked = pressed;
            pressed = null;
            if (clicked is null || e.InitialPressMouseButton != MouseButton.Left || LinkAt(text, links, e.GetPosition(text)) != clicked
                || text is SelectableTextBlock selectable && selectable.SelectionStart != selectable.SelectionEnd)
                return;

            if (TopLevel.GetTopLevel(text)?.DataContext is MainViewModel viewModel)
                viewModel.OpenMarkdownLink(clicked);
        }, handledEventsToo: true);
        text.PointerMoved += (_, e) => Hover(text, LinkAt(text, links, e.GetPosition(text)));
        text.PointerExited += (_, _) => Hover(text, null);
    }

    /// <summary>
    /// Finds the line itself, because TextLayout.HitTestPoint compares the point with the height of a line and not with its place,
    /// so it puts a point on any line below the first outside the text.
    /// </summary>
    private static string? LinkAt(TextBlock text, MarkdownLink[] links, Point point)
    {
        var top = 0.0;
        foreach (var line in text.TextLayout.TextLines)
        {
            if (point.Y < top)
                return null;

            if (point.Y < top + line.Height)
            {
                if (point.X < line.Start || point.X > line.Start + line.Width)
                    return null;

                var index = line.GetCharacterHitFromDistance(point.X).FirstCharacterIndex;
                return links.FirstOrDefault(link => index >= link.Start && index < link.End)?.Url;
            }

            top += line.Height;
        }

        return null;
    }

    /// <summary>The address of a link shows as the tooltip while the pointer is on it.</summary>
    private static void Hover(TextBlock text, string? link)
    {
        if (Equals(ToolTip.GetTip(text), link))
            return;

        if (link is null)
        {
            text.ClearValue(InputElement.CursorProperty);
            text.ClearValue(ToolTip.TipProperty);
            return;
        }

        text.Cursor = _hand ??= new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(text, link);
    }

    /// <summary>A br is a line break and an image its alternative text. Any other tag shows nothing.</summary>
    private static string HtmlInlineText(string tag) =>
        HtmlTag().Match(tag) is { Success: true } match ? WebUtility.HtmlDecode(TagText(match, block: false)) : string.Empty;

    /// <summary>
    /// The text of an HTML block as a browser would break it into lines. Comments, scripts and styles give no text,
    /// also when they are not closed, and the line breaks of the source are spaces, as in HTML.
    /// </summary>
    internal static string HtmlBlockText(HtmlBlock block)
    {
        if (block.Type is HtmlBlockType.Comment or HtmlBlockType.ProcessingInstruction or HtmlBlockType.DocumentType or HtmlBlockType.CData)
            return string.Empty;

        var html = HiddenElement().Replace(block.Lines.ToString(), string.Empty).Replace(LineFeed, " ", StringComparison.Ordinal);
        var text = WebUtility.HtmlDecode(HtmlTag().Replace(html, match => TagText(match, block: true)));
        return string.Join(LineFeed, text.Split(LineFeed).Select(line => Whitespace().Replace(line, " ").Trim()).Where(line => line.Length > 0));
    }

    private static string TagText(Match tag, bool block)
    {
        if (!tag.Groups["name"].Success)
            return string.Empty;

        var name = tag.Groups["name"].Value;
        if (name.Equals("br", StringComparison.OrdinalIgnoreCase))
            return LineFeed;
        if (name.Equals("img", StringComparison.OrdinalIgnoreCase) || name.Equals("image", StringComparison.OrdinalIgnoreCase))
            return HtmlAlt().Match(tag.Groups["attributes"].Value).Groups["alt"].Value;
        return block && BlockTags.Contains(name) ? LineFeed : string.Empty;
    }

    private readonly record struct RunStyle(bool Bold, bool Italic, string? Link);

    /// <summary>The characters from <see cref="Start"/> up to <see cref="End"/> of a text block link to <see cref="Url"/>.</summary>
    private sealed record MarkdownLink(int Start, int End, string Url);

    [GeneratedRegex("""<!--[\s\S]*?(?:-->|$)|</?(?<name>[A-Za-z][A-Za-z0-9-]*)(?<attributes>(?:"[^"]*"|'[^']*'|[^'"<>])*)>""")]
    private static partial Regex HtmlTag();

    [GeneratedRegex("""<(?<name>script|style)\b[\s\S]*?(?:</\k<name>\s*>|$)""", RegexOptions.IgnoreCase)]
    private static partial Regex HiddenElement();

    [GeneratedRegex("""\balt\s*=\s*(?:"(?<alt>[^"]*)"|'(?<alt>[^']*)'|(?<alt>[^\s"'>]+))""", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlAlt();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
