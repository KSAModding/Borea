using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace Borea.App.Views;

/// <summary>
/// Lays out the parts of a thin bar from the left, each as wide as its share of the panel, so the bars of a list start on one baseline and compare by length.
/// A part with no share takes no room and no gap, and every other part stays wide enough to see.
/// </summary>
public sealed class ShareBarPanel : Panel
{
    public static readonly AttachedProperty<double> ShareProperty =
        AvaloniaProperty.RegisterAttached<ShareBarPanel, Control, double>("Share");

    private const double Gap = 2;

    private const double MinPartWidth = 2;

    static ShareBarPanel()
    {
        AffectsParentArrange<ShareBarPanel>(ShareProperty);
    }

    public static double GetShare(Control part) => part.GetValue(ShareProperty);

    public static void SetShare(Control part, double value) => part.SetValue(ShareProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0.0;
        foreach (var part in Children)
        {
            part.Measure(availableSize);
            height = Math.Max(height, part.DesiredSize.Height);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var parts = Children.Count(part => GetShare(part) > 0);
        var room = Math.Max(0, finalSize.Width - Gap * (parts - 1));
        var left = 0.0;
        foreach (var part in Children)
        {
            var share = GetShare(part);
            if (share <= 0)
            {
                part.Arrange(default);
                continue;
            }

            var width = Math.Max(MinPartWidth, share * room);
            part.Arrange(new Rect(left, 0, width, finalSize.Height));
            left += width + Gap;
        }

        return finalSize;
    }
}
