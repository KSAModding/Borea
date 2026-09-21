using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Borea.App.ViewModels;

namespace Borea.App.Views.Settings;

/// <summary>
/// The copy buttons of the settings pages. The clipboard belongs to the top
/// level, so each page hands its own control over, and the text always comes
/// from the view model.
/// </summary>
internal static class SettingsClipboard
{
    internal static async Task CopyDiagnosticsAsync(Control page)
    {
        if (page.DataContext is not MainViewModel viewModel || TopLevel.GetTopLevel(page)?.Clipboard is not { } clipboard)
            return;

        await clipboard.SetTextAsync(viewModel.DiagnosticsWithLogText());
        viewModel.ReportDiagnosticsCopied();
    }

    /// <summary>The page shows the folder with "~", and the clipboard gets the real path.</summary>
    internal static async Task CopyBoreaFolderAsync(Control page)
    {
        if (page.DataContext is not MainViewModel { BoreaFolder: { } folder } viewModel || TopLevel.GetTopLevel(page)?.Clipboard is not { } clipboard)
            return;

        await clipboard.SetTextAsync(folder);
        viewModel.ReportFolderCopied();
    }
}
