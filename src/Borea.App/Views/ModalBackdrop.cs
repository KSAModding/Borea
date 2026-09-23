using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Borea.App.Views;

/// <summary>
/// Closes a modal when a press lands on its dimmed backdrop, the way its close
/// button does, but not while that button is off.
/// </summary>
public static class ModalBackdrop
{
    public static readonly AttachedProperty<Button?> CloseButtonProperty =
        AvaloniaProperty.RegisterAttached<Panel, Button?>("CloseButton", typeof(ModalBackdrop));

    static ModalBackdrop() =>
        CloseButtonProperty.Changed.AddClassHandler<Panel>((backdrop, args) => Follow(backdrop, args.GetNewValue<Button?>()));

    public static Button? GetCloseButton(Panel backdrop) => backdrop.GetValue(CloseButtonProperty);

    public static void SetCloseButton(Panel backdrop, Button? value) => backdrop.SetValue(CloseButtonProperty, value);

    private static void Follow(Panel backdrop, Button? closeButton)
    {
        backdrop.PointerPressed -= OnPressed;
        if (closeButton is not null)
            backdrop.PointerPressed += OnPressed;
    }

    private static void OnPressed(object? sender, PointerPressedEventArgs args)
    {
        // a press inside the modal reaches the backdrop too, with a source of its own
        if (sender is not Panel backdrop || args.Source != backdrop || !args.GetCurrentPoint(backdrop).Properties.IsLeftButtonPressed)
            return;

        if (GetCloseButton(backdrop) is not { IsEffectivelyEnabled: true, Command: { } command } closeButton || !command.CanExecute(closeButton.CommandParameter))
            return;

        command.Execute(closeButton.CommandParameter);
        args.Handled = true;
    }
}
