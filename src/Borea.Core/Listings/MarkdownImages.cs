using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Borea.Core.Listings;

/// <summary>
/// Finds the images of a description in the parse of <see cref="MarkdownSyntax"/>, as the image reference check of
/// content-index does: inline and reference images outside code, and raw HTML images.
/// </summary>
public static partial class MarkdownImages
{
    public const string Scheme = "ksa-image:";

    /// <summary>The destination of every Markdown image, and the number of raw HTML images.</summary>
    public static (IReadOnlyList<string> Destinations, int HtmlImages) Scan(string markdown)
    {
        var destinations = new List<string>();
        var html = 0;
        foreach (var node in MarkdownSyntax.Walk(MarkdownSyntax.Parse(markdown)))
        {
            switch (node)
            {
                case LinkInline { IsImage: true } image:
                    destinations.Add(image.Url ?? string.Empty);
                    break;
                case HtmlInline inline:
                    html += HtmlImage().Count(inline.Tag);
                    break;
                case HtmlBlock block:
                    html += HtmlImage().Count(block.Lines.ToString());
                    break;
            }
        }

        return (destinations, html);
    }

    /// <summary>The ids of the ksa-image references, in the order they appear.</summary>
    public static IReadOnlyList<string> References(string markdown)
    {
        var ids = new List<string>();
        foreach (var destination in Scan(markdown).Destinations)
        {
            if (TryGetId(destination, out var id))
                ids.Add(id);
        }

        return ids;
    }

    /// <summary>The record id that an image destination names, when it is a ksa-image reference.</summary>
    public static bool TryGetId(string? destination, [NotNullWhen(true)] out string? id)
    {
        id = destination is not null && destination.StartsWith(Scheme, StringComparison.Ordinal) ? destination[Scheme.Length..] : null;
        return id is not null;
    }

    [GeneratedRegex(@"<\s*(?:img|image|picture|svg)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlImage();
}
