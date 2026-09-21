namespace Borea.Storage.Tests.Files;

/// <summary>
/// A fact that runs on Windows when this process can write on a second local
/// volume, and is reported as skipped everywhere else.
/// </summary>
public sealed class SecondVolumeFactAttribute : FactAttribute
{
    public SecondVolumeFactAttribute(string reason)
    {
        if (!OperatingSystem.IsWindows())
            Skip = reason;
        else if (SecondVolume.Folder is null)
            Skip = "This machine has no second local volume this process can write.";
    }
}
