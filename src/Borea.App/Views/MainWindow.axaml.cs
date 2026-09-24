using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Borea.App.ViewModels;

namespace Borea.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // tunnelled, so a control under the pointer that handles the press cannot keep it from the window
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
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

    /// <summary>
    /// The back and forward buttons of a mouse (#492). Back acts only where the page
    /// shows the back arrow, and neither acts while a modal is open.
    /// </summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        var command = e.GetCurrentPoint(this).Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.XButton1Pressed when viewModel.CanGoBack => viewModel.GoBackCommand,
            PointerUpdateKind.XButton2Pressed => viewModel.GoForwardCommand,
            _ => null,
        };
        if (command is null || IsModalOpen() || !command.CanExecute(null))
            return;

        command.Execute(null);
        e.Handled = true;
    }

    private bool IsModalOpen() =>
        this.GetVisualDescendants().OfType<Border>().Any(border => border.Classes.Contains("modal") && border.IsEffectivelyVisible);
}
