using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Borea.App.ViewModels;

namespace Borea.App.Views.Pages;

public partial class InstancePage : UserControl
{
    public InstancePage()
    {
        InitializeComponent();
    }

    private async void CopyGameLog(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { GameLogText: { } text } viewModel || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;

        await clipboard.SetTextAsync(text);
        viewModel.ReportGameLogCopied();
    }
}
