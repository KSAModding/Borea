using Borea.Core.Game;
using Borea.Core.Launch;
using Borea.Storage.Launch;

namespace Borea.Storage.Game;

/// <summary>
/// Whether a folder is the KSA folder. StarMap appends KSA.dll to a game path that names a folder and looks no deeper
/// (LoaderConfig.TryLoadConfig), so a game folder one level above KSA starts no game.
/// </summary>
public static class GameFolder
{
    private const string GameAssemblyFileName = "KSA.dll";

    private const int SearchDepth = 2;

    /// <summary>
    /// Whether <paramref name="gameDirectory"/> is a folder that exists and holds no KSA.dll. A folder that is not set
    /// or not there is a different problem, so it gives false.
    /// </summary>
    public static bool LacksGame(string? gameDirectory) =>
        !string.IsNullOrWhiteSpace(gameDirectory)
        && Path.IsPathFullyQualified(gameDirectory)
        && Directory.Exists(gameDirectory)
        && !File.Exists(Path.Combine(gameDirectory, GameAssemblyFileName));

    /// <summary>
    /// The game in the one folder up to two levels below <paramref name="directory"/> that holds KSA.dll and the
    /// program file of its build on this system. Null when no folder or more than one does, or when its KSA.dll gives
    /// no version.
    /// </summary>
    public static DetectedGame? FindBelow(string directory) => FindBelow(directory, SharedProfileLauncher.CurrentPlatform());

    /// <param name="host">The system Borea runs on.</param>
    internal static DetectedGame? FindBelow(string directory, OsPlatform? host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var options = new EnumerationOptions { IgnoreInaccessible = true };
        string? found = null;
        IReadOnlyList<string> level = [Path.GetFullPath(directory)];
        try
        {
            for (var depth = 0; depth < SearchDepth; depth++)
            {
                level = level.SelectMany(parent => Directory.EnumerateDirectories(parent, "*", options)).ToList();
                foreach (var candidate in level.Where(candidate => HoldsGame(candidate, host)))
                {
                    if (found is not null)
                        return null;

                    found = candidate;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return found is not null && InstalledGameVersionProvider.Read(found) is { } version ? new DetectedGame(found, version) : null;
    }

    /// <summary>
    /// Whether <paramref name="directory"/> holds the program file of the game build that <paramref name="host"/> starts
    /// there, KSA.exe for the Windows build and KSA for the native Linux build.
    /// </summary>
    internal static bool HoldsProgram(string directory, OsPlatform? host) =>
        GamePlatform.Of(host, directory) is { } platform
        && GameExecutable.FileName(platform) is { } program
        && File.Exists(Path.Combine(directory, program));

    private static bool HoldsGame(string directory, OsPlatform? host) =>
        File.Exists(Path.Combine(directory, GameAssemblyFileName)) && HoldsProgram(directory, host);
}
