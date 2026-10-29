namespace Borea.Preview.Tests;

/// <summary>
/// A fact that reads files of a KSA install. It runs only when BOREA_PREVIEW_GAME_DIR names a
/// folder that holds Content/Core, and is reported as skipped everywhere else.
/// </summary>
public sealed class GameDirFactAttribute : FactAttribute
{
    public GameDirFactAttribute()
    {
        if (GameDir is null)
        {
            Skip = "Set BOREA_PREVIEW_GAME_DIR to a KSA install that holds Content/Core to run this test.";
        }
    }

    public static string? GameDir
    {
        get
        {
            var folder = Environment.GetEnvironmentVariable("BOREA_PREVIEW_GAME_DIR");
            return !string.IsNullOrEmpty(folder) && Directory.Exists(Path.Combine(folder, "Content", "Core")) ? folder : null;
        }
    }
}
