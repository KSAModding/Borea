using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Borea.App.ViewModels;
using Borea.Core.Listings;
using Markdig.Extensions.Tables;
using Markdig.Syntax;

namespace Borea.App.Views;

/// <summary>
/// Shows a description, release notes or a post. <see cref="MarkdownSyntax"/> parses the text,
/// this control turns its blocks into controls, and <see cref="MarkdownText"/> writes the text of each block.
/// </summary>
public sealed class MarkdownView : StackPanel
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    public static readonly StyledProperty<DescriptionImages?> ImagesProperty =
        AvaloniaProperty.Register<MarkdownView, DescriptionImages?>(nameof(Images));

    public static readonly StyledProperty<bool> IsCompactProperty =
        AvaloniaProperty.Register<MarkdownView, bool>(nameof(IsCompact));

    internal static readonly FontFamily MonoFont = new("avares://Borea.App/Assets/Fonts#IBM Plex Mono");

    private const string Bullet = "\u2022";

    /// <summary>The source of each shown top level block and the number of children it made, in the order of the children.</summary>
    private List<(string? Source, int Count)> _shown = [];

    /// <summary>The link reference definitions of the shown text.</summary>
    private string _definitions = string.Empty;

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    /// <summary>The images the description can show. Null shows every image as its alternative text, which fits a changelog.</summary>
    public DescriptionImages? Images
    {
        get => GetValue(ImagesProperty);
        set => SetValue(ImagesProperty, value);
    }

    /// <summary>Small sizes for a text that is one part of a list, such as a changelog, where the heading of an author must not outgrow the name of the mod.</summary>
    public bool IsCompact
    {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    private MarkdownMetrics Metrics => IsCompact ? MarkdownMetrics.Compact : MarkdownMetrics.Regular;

    public MarkdownView()
    {
        Spacing = MarkdownMetrics.Regular.BlockSpacing;
        new MarkdownSelection(this);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCompactProperty)
            Spacing = Metrics.BlockSpacing;

        if (change.Property == ImagesProperty || change.Property == IsCompactProperty)
            Clear();

        if (change.Property == MarkdownProperty || change.Property == ImagesProperty || change.Property == IsCompactProperty)
            Rebuild();
    }

    private void Clear()
    {
        Children.Clear();
        _shown = [];
    }

    /// <summary>
    /// Keeps the children of the unchanged top level blocks at the start and at the end, so typing rebuilds only the edited blocks,
    /// and an image next to them stays loaded instead of loading again on every key. A block is unchanged when its source is,
    /// and a change of the link reference definitions rebuilds every block, because any block can use them.
    /// </summary>
    private void Rebuild()
    {
        var document = string.IsNullOrWhiteSpace(Markdown) ? null : MarkdownSyntax.Parse(Markdown);
        var blocks = document?.Where(block => block is not LinkReferenceDefinitionGroup).ToList() ?? [];
        var definitions = document is null ? string.Empty : Definitions(document);
        if (definitions != _definitions)
        {
            Clear();
            _definitions = definitions;
        }

        var sources = blocks.Select((block, index) => Source(Markdown!, block, index == blocks.Count - 1)).ToList();
        var start = 0;
        while (start < sources.Count && start < _shown.Count && Same(sources[start], _shown[start].Source))
            start++;

        var end = 0;
        while (end < sources.Count - start && end < _shown.Count - start && Same(sources[^(end + 1)], _shown[^(end + 1)].Source))
            end++;

        var first = _shown.Take(start).Sum(block => block.Count);
        Children.RemoveRange(first, _shown.Skip(start).Take(_shown.Count - start - end).Sum(block => block.Count));

        var built = new Controls();
        var shown = _shown.Take(start).ToList();
        for (var index = start; index < blocks.Count - end; index++)
        {
            var count = built.Count;
            AddBlocks(built, [blocks[index]], Images, Metrics);
            shown.Add((sources[index], built.Count - count));
        }

        Children.InsertRange(first, built);
        shown.AddRange(_shown.Skip(_shown.Count - end));
        _shown = shown;
    }

    /// <summary>
    /// The text that decides what a block draws, or null when the block has no span inside the text. The span starts after the indent
    /// of the first line, but that indent moves the columns of a code block or a list item, so the text starts at the start of the line.
    /// The last block runs to the end of the text, because a code block without a closing fence holds blank lines after its span.
    /// </summary>
    private static string? Source(string markdown, Block block, bool last)
    {
        if (block.Span.Start < 0 || block.Span.Length <= 0 || block.Span.End >= markdown.Length)
            return null;

        var line = block.Span.Start == 0 ? 0 : markdown.LastIndexOf('\n', block.Span.Start - 1) + 1;
        return last ? markdown[line..] : markdown[line..(block.Span.End + 1)];
    }

    /// <summary>A block without a source is never the same as a shown block, so it is always built again.</summary>
    private static bool Same(string? source, string? shown) => source is not null && source == shown;

    private static string Definitions(MarkdownDocument document) =>
        string.Join('\n', document.OfType<LinkReferenceDefinitionGroup>().SelectMany(group => group.Links).Select(link => $"{link.Key}\n{link.Value.Url}\n{link.Value.Title}"));

    /// <summary>A link reference definition, and any block that CommonMark has but this view does not draw, shows nothing.</summary>
    private static void AddBlocks(Controls target, IEnumerable<Block> blocks, DescriptionImages? images, MarkdownMetrics metrics)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    MarkdownText.Write(target, heading.Inline, metrics.HeadingClass(heading.Level), images, metrics);
                    break;
                case ParagraphBlock paragraph:
                    MarkdownText.Write(target, paragraph.Inline, textClass: null, images, metrics);
                    break;
                case CodeBlock code:
                    target.Add(Code(code.Lines.ToString(), metrics));
                    break;
                case HtmlBlock html when MarkdownText.HtmlBlockText(html) is { Length: > 0 } text:
                    var body = MarkdownText.Body(metrics);
                    body.Inlines!.Add(new Run(text));
                    target.Add(body);
                    break;
                case ThematicBreakBlock:
                    target.Add(new Border { Height = 1, Background = Resource<IBrush>("Brush.BorderStrong") });
                    break;
                case ListBlock list:
                    target.Add(List(list, images, metrics));
                    break;
                case QuoteBlock quote:
                    target.Add(new Border
                    {
                        BorderBrush = Resource<IBrush>("Brush.BorderStrong"),
                        BorderThickness = new Thickness(3, 0, 0, 0),
                        Padding = new Thickness(16, 0, 0, 0),
                        Child = Blocks(quote, images, metrics),
                    });
                    break;
                case Table table:
                    target.Add(Table(table, images, metrics));
                    break;
            }
        }
    }

    private static StackPanel Blocks(ContainerBlock container, DescriptionImages? images, MarkdownMetrics metrics)
    {
        var panel = new StackPanel { Spacing = metrics.BlockSpacing };
        AddBlocks(panel.Children, container, images, metrics);
        return panel;
    }

    /// <summary>Each item is a row of its marker and its blocks, so a nested list sits one indent deeper than the item that holds it.</summary>
    private static StackPanel List(ListBlock list, DescriptionImages? images, MarkdownMetrics metrics)
    {
        var panel = new StackPanel { Spacing = metrics.ListItemSpacing, Margin = new Thickness(metrics.ListIndent, 0, 0, 0) };
        var number = list.IsOrdered && int.TryParse(list.OrderedStart, NumberStyles.None, CultureInfo.InvariantCulture, out var start) ? start : 1;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var marker = list.IsOrdered ? (number++).ToString(CultureInfo.InvariantCulture) + list.OrderedDelimiter : Bullet;
            var content = Blocks(item, images, metrics);
            Grid.SetColumn(content, 1);
            panel.Children.Add(new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Children =
                {
                    new TextBlock { Text = marker + "  ", Classes = { "body-md" }, FontSize = metrics.BodySize, LineHeight = metrics.BodyLineHeight },
                    content,
                },
            });
        }

        return panel;
    }

    /// <summary>
    /// The header row decides the number of columns, as GFM does. Each column gets a share of the width by the
    /// longest cell in it, so the cells wrap inside the view instead of pushing it wider.
    /// </summary>
    private static Border Table(Table table, DescriptionImages? images, MarkdownMetrics metrics)
    {
        var rows = table.OfType<TableRow>().ToList();
        var columns = rows.Count > 0 ? rows[0].Count : 0;
        var grid = new Grid();
        for (var column = 0; column < columns; column++)
        {
            var longest = rows.Max(row => column < row.Count ? row[column].Span.Length : 0);
            grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Clamp(longest, 4, 40), GridUnitType.Star));
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var column = 0; column < columns; column++)
            {
                var cell = new Border
                {
                    BorderBrush = Resource<IBrush>("Brush.BorderStrong"),
                    BorderThickness = new Thickness(column > 0 ? 1 : 0, index > 0 ? 1 : 0, 0, 0),
                    Padding = metrics.TableCellPadding,
                    Child = column < row.Count ? Blocks((TableCell)row[column], images, metrics) : new StackPanel(),
                };
                if (row.IsHeader)
                {
                    cell.Background = Resource<IBrush>("Brush.SurfaceRaised");
                    TextElement.SetFontWeight(cell, FontWeight.SemiBold);
                }

                Grid.SetRow(cell, index);
                Grid.SetColumn(cell, column);
                grid.Children.Add(cell);
            }
        }

        return new Border
        {
            BorderBrush = Resource<IBrush>("Brush.BorderStrong"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = grid,
        };
    }

    internal static Control Figure(ListingImage image, string alternativeText)
    {
        var view = new ListingImageView
        {
            Image = image,
            LayoutFromRecord = true,
            Background = Resource<IBrush>("Brush.Surface"),
            Child = new Path { Classes = { "icon", "size-32" }, Data = Resource<Geometry>("Icon.Image") },
        };
        AutomationProperties.SetName(view, alternativeText);

        var frame = new Border { Classes = { "thumbnail" }, HorizontalAlignment = HorizontalAlignment.Left, Child = view };
        if (alternativeText.Length > 0)
            ToolTip.SetTip(frame, alternativeText);

        if (!image.HasCredit)
            return frame;

        var figure = new StackPanel { Spacing = 4, Children = { frame } };
        if (image.Attribution is { } attribution)
            figure.Children.Add(new TextBlock { Text = attribution, Classes = { "body-sm", "secondary" }, TextWrapping = TextWrapping.Wrap });

        if (image.Source is { } source)
        {
            var link = new Button
            {
                Classes = { "link" },
                HorizontalAlignment = HorizontalAlignment.Left,
                Command = image.OpenSourceCommand,
                Content = new TextBlock { Text = new Uri(source).Host, Classes = { "action-md" } },
            };
            ToolTip.SetTip(link, source);
            AutomationProperties.SetName(link, source);
            figure.Children.Add(link);
        }

        return figure;
    }

    internal static Border MissingImage(string alternativeText)
    {
        var glyph = new Path { Classes = { "icon" }, Data = Resource<Geometry>("Icon.Image"), Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(glyph, Dock.Left);
        var row = new DockPanel { Children = { glyph } };
        if (alternativeText.Length > 0)
            row.Children.Add(new TextBlock { Text = alternativeText, Classes = { "body-sm", "secondary" }, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });

        var placeholder = new Border { Classes = { "thumbnail" }, Padding = new Thickness(12, 8), HorizontalAlignment = HorizontalAlignment.Left, Child = row };
        AutomationProperties.SetName(placeholder, alternativeText);
        return placeholder;
    }

    /// <summary>The text classes style only a TextBlock, so the selectable code block takes the font of label-md itself.</summary>
    private static SelectableTextBlock Code(string code, MarkdownMetrics metrics) => new()
    {
        Text = code,
        FontFamily = MonoFont,
        Foreground = Resource<IBrush>("Brush.TextSecondary"),
        FontSize = metrics.CodeBlockSize,
        TextWrapping = TextWrapping.Wrap,
        Padding = new Thickness(12),
        Background = Resource<IBrush>("Brush.SurfaceHeader"),
    };

    internal static T? Resource<T>(string key)
        where T : class
        => Application.Current?.TryFindResource(key, out var value) == true ? value as T : null;
}
