using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Borea.App.Views.Settings;

public partial class HelpSettings : UserControl
{
    public HelpSettings()
    {
        InitializeComponent();
    }

    private async void CopyDiagnostics(object? sender, RoutedEventArgs e) => await SettingsClipboard.CopyDiagnosticsAsync(this);
}
