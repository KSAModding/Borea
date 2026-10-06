using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Borea.App.Views;

/// <summary>
/// Fades and slides a dropdown in when it opens: a <see cref="ComboBox"/> that sets
/// <see cref="IsEnabledProperty"/>, and every popup flyout set as <c>Button.Flyout</c> or as an
/// attached flyout. Only the opening is animated, so a menu always closes at once.
/// </summary>
public static class DropdownAnimation
{
    internal static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(120);
    internal const double Travel = 6;
    internal const double Shrink = 0.96;

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ComboBox, bool>("IsEnabled", typeof(DropdownAnimation));
    private static readonly AttachedProperty<int> MoveProperty =
        AvaloniaProperty.RegisterAttached<Control, int>("Move", typeof(DropdownAnimation));

    private static bool _registered;

    public static void Register()
    {
        if (_registered)
            return;

        _registered = true;
        IsEnabledProperty.Changed.AddClassHandler<ComboBox>((box, args) => OnIsEnabledChanged(box, args.GetNewValue<bool>()));

        // borea menus are <Button.Flyout>;
        Button.FlyoutProperty.Changed.AddClassHandler<Button>((_, args) =>
            Follow(args.GetOldValue<FlyoutBase?>(), args.GetNewValue<FlyoutBase?>()));
        FlyoutBase.AttachedFlyoutProperty.Changed.AddClassHandler<Control>((_, args) =>
            Follow(args.GetOldValue<FlyoutBase?>(), args.GetNewValue<FlyoutBase?>()));
    }

    public static bool GetIsEnabled(ComboBox box) => box.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(ComboBox box, bool value) => box.SetValue(IsEnabledProperty, value);

    private static void Follow(FlyoutBase? old, FlyoutBase? current)
    {
        if (old is PopupFlyoutBase gone)
            gone.Opened -= OnFlyoutOpened;

        if (current is PopupFlyoutBase flyout)
        {
            flyout.Opened -= OnFlyoutOpened;
            flyout.Opened += OnFlyoutOpened;
        }
    }

    private static void OnFlyoutOpened(object? sender, EventArgs e)
    {
        if (sender is PopupFlyoutBase flyout)
            FadeIn(flyout.Popup, flyout.Placement);
    }

    private static void OnIsEnabledChanged(ComboBox box, bool enabled)
    {
        box.DropDownOpened -= OnDropDownOpened;
        if (enabled)
            box.DropDownOpened += OnDropDownOpened;
    }

    private static void OnDropDownOpened(object? sender, EventArgs e)
    {
        if (sender is ComboBox box)
            FadeIn(PopupOf(box), PlacementMode.Bottom);
    }

    private static Popup? PopupOf(Control owner) => Popped(owner);

    private static Popup? Popped(Visual visual)
    {
        if (visual is Popup popup)
            return popup;

        foreach (var child in visual.GetVisualChildren())
        {
            if (Popped(child) is { } found)
                return found;
        }

        return null;
    }

    internal static (Vector Travel, RelativePoint Origin) Edge(PlacementMode placement) => placement switch
    {
        PlacementMode.Top or PlacementMode.TopEdgeAlignedLeft or PlacementMode.TopEdgeAlignedRight
            => (new Vector(0, -Travel), new RelativePoint(0.5, 1, RelativeUnit.Relative)),
        PlacementMode.Left or PlacementMode.LeftEdgeAlignedTop or PlacementMode.LeftEdgeAlignedBottom
            => (new Vector(-Travel, 0), new RelativePoint(1, 0.5, RelativeUnit.Relative)),
        PlacementMode.Right or PlacementMode.RightEdgeAlignedTop or PlacementMode.RightEdgeAlignedBottom
            => (new Vector(Travel, 0), new RelativePoint(0, 0.5, RelativeUnit.Relative)),
        _ => (new Vector(0, Travel), new RelativePoint(0.5, 0, RelativeUnit.Relative)),
    };

    private static void FadeIn(Popup? popup, PlacementMode placement)
    {
        if (popup?.Child is not { } panel || TopLevel.GetTopLevel(panel) is not { } topLevel)
            return;

        var (travel, origin) = Edge(placement);
        var shifts = new TranslateTransform { X = travel.X, Y = travel.Y };
        var grows = new ScaleTransform { ScaleX = Shrink, ScaleY = Shrink };
        var transforms = new TransformGroup();
        transforms.Children.Add(grows);
        transforms.Children.Add(shifts);
        panel.RenderTransformOrigin = origin;
        panel.RenderTransform = transforms;
        panel.Opacity = 0;

        var mine = panel.GetValue(MoveProperty) + 1;
        panel.SetValue(MoveProperty, mine);
        var started = TimeSpan.Zero;
        var first = true;

        void OnFrame(TimeSpan now)
        {
            if (panel.GetValue(MoveProperty) != mine)
                return;

            if (first)
            {
                first = false;
                started = now;
            }

            var progress = Math.Clamp((now - started) / Duration, 0, 1);
            var eased = Eased(progress);
            panel.Opacity = eased;
            shifts.X = travel.X * (1 - eased);
            shifts.Y = travel.Y * (1 - eased);
            grows.ScaleX = grows.ScaleY = Shrink + (1 - Shrink) * eased;

            if (progress < 1)
            {
                topLevel.RequestAnimationFrame(OnFrame);
                return;
            }

            panel.Opacity = 1;
            panel.RenderTransform = null;
            panel.RenderTransformOrigin = default;
        }

        topLevel.RequestAnimationFrame(OnFrame);
    }

    private static double Eased(double progress) => progress * progress * (3 - 2 * progress);
}
