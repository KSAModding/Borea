using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Borea.App.ViewModels;

namespace Borea.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    // Folder pickers need the window, so they live here; the chosen path goes to the view model.
    private async void BrowseGameDirectory(object? sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is not null && DataContext is MainViewModel viewModel)
            viewModel.GameDirectoryInput = folder;
    }

    private async void BrowseLoaderDirectory(object? sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is not null && DataContext is MainViewModel viewModel)
            viewModel.LoaderDirectoryInput = folder;
    }

    // the clipboard belongs to the window too; the text itself comes from the view model
    private async void CopyDiagnostics(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || Clipboard is null)
            return;

        await Clipboard.SetTextAsync(viewModel.DiagnosticsText);
        viewModel.ReportDiagnosticsCopied();
    }

    // the page shows the folder with "~"; the clipboard gets the real path
    private async void CopyBoreaFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { BoreaFolder: { } folder } viewModel || Clipboard is null)
            return;

        await Clipboard.SetTextAsync(folder);
        viewModel.ReportDiagnosticsCopied();
    }

    private async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }
}
