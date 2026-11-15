using System;
using System.Runtime;
using Avalonia.Controls;
using Avalonia.Threading;
using Borea.App.SingleInstance;
using Borea.App.ViewModels;

namespace Borea.App.Views;

internal sealed class GameWindow(MainWindow window, WindowFront front) : IGameWindow
{
    public bool IsModalOpen => window.IsModalOpen();

    public bool IsMinimized => window.WindowState == WindowState.Minimized;

    /// <summary>
    /// The collection runs after the window is minimized, at background priority, so the window does not wait for it.
    /// An aggressive collection also gives the freed memory back to the system, which a normal collection keeps for later use.
    /// </summary>
    public void Minimize()
    {
        ListingImageView.Shelf.Clear();
        window.WindowState = WindowState.Minimized;
        Dispatcher.UIThread.Post(
            () =>
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            },
            DispatcherPriority.Background);
    }

    public void Restore() => front.BringToFront();
}
