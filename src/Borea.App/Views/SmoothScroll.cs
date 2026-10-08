using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Borea.App.Views;

/// <summary>
/// Eases the mouse wheel of a <see cref="ScrollViewer"/> toward a target offset instead of
/// jumping one notch at a time. Turn it on per viewer with <see cref="IsEnabledProperty"/>.
/// </summary>
public static class SmoothScroll
{
    internal const double Notch = 50;
    internal const double ResponseSeconds = 0.075;
    private static readonly TimeSpan LongestFrame = TimeSpan.FromMilliseconds(100);
    private const double Rest = 0.1;
    internal static double Left(double seconds)
    {
        var t = 2 * seconds / ResponseSeconds;
        return (1 + t) * Math.Exp(-t);
    }

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("IsEnabled", typeof(SmoothScroll));

    private static readonly AttachedProperty<Run?> RunProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, Run?>("Run", typeof(SmoothScroll));

    static SmoothScroll() =>
        IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>((viewer, args) => Follow(viewer, args.GetNewValue<bool>()));

    public static bool GetIsEnabled(ScrollViewer viewer) => viewer.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(ScrollViewer viewer, bool value) => viewer.SetValue(IsEnabledProperty, value);

    public static void ScrollTo(ScrollViewer viewer, Vector offset) =>
        RunOf(viewer).GoTo(Clamp(viewer, offset));

    private static void Follow(ScrollViewer viewer, bool enabled)
    {
        viewer.RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheel);
        if (enabled)
            viewer.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        else if (viewer.GetValue(RunProperty) is { } run)
        {
            run.Stop();
            viewer.SetValue(RunProperty, null);
        }
    }

    private static Run RunOf(ScrollViewer viewer)
    {
        if (viewer.GetValue(RunProperty) is not { } run)
            viewer.SetValue(RunProperty, run = new Run(viewer));

        return run;
    }

    private static void OnWheel(object? sender, PointerWheelEventArgs args)
    {
        if (sender is not ScrollViewer viewer
            || args.Handled
            || TopLevel.GetTopLevel(viewer) is null
            || viewer.Content is ILogicalScrollable { IsLogicalScrollEnabled: true }
            || WheelBelongsToAnother(viewer, args.Source))
        {
            return;
        }

        var delta = ReadDelta(viewer, args);
        var run = RunOf(viewer);
        if (run.Nudge(new Vector(-delta.X * Notch, -delta.Y * Notch)))
            args.Handled = true;
        else
            args.Handled = !viewer.IsScrollChainingEnabled;
    }

    /// <summary>
    /// True when the wheel is not for this viewer: it is over a viewer inside it, or over a combo
    /// box that is focused or open, which uses the wheel to change or scroll its selection.
    /// </summary>
    private static bool WheelBelongsToAnother(ScrollViewer viewer, object? source)
    {
        for (var node = source as Visual; node is not null && node != viewer; node = ParentOf(node))
        {
            if (node is ScrollViewer)
                return true;

            if (node is ComboBox box && (box.IsDropDownOpen || box.IsFocused))
                return true;
        }

        return false;
    }

    private static Visual? ParentOf(Visual node) =>
        node.GetVisualParent() ?? (node as ILogical)?.LogicalParent as Visual;

    private static Vector ReadDelta(ScrollViewer viewer, PointerWheelEventArgs args)
    {
        var delta = args.Delta;
        if (args.KeyModifiers == KeyModifiers.Shift && delta.X == 0)
            return new Vector(delta.Y, delta.X);

        return viewer.FlowDirection == FlowDirection.RightToLeft ? delta.WithX(-delta.X) : delta;
    }

    private static Vector Clamp(ScrollViewer viewer, Vector offset)
    {
        var maximum = viewer.ScrollBarMaximum;
        return new Vector(Inside(offset.X, maximum.X), Inside(offset.Y, maximum.Y));
    }

    private static double Inside(double value, double maximum) =>
        double.IsNaN(value) || value <= 0 ? 0 : value >= maximum ? maximum : value;

    private sealed class Run(ScrollViewer viewer)
    {
        private readonly ScrollViewer _viewer = viewer;

        private Vector _target;

        private Vector _moved;

        private TopLevel? _topLevel;

        private TimeSpan? _lastFrame;

        private TimeSpan _since;

        private double _left = 1;

        private bool _running;

        public Vector Target => _target;

        public bool Nudge(Vector step)
        {
            var wasRunning = _running;
            if (!wasRunning)
                _target = _viewer.Offset;

            var next = Clamp(_viewer, _target + step);
            if (next == _target)
            {
                return wasRunning && step != default;
            }

            GoTo(next);
            return true;
        }

        public void GoTo(Vector target)
        {
            if (_viewer.IsEffectivelyVisible == false || TopLevel.GetTopLevel(_viewer) is not { } topLevel)
                return;

            _target = target;
            _moved = _viewer.Offset;
            if (!_running)
            {
                _running = true;
                _lastFrame = null;
                _since = TimeSpan.Zero;
                _left = 1;
                _topLevel = topLevel;
                topLevel.RequestAnimationFrame(OnFrame);
            }
        }

        public void Stop()
        {
            _running = false;
            _lastFrame = null;
            _topLevel = null;
            _target = _viewer.Offset;
        }


        private void OnFrame(TimeSpan now)
        {
            if (_topLevel is null)
                _topLevel = TopLevel.GetTopLevel(_viewer);

            var last = _lastFrame;
            var running = last is not null;
            if (running)
                _since += now - last!.Value > LongestFrame ? LongestFrame : now - last.Value;

            _lastFrame = now;
            var left = Left(_since.TotalSeconds);
            var share = running && _left > 0 ? 1 - left / _left : 0;
            _left = left;

            _target = Clamp(_viewer, _target);

            var offset = _viewer.Offset;
            var gap = (_target - offset).Length;
            if (_topLevel is null || (running && (offset - _moved).Length > gap * share + Rest))
            {
                Stop();
                return;
            }

            if (gap < Rest)
            {
                _moved = _target;
                _viewer.SetCurrentValue(ScrollViewer.OffsetProperty, _target);
                Stop();
                return;
            }

            _viewer.SetCurrentValue(ScrollViewer.OffsetProperty, offset + (_target - offset) * share);
            _moved = _viewer.Offset;
            _topLevel.RequestAnimationFrame(OnFrame);
        }
    }
}
