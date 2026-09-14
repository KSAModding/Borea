using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Borea.App.ViewModels;
using Avalonia.Controls;

namespace Borea.App.Views.Settings;

public partial class AboutSettings : UserControl
{
    public AboutSettings()
    {
        InitializeComponent();
    }

    // the clipboard belongs to the top level; the text itself comes from the view model
    private async void CopyDiagnostics(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;

        await clipboard.SetTextAsync(viewModel.DiagnosticsWithLogText());
        viewModel.ReportDiagnosticsCopied();
    }

    // the page shows the folder with "~"; the clipboard gets the real path
    private async void CopyBoreaFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { BoreaFolder: { } folder } viewModel || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;

        await clipboard.SetTextAsync(folder);
        viewModel.ReportFolderCopied();
    }
}
