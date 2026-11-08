using System.Diagnostics;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;

namespace Borea.Storage.Launch;

/// <summary>
/// Looks for game, mod loader and Borea processes on the whole machine, whoever started
/// them. The process that asks never counts.
/// </summary>
public static class RunningProcesses
{
    private static readonly string[] BoreaProcessNames = ["Borea.App", "borea"];

    private static readonly string? GameProcessName = SharedProfileLauncher.CurrentPlatform() is { } platform && GameExecutable.FileName(platform) is { } game
        ? Path.GetFileNameWithoutExtension(game)
        : null;

    /// <summary>Whether a KSA process or a process of a known loader runs. This knows no installed loader, see <see cref="IsGameRunning(Func{IReadOnlyList{string}}, Func{string, bool})"/>.</summary>
    public static bool IsGameRunning() => IsGameRunning(() => [], IsOtherProcessRunning);

    /// <summary>
    /// Whether a KSA process or a process of an installed mod loader runs, because a loader runs the game inside its own process.
    /// The process names of the known loader profiles always count too, because Borea may not list such a loader as installed.
    /// </summary>
    /// <param name="loaderProcessNames">The process names of the installed loaders, without extension.</param>
    /// <param name="isProcessRunning">Whether another process has that name. The default asks the system.</param>
    public static bool IsGameRunning(Func<IReadOnlyList<string>> loaderProcessNames, Func<string, bool>? isProcessRunning = null)
    {
        ArgumentNullException.ThrowIfNull(loaderProcessNames);
        isProcessRunning ??= IsOtherProcessRunning;

        var loaders = loaderProcessNames();
        var names = new List<string>(loaders);
        if (GameProcessName is not null)
            names.Add(GameProcessName);

        names.AddRange(LoaderProfiles.Known.All.SelectMany(profile => profile.ProcessNames));

        return names.Distinct(StringComparer.OrdinalIgnoreCase).Any(isProcessRunning);
    }

    /// <summary>Whether a Borea App or command other than this process runs.</summary>
    public static bool IsOtherBoreaRunning() => BoreaProcessNames.Any(IsOtherProcessRunning);

    private static bool IsOtherProcessRunning(string name)
    {
        var processes = Process.GetProcessesByName(name);
        var found = processes.Any(process => process.Id != Environment.ProcessId);
        foreach (var process in processes)
            process.Dispose();

        return found;
    }
}
