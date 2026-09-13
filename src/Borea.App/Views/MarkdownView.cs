using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Borea.App.Views;

/// <summary>
/// Shows a listing description. <see cref="MarkdownParser"/> splits the text;
/// this control only turns blocks and spans into text controls.
/// </summary>
public sealed class MarkdownView : StackPanel
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    private static readonly FontFamily MonoFont = new("avares://Borea.App/Assets/Fonts#IBM Plex Mono");

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
        foreach (var block in MarkdownParser.Parse(Markdown))
        {
            Children.Add(block.Kind switch
            {
                MarkdownBlockKind.Heading => Text(block.Text, block.Level switch
                {
                    1 => "heading-lg",
                    2 => "heading-md",
                    _ => "heading-sm",
                }),
                MarkdownBlockKind.ListItem => ListItem(block),
                MarkdownBlockKind.Code => Code(block.Text),
                _ => Text(block.Text, "body-md"),
            });
        }
    }

    private static Grid ListItem(MarkdownBlock block)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(20, 0, 0, 0) };
        var marker = new TextBlock { Text = block.Marker + "  ", Classes = { "body-md" }, FontSize = 16, LineHeight = 26 };
        var content = Text(block.Text, "body-md");
        Grid.SetColumn(content, 1);
        row.Children.Add(marker);
        row.Children.Add(content);
        return row;
    }

    private static TextBlock Text(string markdown, string textClass)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Classes = { textClass } };
        if (textClass == "body-md")
        {
            text.FontSize = 16;
            text.LineHeight = 26;
        }

        foreach (var span in MarkdownParser.ParseInline(markdown))
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

    private static SelectableTextBlock Code(string code) => new()
    {
        Text = code,
        Classes = { "label-md" },
        FontSize = 13,
        TextWrapping = TextWrapping.Wrap,
        Padding = new Thickness(12),
        Background = Brushes.Black,
    };
}
