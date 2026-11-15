using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Borea.Core.Logging;
using Borea.Storage.Launch;
using Borea.Storage.Logging;
using Borea.Storage.Paths;

namespace Borea.App.GameWatch;

/// <summary>
/// A small process of the same program that the App starts before it closes for the game. It waits for the game
/// to end and then opens the App again. It loads no window and takes no single-instance lock, so an App that the
/// player opens meanwhile starts as usual, and the start of this watcher then hands over to it.
/// </summary>
internal static class ReopenWatcher
{
    /// <summary>The first argument that runs the watcher. No help text lists it, because only the App starts it.</summary>
    internal const string CommandName = "reopen-after-game";

    private const string ProcessOption = "--process";

    /// <summary>The program that runs the watcher, or null for a build that runs through the dotnet host, which cannot start it.</summary>
    internal static string? ProgramPath(string? processPath)
        => string.IsNullOrEmpty(processPath) || Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? null
            : processPath;

    /// <param name="loaderProcessNames">The process names of the installed loaders, which count as a running game.</param>
    internal static IReadOnlyList<string> Arguments(IReadOnlyList<string> loaderProcessNames)
    {
        var arguments = new List<string> { CommandName };
        foreach (var name in loaderProcessNames)
        {
            arguments.Add(ProcessOption);
            arguments.Add(name);
        }

        return arguments;
    }

    /// <summary>
    /// Borea is a console program, so on Windows a process without a console window gets a hidden console, which
    /// the watcher keeps and a reopened App frees. Linux and macOS ignore the flag. No stream is redirected,
    /// because the process that starts the watcher ends right after and a pipe into an ended process would break.
    /// </summary>
    internal static ProcessStartInfo StartInfo(string programPath, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = programPath,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(programPath)),
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }

    /// <exception cref="Win32Exception">The program could not start.</exception>
    internal static void Start(string programPath, IReadOnlyList<string> loaderProcessNames)
        => StartProgram(programPath, Arguments(loaderProcessNames));

    private static void StartProgram(string programPath, IReadOnlyList<string> arguments)
    {
        using var started = Process.Start(StartInfo(programPath, arguments))
            ?? throw new InvalidOperationException($"No process was started for '{programPath}'.");
    }

    /// <summary>Runs the watcher in this process, with the arguments that follow <see cref="CommandName"/>.</summary>
    internal static int Run(string[] args)
    {
        var log = new FileBoreaLog(new GamePathProvider(gameDirectory: null), BoreaLogSource.App);
        if (ProgramPath(Environment.ProcessPath) is not { } program)
        {
            log.Write("Borea could not open again after the game closed, because the path of its program is not known.");
            return 1;
        }

        return RunAsync(
                args,
                names => new GameRunWatch(() => RunningProcesses.IsGameRunning(() => names), TimeProvider.System).WaitForEndAsync(),
                () => StartProgram(program, []),
                log)
            .GetAwaiter()
            .GetResult();
    }

    /// <param name="args">The arguments that follow <see cref="CommandName"/>.</param>
    /// <param name="waitForGameEnd">Gets the loader process names, and returns true when a game ran and ended, false when none appeared in time.</param>
    /// <param name="startApp">Starts the App without arguments.</param>
    internal static async Task<int> RunAsync(IReadOnlyList<string> args, Func<IReadOnlyList<string>, Task<bool>> waitForGameEnd, Action startApp, IBoreaLog log)
    {
        bool? ended = null;
        try
        {
            ended = await waitForGameEnd(LoaderProcessNames(args)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            log.Write("Borea could not watch the game, so it opens again now.", exception);
        }

        try
        {
            startApp();
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            log.Write("Borea could not open again after the game closed.", exception);
            return 1;
        }

        if (ended is null)
            return 0;

        log.Write(ended.Value
            ? "The game closed, so Borea opened again."
            : $"No game started within {GameRunWatch.AppearLimit.TotalSeconds:0} seconds, so Borea opened again.");
        return 0;
    }

    private static List<string> LoaderProcessNames(IReadOnlyList<string> args)
    {
        var names = new List<string>();
        for (var index = 0; index + 1 < args.Count; index++)
        {
            if (args[index] == ProcessOption)
                names.Add(args[++index]);
        }

        return names;
    }
}
