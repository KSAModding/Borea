using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Borea.App.ViewModels;

namespace Borea.App.Views;

public partial class TasksDrawer : UserControl
{
    public TasksDrawer()
    {
        InitializeComponent();
        Scrim.PointerPressed += (_, _) => Close();
        KeyDown += OnKeyDown;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // the newest tasks are at the bottom, so an opened drawer shows the end of the list and takes the focus
        if (change.Property == IsVisibleProperty && IsVisible)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Scroller.ScrollToEnd();
                Scroller.Focus();
            }, DispatcherPriority.Loaded);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        Close();
        e.Handled = true;
    }

    // a close from inside the drawer gives the focus back to the rail item that opened it
    private void Close()
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        viewModel.CloseTasksCommand.Execute(null);
        TopLevel.GetTopLevel(this)?.FindControl<Button>("TasksButton")?.Focus();
    }
}
