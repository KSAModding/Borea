using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Borea.App.ViewModels;

namespace Borea.App.Views.Settings;

public partial class GeneralSettings : UserControl
{
    public GeneralSettings()
    {
        InitializeComponent();
    }

    private async void BrowseLibraryFolder(object? sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is not null && DataContext is MainViewModel viewModel)
            await viewModel.ChangeLibraryFolderCommand.ExecuteAsync(folder);
    }

    private async Task<string?> PickFolderAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return null;

        var title = (DataContext as MainViewModel)?.Localization.SettingsLibraryFolderPickerTitle;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false, Title = title });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }
}
