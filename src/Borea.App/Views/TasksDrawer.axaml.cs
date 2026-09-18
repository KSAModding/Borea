using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Borea.App.ViewModels;

namespace Borea.App.Views;

public partial class TasksDrawer : UserControl
{
    private MainViewModel? _viewModel;

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

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelChanged;

        _viewModel = DataContext as MainViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // a drawer that just opened has no row in place before the next layout pass
        if (e.PropertyName == nameof(MainViewModel.TaskInView) && _viewModel?.TaskInView is { } task)
            Dispatcher.UIThread.Post(() => HistoryList.ContainerFromItem(task)?.BringIntoView(), DispatcherPriority.Loaded);
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
