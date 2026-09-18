using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Borea.App.ViewModels;

namespace Borea.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainViewModel viewModel)
            viewModel.WindowServices = new WindowServices(this);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || DataContext is not MainViewModel viewModel || !MustWait(viewModel))
            return;

        e.Cancel = true;
        if (!viewModel.IsClosing)
            _ = CloseAfterTasksAsync(viewModel);
    }

    // the process ends with the window, so the task history is saved first
    private static bool MustWait(MainViewModel viewModel)
        => viewModel.HasRunningInstalls || !viewModel.Tasks.WhenSavedAsync().IsCompleted;

    private async Task CloseAfterTasksAsync(MainViewModel viewModel)
    {
        do
        {
            await viewModel.StopInstallsAsync();
            await viewModel.Tasks.WhenSavedAsync();
        }
        while (MustWait(viewModel));

        Close();
    }
}
