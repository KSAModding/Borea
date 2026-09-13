using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Borea.App.ViewModels;
using Avalonia.Controls;

namespace Borea.App.Views.Settings;

public partial class GameSettings : UserControl
{
    public GameSettings()
    {
        InitializeComponent();
    }

    // folder pickers need the top level; the chosen path goes to the view model
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

    private async Task<string?> PickFolderAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return null;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }
}
