using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Borea.App.Views;

/// <summary>Clips the child of a border with the same radius at every corner to the inner edge of its line, so a child background does not cover the line in the corners.</summary>
public static class RoundedClip
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Border, bool>("IsEnabled", typeof(RoundedClip));

    static RoundedClip()
    {
        IsEnabledProperty.Changed.AddClassHandler<Border>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(Border border) => border.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(Border border, bool value) => border.SetValue(IsEnabledProperty, value);

    /// <summary>The inner edge of the line in the coordinates of the child, where Border leaves the corner radius less half the thickness.</summary>
    internal static (Rect Rect, double RadiusX, double RadiusY) InnerEdge(Size border, Rect child, Thickness thickness, CornerRadius radius) =>
        (new Rect(border).Deflate(thickness).Translate(new Vector(-child.X, -child.Y)),
            Math.Max(0, radius.TopLeft - thickness.Left / 2),
            Math.Max(0, radius.TopLeft - thickness.Top / 2));

    private static void OnIsEnabledChanged(Border border, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.GetNewValue<bool>())
        {
            border.PropertyChanged += OnBorderPropertyChanged;
            Watch(border.Child);
            Update(border);
        }
        else
        {
            border.PropertyChanged -= OnBorderPropertyChanged;
            Unwatch(border.Child);
        }
    }

    private static void OnBorderPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Decorator.ChildProperty)
        {
            Unwatch(e.GetOldValue<Control?>());
            Watch(e.GetNewValue<Control?>());
        }
        else if (e.Property != Visual.BoundsProperty && e.Property != Border.BorderThicknessProperty && e.Property != Border.CornerRadiusProperty)
        {
            return;
        }

        Update((Border)sender!);
    }

    private static void OnChildPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.BoundsProperty && sender is Control { Parent: Border border })
            Update(border);
    }

    private static void Watch(Control? child)
    {
        if (child is not null)
            child.PropertyChanged += OnChildPropertyChanged;
    }

    private static void Unwatch(Control? child)
    {
        if (child is null)
            return;

        child.PropertyChanged -= OnChildPropertyChanged;
        child.Clip = null;
    }

    private static void Update(Border border)
    {
        if (border.Child is not { } child)
            return;

        var thickness = border.UseLayoutRounding
            ? LayoutHelper.RoundLayoutThickness(border.BorderThickness, LayoutHelper.GetLayoutScale(border))
            : border.BorderThickness;
        var (rect, radiusX, radiusY) = InnerEdge(border.Bounds.Size, child.Bounds, thickness, border.CornerRadius);
        if (child.Clip is RectangleGeometry clip && clip.Rect == rect && clip.RadiusX == radiusX && clip.RadiusY == radiusY)
            return;

        child.Clip = new RectangleGeometry(rect, radiusX, radiusY);
    }
}
