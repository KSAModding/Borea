using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Borea.App.Views;

/// <summary>
/// Stacks the sections of a page body with the responsive side margin. The
/// margin grows in steps with the page width, and the body stops growing at the
/// width of the large step, with the extra space on both sides. With the side
/// panel, the body keeps the same place and width, so it does not jump between
/// pages, and gives up only the room on its right that the panel needs.
/// </summary>
public sealed class PageBodyPanel : Panel
{
    public static readonly StyledProperty<bool> HasSidePanelProperty =
        AvaloniaProperty.Register<PageBodyPanel, bool>(nameof(HasSidePanel));

    public static readonly StyledProperty<bool> HasTopMarginProperty =
        AvaloniaProperty.Register<PageBodyPanel, bool>(nameof(HasTopMargin), defaultValue: true);

    public static readonly StyledProperty<bool> HasBottomMarginProperty =
        AvaloniaProperty.Register<PageBodyPanel, bool>(nameof(HasBottomMargin), defaultValue: true);

    public const double NavigationRailWidth = 80;

    public const double SmallMargin = 24;

    public const double RegularMargin = 64;

    public const double LargeMargin = 128;

    public const double RegularFromWidth = 1280 - NavigationRailWidth;

    public const double LargeFromWidth = 1600 - NavigationRailWidth;

    public const double MaxBodyWidth = LargeFromWidth - 2 * LargeMargin;

    public const double PageTopMargin = 42;

    public const double PageBottomMargin = 32;

    public const double MaxHeaderMessagesHeight = 200;

    /// <summary>Turns the page height into the most room the messages in a fixed page header take before they scroll on their own.</summary>
    public static FuncValueConverter<double, double> HeaderMessagesMaxHeight { get; } = new(pageHeight => Math.Min(MaxHeaderMessagesHeight, pageHeight / 4));

    public const double SidePanelWidth = 300;

    /// <summary>The space between the side panel and the edges of the page.</summary>
    public const double SidePanelInset = 24;

    public static Thickness SidePanelMargin { get; } = new(0, SidePanelInset, SidePanelInset, SidePanelInset);

    static PageBodyPanel()
    {
        AffectsMeasure<PageBodyPanel>(HasSidePanelProperty, HasTopMarginProperty, HasBottomMarginProperty);
    }

    /// <summary>Keeps the body left of the side panel that the page shows along its right edge.</summary>
    public bool HasSidePanel
    {
        get => GetValue(HasSidePanelProperty);
        set => SetValue(HasSidePanelProperty, value);
    }

    /// <summary>False for the part of a page that scrolls below a fixed header.</summary>
    public bool HasTopMargin
    {
        get => GetValue(HasTopMarginProperty);
        set => SetValue(HasTopMarginProperty, value);
    }

    /// <summary>False for the fixed header of a page whose rest scrolls.</summary>
    public bool HasBottomMargin
    {
        get => GetValue(HasBottomMarginProperty);
        set => SetValue(HasBottomMarginProperty, value);
    }

    private double TopMargin => HasTopMargin ? PageTopMargin : 0;

    private double BottomMargin => HasBottomMargin ? PageBottomMargin : 0;

    protected override Size MeasureOverride(Size availableSize)
    {
        var available = double.IsInfinity(availableSize.Width) ? MaxBodyWidth + 2 * LargeMargin : availableSize.Width;
        var (_, width) = Place(available, HasSidePanel);
        var height = TopMargin + BottomMargin;
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
        var top = TopMargin;
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

    /// <summary>The least space between the body and the side panel: the side margin, but at most the regular one, so a wide window holds the full body next to the panel.</summary>
    internal static double SidePanelGap(double available) => Math.Min(SideMargin(available), RegularMargin);

    /// <summary>Where the body goes on a page that is <paramref name="available"/> wide.</summary>
    internal static (double Left, double Width) Place(double available, bool hasSidePanel)
    {
        var margin = SideMargin(available);
        var width = Math.Clamp(available - 2 * margin, 0, MaxBodyWidth);
        var left = Math.Max(0, (available - width) / 2);
        if (!hasSidePanel)
            return (left, width);

        var panelLeft = available - SidePanelWidth - SidePanelInset;
        var right = Math.Min(left + width, panelLeft - SidePanelGap(available));
        return (left, Math.Max(0, right - left));
    }
}
