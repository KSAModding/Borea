using System;
using Avalonia;
using Avalonia.Controls;

namespace Borea.App.Views;

/// <summary>
/// Lays out the body of a page that has the filter or detail panel along its
/// right edge. While the window is wide enough, the body keeps the center line
/// that Home and Library use. When it is not, the body moves left only as far
/// as it has to, so it never runs under the panel. On a narrow window the gap
/// to the panel closes first, and only then does the body get narrower.
/// </summary>
public sealed class PageBodyPanel : Panel
{
    /// <summary>The 700 of the design in #8, plus the side margins of the body.</summary>
    public const double BodyWidth = 764;

    /// <summary>The panel, 300 wide, with its margin to the window edge.</summary>
    public const double PanelWidth = 324;

    /// <summary>The widest extra gap between the body and the panel, which gives the layout of the design at 1280.</summary>
    public const double Gap = 56;

    protected override Size MeasureOverride(Size availableSize)
    {
        var available = double.IsInfinity(availableSize.Width) ? BodyWidth + Gap + PanelWidth : availableSize.Width;
        var (_, width) = Place(available);
        var height = 0.0;
        foreach (var child in Children)
        {
            child.Measure(new Size(width, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(available, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (left, width) = Place(finalSize.Width);
        foreach (var child in Children)
            child.Arrange(new Rect(left, 0, width, finalSize.Height));

        return finalSize;
    }

    /// <summary>Where the body goes on a page that is <paramref name="available"/> wide.</summary>
    internal static (double Left, double Width) Place(double available)
    {
        var width = Math.Clamp(available - PanelWidth, 0, BodyWidth);
        var gap = Math.Clamp(available - PanelWidth - width, 0, Gap);
        var left = Math.Min((available - width) / 2, available - PanelWidth - gap - width);
        return (Math.Max(0, left), width);
    }
}
