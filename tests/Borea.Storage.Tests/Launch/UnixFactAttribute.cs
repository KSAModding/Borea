namespace Borea.Storage.Tests.Launch;

/// <summary>A fact that runs on Linux and macOS and is reported as skipped on Windows.</summary>
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute(string reason)
    {
        if (OperatingSystem.IsWindows())
            Skip = reason;
    }
}
