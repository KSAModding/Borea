using Borea.Core.Game;

namespace Borea.Core.Launch;

/// <summary>
/// The game's own executable, which a launch without a mod loader starts.
/// </summary>
public static class GameExecutable
{
    /// <summary>
    /// The executable's file name in the game directory, or null when Borea
    /// does not know it for <paramref name="platform"/>. Only the Windows build
    /// has a known name, so another platform gets a message instead of a
    /// guessed file.
    /// </summary>
    public static string? FileName(OsPlatform platform) => platform switch
    {
        OsPlatform.Windows => "KSA.exe",
        _ => null,
    };

    /// <summary>
    /// The executable in the game directory, started there with no arguments
    /// and no added environment variables. The game sets its working directory
    /// to its own folder when it starts, so the game directory is the same
    /// value the game uses and does not depend on that.
    /// </summary>
    public static LaunchPlan Plan(string gameDirectory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
            throw new ArgumentException("The executable must be a plain file name.", nameof(fileName));

        if (string.IsNullOrWhiteSpace(gameDirectory))
            throw new ArgumentException("The game directory is required.", nameof(gameDirectory));

        return new LaunchPlan(
            Path.Combine(gameDirectory, fileName),
            Array.Empty<string>(),
            gameDirectory,
            new Dictionary<string, string>());
    }
}
