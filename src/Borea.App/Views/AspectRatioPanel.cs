using System;
using Avalonia;
using Avalonia.Controls;

namespace Borea.App.Views;

/// <summary>
/// Lays out its children on top of each other at the full width it gets and at a height of that width times
/// <see cref="Ratio"/>, so a 16:9 area keeps its shape when the page gets wider or narrower.
/// </summary>
public sealed class AspectRatioPanel : Panel
{
    public static readonly StyledProperty<double> RatioProperty =
        AvaloniaProperty.Register<AspectRatioPanel, double>(nameof(Ratio), 9.0 / 16.0);

    static AspectRatioPanel() => AffectsMeasure<AspectRatioPanel>(RatioProperty);

    /// <summary>The height as a part of the width, 9/16 by default.</summary>
    public double Ratio
    {
        get => GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = SizeFor(availableSize.Width);
        foreach (var child in Children)
            child.Measure(size);

        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = SizeFor(finalSize.Width);
        foreach (var child in Children)
            child.Arrange(new Rect(size));

        return size;
    }

    // A panel without a width limit, such as one in a horizontal stack, gets no area, because it has no width to follow.
    private Size SizeFor(double width) =>
        double.IsFinite(width) && width > 0 ? new Size(width, width * Math.Max(Ratio, 0)) : default;
}
