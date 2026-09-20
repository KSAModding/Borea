using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Borea.App.Views;

/// <summary>
/// Keeps a row that is one big button from opening when the press landed on a
/// control inside it that is switched off. Avalonia looks for what was hit
/// among the enabled controls only, so a control that is off is skipped and the
/// press reaches the button behind it.
/// </summary>
public static class RowButton
{
    public static readonly AttachedProperty<bool> IgnoresDisabledProperty =
        AvaloniaProperty.RegisterAttached<Button, bool>("IgnoresDisabled", typeof(RowButton));

    static RowButton() =>
        IgnoresDisabledProperty.Changed.AddClassHandler<Button>((button, args) => Follow(button, args.GetNewValue<bool>()));

    public static bool GetIgnoresDisabled(Button button) => button.GetValue(IgnoresDisabledProperty);

    public static void SetIgnoresDisabled(Button button, bool value) => button.SetValue(IgnoresDisabledProperty, value);

    private static void Follow(Button button, bool ignores)
    {
        button.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
        if (ignores)
            button.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
    }

    private static void OnPressed(object? sender, PointerPressedEventArgs args)
    {
        if (sender is Button button && LandedOnSomethingOff(button, args.GetPosition(button)))
            args.Handled = true;
    }

    /// <summary>
    /// Whether the point is on a control that is off. The search runs over every
    /// visual under the point, because the one that is off is not the topmost
    /// when it holds a child.
    /// </summary>
    internal static bool LandedOnSomethingOff(Button button, Point point)
    {
        foreach (var hit in button.GetVisualsAt(point, visual => visual is not null))
        {
            for (var current = hit; current is not null && current != button; current = current.GetVisualParent())
            {
                if (current is InputElement { IsEnabled: false })
                    return true;
            }
        }

        return false;
    }
}
