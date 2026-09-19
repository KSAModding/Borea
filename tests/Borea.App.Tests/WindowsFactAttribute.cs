namespace Borea.App.Tests;

/// <summary>A fact that runs on Windows and is reported as skipped on Linux and macOS.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute(string reason)
    {
        if (!OperatingSystem.IsWindows())
            Skip = reason;
    }
}
