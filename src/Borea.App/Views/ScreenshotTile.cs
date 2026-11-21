using System;
using Avalonia.Controls;
using Avalonia.Input;
using Borea.App.ViewModels;

namespace Borea.App.Views;

/// <summary>
/// A screenshot tile that opens the picture on a click and offers its other actions in a context menu. Few players open the menu and a
/// gallery shows many tiles, so the tile builds its menu only when it is asked for. The menu takes its texts each time it opens, because
/// the language can change while the tile stays.
/// </summary>
public sealed class ScreenshotTile : Button
{
    public ScreenshotTile()
    {
        ContextRequested += OpenMenu;
    }

    protected override Type StyleKeyOverride => typeof(Button);

    /// <summary>A recycled tile shows another screenshot, so its menu is built again for that one.</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        ContextFlyout = null;
    }

    // the flyout opens itself on each later request, once it is set, at the pointer only when a pointer asked for it, as here
    private void OpenMenu(object? sender, ContextRequestedEventArgs e)
    {
        if (ContextFlyout is not null || DataContext is not ScreenshotItem item || TopLevel.GetTopLevel(this)?.DataContext is not MainViewModel { Localization: var localization })
            return;

        var open = new MenuItem { Command = item.OpenCommand };
        var showInFolder = new MenuItem { Command = item.ShowInFolderCommand };
        var copyPath = new MenuItem { Command = item.CopyPathCommand };
        var menu = new MenuFlyout { Items = { open, showInFolder, copyPath } };
        menu.Opening += (_, _) =>
        {
            open.Header = localization.ScreenshotOpen;
            showInFolder.Header = localization.ScreenshotShowInFolder;
            copyPath.Header = localization.ScreenshotCopyPath;
        };
        ContextFlyout = menu;
        menu.ShowAt(this, showAtPointer: e.TryGetPosition(null, out _));
        e.Handled = true;
    }
}
