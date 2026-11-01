using Avalonia;

namespace Borea.App.Views;

/// <summary>The sizes of the blocks of a <see cref="MarkdownView"/>, which the text classes cannot set, because a class styles only a TextBlock.</summary>
internal sealed record MarkdownMetrics(
    string LargeHeadingClass,
    string MediumHeadingClass,
    string SmallHeadingClass,
    double BodySize,
    double BodyLineHeight,
    double BlockSpacing,
    double ListItemSpacing,
    double ListIndent,
    double InlineCodeSize,
    double CodeBlockSize,
    Thickness TableCellPadding)
{
    public static readonly MarkdownMetrics Regular = new("heading-lg", "heading-md", "heading-sm", 16, 26, 12, 12, 20, 14, 13, new Thickness(12, 8));

    public static readonly MarkdownMetrics Compact = new("heading-sm", "heading-sm", "heading-xs", 12, 18, 6, 2, 12, 12, 12, new Thickness(8, 4));

    public string HeadingClass(int level) => level switch
    {
        1 => LargeHeadingClass,
        2 => MediumHeadingClass,
        _ => SmallHeadingClass,
    };
}
