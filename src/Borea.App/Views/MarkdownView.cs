using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Borea.App.ViewModels;

namespace Borea.App.Views;

/// <summary>A run of inline spans, or an image that takes a line of its own.</summary>
public abstract record MarkdownPart;

public sealed record MarkdownTextPart(IReadOnlyList<MarkdownSpan> Spans) : MarkdownPart;

public sealed record MarkdownImagePart(ListingImage Image, string AlternativeText) : MarkdownPart;

/// <summary>A ksa-image reference to an id that has no record.</summary>
public sealed record MarkdownMissingImagePart(string AlternativeText) : MarkdownPart;

/// <summary>
/// Shows a listing description. <see cref="MarkdownParser"/> splits the text;
/// this control only turns blocks and spans into controls.
/// </summary>
public sealed class MarkdownView : StackPanel
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    public static readonly StyledProperty<DescriptionImages?> ImagesProperty =
        AvaloniaProperty.Register<MarkdownView, DescriptionImages?>(nameof(Images));

    private static readonly FontFamily MonoFont = new("avares://Borea.App/Assets/Fonts#IBM Plex Mono");

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

    public MarkdownView()
    {
        Spacing = 12;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty || change.Property == ImagesProperty)
            Rebuild();
    }

    internal static IReadOnlyList<MarkdownPart> Split(string text, DescriptionImages? images)
    {
        var parts = new List<MarkdownPart>();
        var spans = new List<MarkdownSpan>();
        foreach (var span in MarkdownParser.ParseInline(text))
        {
            if (span.Kind != MarkdownSpanKind.Image)
            {
                spans.Add(span);
            }
            else if (images is not null && DescriptionImages.TryGetId(span.Destination, out var id))
            {
                Flush();
                parts.Add(images.Find(id) is { } image
                    ? new MarkdownImagePart(image, span.Text)
                    : new MarkdownMissingImagePart(span.Text));
            }
            else if (span.Text.Length > 0)
            {
                spans.Add(new MarkdownSpan(MarkdownSpanKind.Text, span.Text));
            }
        }

        Flush();
        return parts;

        void Flush()
        {
            if (spans.Any(span => !string.IsNullOrWhiteSpace(span.Text)))
                parts.Add(new MarkdownTextPart(spans.ToArray()));
            spans.Clear();
        }
    }

    private void Rebuild()
    {
        Children.Clear();
        foreach (var block in MarkdownParser.Parse(Markdown))
        {
            if (block.Kind == MarkdownBlockKind.Code)
            {
                Children.Add(Code(block.Text));
                continue;
            }

            var parts = Split(block.Text, Images);
            for (var index = 0; index < parts.Count; index++)
                Children.Add(Build(block, parts[index], first: index == 0));
        }
    }

    private static Control Build(MarkdownBlock block, MarkdownPart part, bool first)
    {
        if (part is MarkdownTextPart text)
        {
            return block.Kind switch
            {
                MarkdownBlockKind.Heading => Text(text.Spans, block.Level switch
                {
                    1 => "heading-lg",
                    2 => "heading-md",
                    _ => "heading-sm",
                }),
                MarkdownBlockKind.ListItem => ListItem(block, text.Spans, first),
                _ => Text(text.Spans, "body-md"),
            };
        }

        var image = part is MarkdownImagePart found ? Figure(found) : MissingImage(((MarkdownMissingImagePart)part).AlternativeText);
        if (block.Kind == MarkdownBlockKind.ListItem)
            image.Margin = new Thickness(20, 0, 0, 0);
        return image;
    }

    /// <summary>A continuation of an item keeps the width of the marker, so its text lines up with the first part.</summary>
    private static Grid ListItem(MarkdownBlock block, IReadOnlyList<MarkdownSpan> spans, bool first)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(20, 0, 0, 0) };
        var marker = new TextBlock { Text = block.Marker + "  ", Classes = { "body-md" }, FontSize = 16, LineHeight = 26, Opacity = first ? 1 : 0 };
        var content = Text(spans, "body-md");
        Grid.SetColumn(content, 1);
        row.Children.Add(marker);
        row.Children.Add(content);
        return row;
    }

    private static TextBlock Text(IReadOnlyList<MarkdownSpan> spans, string textClass)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Classes = { textClass } };
        if (textClass == "body-md")
        {
            text.FontSize = 16;
            text.LineHeight = 26;
        }

        foreach (var span in spans)
        {
            text.Inlines!.Add(span.Kind switch
            {
                MarkdownSpanKind.Bold => new Bold { Inlines = { new Run(span.Text) } },
                MarkdownSpanKind.Italic => new Italic { Inlines = { new Run(span.Text) } },
                MarkdownSpanKind.Code => new Run(span.Text) { FontFamily = MonoFont, Background = Brushes.Black },
                MarkdownSpanKind.Link => new Run(span.Text) { TextDecorations = TextDecorations.Underline },
                _ => new Run(span.Text),
            });
        }

        return text;
    }

    private static Control Figure(MarkdownImagePart part)
    {
        var image = part.Image;
        var view = new ListingImageView
        {
            Image = image,
            LayoutFromRecord = true,
            Background = Resource<IBrush>("Brush.Surface"),
            Child = new Path { Classes = { "icon", "size-32" }, Data = Resource<Geometry>("Icon.Image") },
        };
        AutomationProperties.SetName(view, part.AlternativeText);

        var frame = new Border { Classes = { "thumbnail" }, HorizontalAlignment = HorizontalAlignment.Left, Child = view };
        if (part.AlternativeText.Length > 0)
            ToolTip.SetTip(frame, part.AlternativeText);

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

    private static Border MissingImage(string alternativeText)
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

    private static SelectableTextBlock Code(string code) => new()
    {
        Text = code,
        Classes = { "label-md" },
        FontSize = 13,
        TextWrapping = TextWrapping.Wrap,
        Padding = new Thickness(12),
        Background = Brushes.Black,
    };

    private static T? Resource<T>(string key)
        where T : class
        => Application.Current?.TryFindResource(key, out var value) == true ? value as T : null;
}
