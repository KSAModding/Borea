using Avalonia;
using Avalonia.Controls;

namespace Borea.App.SingleInstance;

/// <summary>
/// Brings a window to the front. A minimized window gets back the state it had before,
/// because setting Normal would also end a maximized state.
/// </summary>
internal sealed class WindowFront
{
    private readonly Window _window;
    private WindowState _restoreState;

    public WindowFront(Window window)
    {
        _window = window;
        _restoreState = window.WindowState == WindowState.Minimized ? WindowState.Normal : window.WindowState;
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty && e.GetNewValue<WindowState>() is var state and not WindowState.Minimized)
                _restoreState = state;
        };
    }

    public void BringToFront()
    {
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = _restoreState;

        _window.Activate();
    }
}
