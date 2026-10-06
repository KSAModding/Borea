using System;
using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Borea.App.Views;

/// <summary>
/// A brush transition that counts the transparency of each colour in, so a fade from or to
/// nothing (a null or transparent brush) does not pass through a colour of its own. A null brush
/// counts as the other colour with alpha 0. Brushes that are not solid switch at the half-way point.
/// </summary>
public class BrushFade : InterpolatingTransitionBase<IBrush?>
{
    protected override IBrush? Interpolate(double progress, IBrush? fromValue, IBrush? toValue) =>
        Blend(progress, fromValue, toValue);

    internal static IBrush? Blend(double progress, IBrush? fromValue, IBrush? toValue)
    {
        if (progress >= 1)
            return toValue;
        if (ColourOf(fromValue, toValue) is not { } was || ColourOf(toValue, fromValue) is not { } toBe)
            return progress < 0.5 ? fromValue : toValue;

        var wasAlpha = was.A / 255.0;
        var toAlpha = toBe.A / 255.0;
        var alpha = wasAlpha + (toAlpha - wasAlpha) * progress;
        if (alpha <= 0)
            return new ImmutableSolidColorBrush(Color.FromArgb(0, was.R, was.G, was.B));

        return new ImmutableSolidColorBrush(Color.FromArgb(
            (byte)Math.Clamp(alpha * 255, 0, 255),
            (byte)Math.Clamp(Mix(was.R, wasAlpha, toBe.R, toAlpha, progress, alpha), 0, 255),
            (byte)Math.Clamp(Mix(was.G, wasAlpha, toBe.G, toAlpha, progress, alpha), 0, 255),
            (byte)Math.Clamp(Mix(was.B, wasAlpha, toBe.B, toAlpha, progress, alpha), 0, 255)));
    }

    private static Color? ColourOf(IBrush? brush, IBrush? other) => brush switch
    {
        ISolidColorBrush solid => solid.Color,
        null when other is ISolidColorBrush solid => Color.FromArgb(0, solid.Color.R, solid.Color.G, solid.Color.B),
        _ => null,
    };

    private static double Mix(byte was, double wasAlpha, byte toBe, double toAlpha, double progress, double alpha)
    {
        var carried = was * wasAlpha;
        var mixed = carried + (toBe * toAlpha - carried) * progress;
        return mixed / alpha;
    }
}
