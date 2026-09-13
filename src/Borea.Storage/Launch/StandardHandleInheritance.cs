using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Borea.Storage.Launch;

/// <summary>
/// Keeps Borea's standard handles out of a process it starts on Windows, where
/// Process.Start always creates the process with handle inheritance on. Linux and
/// macOS need nothing.
/// </summary>
/// <remarks>
/// The flags are process-wide, so Borea starts processes only through ProcessStarter.
/// </remarks>
internal static class StandardHandleInheritance
{
    private const int StandardInput = -10;
    private const int StandardOutput = -11;
    private const int StandardError = -12;
    private const uint HandleFlagInherit = 0x1;

    /// <summary>Clears the inherit flag of the standard handles until the result is disposed.</summary>
    public static IDisposable Suspend() =>
        OperatingSystem.IsWindows() ? new Suspension(ClearInheritFlags()) : new Suspension(Array.Empty<nint>());

    [SupportedOSPlatform("windows")]
    private static nint[] ClearInheritFlags()
    {
        var cleared = new List<nint>();

        foreach (var kind in new[] { StandardInput, StandardOutput, StandardError })
        {
            // Output and error are often the same handle, and a process without a console has none.
            var handle = GetStdHandle(kind);
            if (handle == 0 || handle == -1 || cleared.Contains(handle))
                continue;

            // A handle whose flags cannot be read or changed stays as it is.
            if (GetHandleInformation(handle, out var flags)
                && (flags & HandleFlagInherit) != 0
                && SetHandleInformation(handle, HandleFlagInherit, 0))
            {
                cleared.Add(handle);
            }
        }

        return cleared.ToArray();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetHandleInformation(nint hObject, out uint lpdwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(nint hObject, uint dwMask, uint dwFlags);

    private sealed class Suspension : IDisposable
    {
        private nint[]? _cleared;

        public Suspension(nint[] cleared)
        {
            _cleared = cleared;
        }

        public void Dispose()
        {
            var cleared = Interlocked.Exchange(ref _cleared, null);
            if (cleared is null || !OperatingSystem.IsWindows())
                return;

            foreach (var handle in cleared)
                SetHandleInformation(handle, HandleFlagInherit, HandleFlagInherit);
        }
    }
}
