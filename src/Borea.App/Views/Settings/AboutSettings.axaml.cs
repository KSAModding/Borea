using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Borea.App.Views.Settings;

public partial class AboutSettings : UserControl
{
    public AboutSettings()
    {
        InitializeComponent();
    }

    private async void CopyDiagnostics(object? sender, RoutedEventArgs e) => await SettingsClipboard.CopyDiagnosticsAsync(this);

    private async void CopyBoreaFolder(object? sender, RoutedEventArgs e) => await SettingsClipboard.CopyBoreaFolderAsync(this);
}
