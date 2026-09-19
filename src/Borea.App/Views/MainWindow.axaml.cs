using System;
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
        if (!e.Cancel && DataContext is MainViewModel viewModel && !viewModel.RequestClose(Close))
            e.Cancel = true;
    }
}
