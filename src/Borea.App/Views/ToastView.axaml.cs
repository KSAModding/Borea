using Avalonia;
using Avalonia.Controls;
using Borea.App.ViewModels;

namespace Borea.App.Views;

public partial class ToastView : UserControl
{
    public ToastView()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (DataContext is not ToastItem toast)
            return;

        if (change.Property == IsPointerOverProperty)
            toast.SetPointerOver(change.GetNewValue<bool>());
        else if (change.Property == IsKeyboardFocusWithinProperty)
            toast.SetFocusWithin(change.GetNewValue<bool>());
    }
}
