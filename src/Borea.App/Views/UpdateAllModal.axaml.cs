using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Borea.App.ViewModels;

namespace Borea.App.Views;

public partial class UpdateAllModal : UserControl
{
    public UpdateAllModal()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        WarningList.PropertyChanged += OnWarningListChanged;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // the focus leaves the link that opened the modal, or Enter would plan again behind it, and it stays off the main button, so that a warning is not one Enter away
        if (change.Property == IsVisibleProperty && IsVisible)
        {
            Scroller.Offset = default;
            Dispatcher.UIThread.Post(() => Scroller.Focus(), DispatcherPriority.Loaded);
        }
    }

    // a warning that a new plan brings stands above the view, and the button only changes its text
    private void OnWarningListChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ItemsControl.ItemsSourceProperty
            && e.NewValue is IEnumerable<UpdateWarningLine> lines
            && lines.Except(e.OldValue as IEnumerable<UpdateWarningLine> ?? []).Any())
            Scroller.Offset = default;
    }

    // the window hears Escape only while its focus sink has the focus
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            e.Handled = ModalBackdrop.TryClose(Backdrop);
    }
}
