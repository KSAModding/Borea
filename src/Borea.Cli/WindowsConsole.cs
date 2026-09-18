using System.Runtime.InteropServices;

namespace Borea.Cli;

/// <summary>
/// The console of a Borea process on Windows. Linux and macOS have no console that
/// a program owns, so there every check is false and every change does nothing.
/// </summary>
public static class WindowsConsole
{
    /// <summary>
    /// True when this process is the only one attached to its console, which means that
    /// Windows created the console for it, for example after a double-click in Explorer.
    /// A process started from a terminal shares the console with the shell.
    /// </summary>
    public static bool IsOwnedAlone()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        // A buffer that is too small still returns the number of attached processes.
        var processIds = new uint[1];
        return GetConsoleProcessList(processIds, (uint)processIds.Length) == 1;
    }

    /// <summary>Detaches this process from its console. A console that nobody else uses closes.</summary>
    public static void Free()
    {
        if (OperatingSystem.IsWindows())
            FreeConsole();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleProcessList([Out] uint[] lpdwProcessList, uint dwProcessCount);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();
}
