using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.Core.Launch;

/// <summary>
/// Starts the game for one instance through its mod loader.
/// </summary>
public interface ILauncher
{
    /// <summary>
    /// Starts the loader with the instance handover, then the saved
    /// <see cref="Instance.LaunchArguments"/>, then <paramref name="arguments"/>
    /// for this launch only. For a Windows build in a Wine wrapper on macOS,
    /// it starts the launcher of the wrapper with the loader's file and gives
    /// the instance only through the variable of the handover, because the
    /// wrapper passes no arguments. It refuses launch arguments there, and it
    /// refuses a loader whose runtimeconfig.json names a .NET runtime that
    /// the prefix does not have.
    /// </summary>
    LaunchResult Launch(Instance instance, ModMetadata? loader, IReadOnlyList<string>? arguments = null);

    /// <summary>
    /// Starts the loader from <paramref name="loaderDirectory"/>, the folder of
    /// the loader version that the instance uses, and otherwise does what
    /// <see cref="Launch(Instance, ModMetadata?, IReadOnlyList{string}?)"/> does.
    /// Null starts the folder that the settings record for the loader. This
    /// default ignores the folder, so every launcher that starts a process or
    /// passes a launch on must implement it.
    /// </summary>
    LaunchResult Launch(Instance instance, ModMetadata? loader, string? loaderDirectory, IReadOnlyList<string>? arguments) =>
        Launch(instance, loader, arguments);

    /// <summary>
    /// Watches a launch that <see cref="Launch"/> just started until the game
    /// comes up, the loader stops, or the startup window ends. A loader that
    /// stops with a non-zero exit code in that time turns the result into
    /// <see cref="LaunchOutcome.ExitedEarly"/>, with its output and the mod it
    /// likely stopped on. For a launch through a Wine wrapper, only the game
    /// log decides, because the exit code of the wrapper says nothing about the
    /// game. A wrapper that stops before the log appears gives
    /// <see cref="LaunchOutcome.ExitedEarly"/> with <see cref="LaunchResult.Wine"/>
    /// set and no mod blamed. Any other result is returned as it is.
    /// </summary>
    Task<LaunchResult> WatchStartAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits until the process of a launch that <see cref="WatchStartAsync"/>
    /// reported as started ends, and says how it ended. Only a process that
    /// ends with an error can be <see cref="GameExitKind.Crashed"/>, and only
    /// with a crash log of its own run, so a quit and a restart that exit with
    /// 0 are <see cref="GameExitKind.Closed"/>, and so is the crash of a new
    /// process that a restart started.
    /// </summary>
    Task<GameExit> WatchExitAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a launch this launcher started for the instance is still
    /// running. A loader that restarts itself keeps its launch running while
    /// the new process runs. A launch from an earlier session and a game
    /// started by hand are not seen.
    /// </summary>
    bool IsRunning(Guid instanceId);
}
