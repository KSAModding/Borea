namespace Borea.Storage.Tests.Files;

/// <summary>
/// Keeps one folder on a local volume other than the one the temp folder is on,
/// which a junction across volumes needs. The folder stays between runs and
/// every test works inside it, so a run that is stopped leaves nothing at the
/// root of the volume.
/// </summary>
internal static class SecondVolume
{
    private const string FolderName = "BoreaTests";

    /// <summary>The folder on that volume, or null when this process can write on none.</summary>
    public static string? Folder { get; } = Find();

    private static string? Find()
    {
        var tempVolume = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()));

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed
                || !drive.IsReady
                || string.Equals(drive.Name, tempVolume, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var folder = Path.Combine(drive.Name, FolderName);
            if (CanWrite(folder))
                return folder;
        }

        return null;
    }

    private static bool CanWrite(string folder)
    {
        var probe = Path.Combine(folder, "probe");
        try
        {
            Directory.CreateDirectory(probe);
            Directory.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
