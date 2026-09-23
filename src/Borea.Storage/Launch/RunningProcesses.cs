using System.Diagnostics;
using Borea.Core.Launch;

namespace Borea.Storage.Launch;

/// <summary>
/// Looks for game and Borea processes on the whole machine, whoever started
/// them. The process that asks never counts.
/// </summary>
public static class RunningProcesses
{
    private const string StarMapProcessName = "StarMap";
    private static readonly string[] BoreaProcessNames = ["Borea.App", "borea"];

    // StarMap's GameSurveyer.TryLoadCoreAndGame loads the game assembly into the StarMap process
    private static readonly string[] GameProcessNames = SharedProfileLauncher.CurrentPlatform() is { } platform && GameExecutable.FileName(platform) is { } game
        ? [Path.GetFileNameWithoutExtension(game), StarMapProcessName]
        : [StarMapProcessName];

    /// <summary>Whether a KSA or StarMap process runs.</summary>
    public static bool IsGameRunning() => IsRunning(GameProcessNames);

    /// <summary>Whether a Borea App or command other than this process runs.</summary>
    public static bool IsOtherBoreaRunning() => IsRunning(BoreaProcessNames);

    private static bool IsRunning(string[] names)
    {
        foreach (var name in names)
        {
            var processes = Process.GetProcessesByName(name);
            var found = processes.Any(process => process.Id != Environment.ProcessId);
            foreach (var process in processes)
                process.Dispose();

            if (found)
                return true;
        }

        return false;
    }
}
