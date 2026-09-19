using System;
using System.Runtime.InteropServices;

namespace Borea.App.SingleInstance;

/// <summary>
/// Shows a failed handover to a player who started Borea without a terminal. Avalonia is not
/// running in this process, so on Windows this is a native message box.
/// </summary>
internal static class HandoverFailureDialog
{
    private const uint MbIconWarning = 0x30;

    public static bool IsNeeded => OperatingSystem.IsWindows() && GetConsoleWindow() == IntPtr.Zero;

    public static void Show(string text)
    {
        if (OperatingSystem.IsWindows())
            MessageBoxW(IntPtr.Zero, text, "Borea", MbIconWarning);
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);
}
