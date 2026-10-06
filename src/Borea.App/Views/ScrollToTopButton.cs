using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Borea.App.Views;

/// <summary>
/// A button that scrolls <see cref="Target"/> back to the top. It
/// shows only while the target is scrolled down by more than the height of its
/// viewport.
/// </summary>
public sealed class ScrollToTopButton : Button
{
    public static readonly StyledProperty<ScrollViewer?> TargetProperty =
        AvaloniaProperty.Register<ScrollToTopButton, ScrollViewer?>(nameof(Target));

    public ScrollToTopButton()
    {
        Classes.Add("to-top");
        Update();
    }

    protected override Type StyleKeyOverride => typeof(Button);

    public ScrollViewer? Target
    {
        get => GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != TargetProperty)
            return;

        if (change.OldValue is ScrollViewer old)
            old.PropertyChanged -= OnTargetPropertyChanged;
        if (change.NewValue is ScrollViewer target)
            target.PropertyChanged += OnTargetPropertyChanged;
        Update();
    }

    protected override void OnClick()
    {
        base.OnClick();
        if (Target is not { } target)
            return;

        // the button hides once the target is at the top, and a hidden button loses the focus, so a keyboard user goes on from the top of the target
        var byKeyboard = IsFocused && PseudoClasses.Contains(":focus-visible");

        target.SetCurrentValue(ScrollViewer.OffsetProperty, target.Offset.WithY(0));
        if (byKeyboard)
            FocusManager.FindFirstFocusableElement(target)?.Focus(NavigationMethod.Tab);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Handled || Target?.Presenter is not { } presenter || TopLevel.GetTopLevel(this) is not { } root)
            return;

        // the button floats over the target but is not inside it, so the wheel goes to the presenter of the target and scrolls it as it would with the pointer on the list; that event already bubbles through the ancestors this one would reach
        var forwarded = new PointerWheelEventArgs(presenter, e.Pointer, root, e.GetPosition(root), e.Timestamp, e.Properties, e.KeyModifiers, e.Delta);
        presenter.RaiseEvent(forwarded);
        e.Handled = true;
    }

    private void OnTargetPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == ScrollViewer.OffsetProperty || args.Property == ScrollViewer.ViewportProperty)
            Update();
    }

    private void Update() =>
        SetCurrentValue(IsVisibleProperty, Target is { } target && target.Offset.Y > target.Viewport.Height);
}
