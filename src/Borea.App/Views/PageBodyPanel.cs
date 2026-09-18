using System;
using Avalonia;
using Avalonia.Controls;

namespace Borea.App.Views;

/// <summary>
/// Stacks the sections of a page body with the responsive side margin. The
/// margin grows in steps with the page width, and the body stops growing at the
/// width of the large step, with the extra space on both sides. With the side
/// panel, the body fills the area left of the panel the same way.
/// </summary>
public sealed class PageBodyPanel : Panel
{
    public static readonly StyledProperty<bool> HasSidePanelProperty =
        AvaloniaProperty.Register<PageBodyPanel, bool>(nameof(HasSidePanel));

    public const double NavigationRailWidth = 80;

    public const double SmallMargin = 24;

    public const double RegularMargin = 64;

    public const double LargeMargin = 128;

    public const double RegularFromWidth = 1280 - NavigationRailWidth;

    public const double LargeFromWidth = 1600 - NavigationRailWidth;

    public const double MaxBodyWidth = LargeFromWidth - 2 * LargeMargin;

    public const double PageTopMargin = 42;

    public const double PageBottomMargin = 32;

    public const double SidePanelWidth = 300;

    /// <summary>The space between the side panel and the edges of the page.</summary>
    public const double SidePanelInset = 24;

    public const double MaxSidePanelBodyWidth = MaxBodyWidth - SidePanelWidth - SidePanelInset;

    public static Thickness SidePanelMargin { get; } = new(0, SidePanelInset, SidePanelInset, SidePanelInset);

    static PageBodyPanel()
    {
        AffectsMeasure<PageBodyPanel>(HasSidePanelProperty);
    }

    /// <summary>Keeps the body left of the side panel that the page shows along its right edge.</summary>
    public bool HasSidePanel
    {
        get => GetValue(HasSidePanelProperty);
        set => SetValue(HasSidePanelProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var available = double.IsInfinity(availableSize.Width) ? MaxBodyWidth + 2 * LargeMargin : availableSize.Width;
        var (_, width) = Place(available, HasSidePanel);
        var height = PageTopMargin + PageBottomMargin;
        foreach (var child in Children)
        {
            child.Measure(new Size(width, double.PositiveInfinity));
            height += child.DesiredSize.Height;
        }

        return new Size(available, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (left, width) = Place(finalSize.Width, HasSidePanel);
        var top = PageTopMargin;
        foreach (var child in Children)
        {
            child.Arrange(new Rect(left, top, width, child.DesiredSize.Height));
            top += child.DesiredSize.Height;
        }

        return finalSize;
    }

    /// <summary>The side margin of a page that is <paramref name="available"/> wide.</summary>
    internal static double SideMargin(double available) =>
        available >= LargeFromWidth ? LargeMargin
        : available >= RegularFromWidth ? RegularMargin
        : SmallMargin;

    /// <summary>Where the body goes on a page that is <paramref name="available"/> wide.</summary>
    internal static (double Left, double Width) Place(double available, bool hasSidePanel)
    {
        var margin = SideMargin(available);
        var area = hasSidePanel ? available - SidePanelWidth - SidePanelInset : available;
        var width = Math.Clamp(area - 2 * margin, 0, hasSidePanel ? MaxSidePanelBodyWidth : MaxBodyWidth);
        return (Math.Max(0, (area - width) / 2), width);
    }
}
