using System.ComponentModel;
using Borea.Core.Game;
using Borea.Core.Launch;
using Borea.Core.Paths;
using Borea.Storage.Game;

namespace Borea.Storage.Launch;

/// <summary>
/// ISharedProfileLauncher over the configured game directory and a process
/// starter. It releases the process handle at once, because Borea writes
/// nothing to the shared profile and so has nothing to hold back while the
/// game runs. A start through a Wine wrapper is the exception: it stays in the
/// running launches until it exits, because the wrapper runs one game at a
/// time and a loader launch through it must see that it is busy.
/// </summary>
public sealed class SharedProfileLauncher : ISharedProfileLauncher
{
    private readonly IGamePathProvider _pathProvider;
    private readonly IProcessStarter _starter;
    private readonly OsPlatform? _platform;
    private readonly IWinePrefixProbe _wine;
    private readonly RunningLaunches _launches;

    public SharedProfileLauncher(IGamePathProvider pathProvider, IProcessStarter starter)
        : this(pathProvider, starter, CurrentPlatform())
    {
    }

    /// <param name="launches">The launches this launcher shares with the loader launcher.</param>
    public SharedProfileLauncher(IGamePathProvider pathProvider, IProcessStarter starter, RunningLaunches launches)
        : this(pathProvider, starter, CurrentPlatform(), new WinePrefixProbe(CurrentPlatform()), launches ?? throw new ArgumentNullException(nameof(launches)))
    {
    }

    /// <param name="platform">
    /// The platform whose executable name is used. Null is a system that is
    /// not Windows, Linux or macOS, where nothing is started.
    /// </param>
    public SharedProfileLauncher(IGamePathProvider pathProvider, IProcessStarter starter, OsPlatform? platform)
        : this(pathProvider, starter, platform, new WinePrefixProbe(platform))
    {
    }

    internal SharedProfileLauncher(IGamePathProvider pathProvider, IProcessStarter starter, OsPlatform? platform, IWinePrefixProbe wine, RunningLaunches? launches = null)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _starter = starter ?? throw new ArgumentNullException(nameof(starter));
        _platform = platform;
        _wine = wine ?? throw new ArgumentNullException(nameof(wine));
        _launches = launches ?? new RunningLaunches();
    }

    public SharedProfileLaunchResult Launch(IReadOnlyList<string>? arguments = null)
    {
        var gameDirectory = _pathProvider.GetGameDirectoryPath();
        var hostStart = WindowsBuildOnHost.Check(_platform, gameDirectory, _wine);
        if (hostStart.Refusal is { } refusal)
            return SharedProfileLaunchResult.Failed(SharedProfileLaunchOutcome.WindowsBuild, refusal, wine: hostStart.Wine);

        // The platform comes before the directory, because no directory setting can fix an unknown executable.
        var fileName = GamePlatform.Of(_platform, gameDirectory) is { } platform ? GameExecutable.FileName(platform) : null;
        if (fileName is null)
        {
            return SharedProfileLaunchResult.Failed(
                SharedProfileLaunchOutcome.UnknownExecutable,
                $"Borea does not know the name of the game's executable on {PlatformName(_platform)}, so it starts nothing.");
        }

        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            return SharedProfileLaunchResult.Failed(
                SharedProfileLaunchOutcome.NoGameDirectory,
                "Borea does not know where the game is installed. Set the game directory in the settings.");
        }

        var plan = GameExecutable.Plan(Path.GetFullPath(gameDirectory), fileName, arguments);

        if (!File.Exists(plan.Executable))
        {
            return SharedProfileLaunchResult.Failed(
                SharedProfileLaunchOutcome.ExecutableMissing,
                $"'{plan.Executable}' is not there. Reinstall the game or correct the game directory in the settings.",
                plan);
        }

        if (hostStart.Wrapped is { } wrapped)
            return LaunchThroughWrapper(plan, wrapped);

        var (result, process) = Start(plan);
        process?.Dispose();
        return result;
    }

    private SharedProfileLaunchResult LaunchThroughWrapper(LaunchPlan plan, WineInstall wrapped)
    {
        if (plan.Arguments.Count > 0)
        {
            return SharedProfileLaunchResult.Failed(
                SharedProfileLaunchOutcome.WrapperArguments,
                $"The Wine wrapper '{wrapped.Wrapper!.BundlePath}' passes no launch arguments to the game, so Borea starts nothing. Start it without launch arguments.",
                wine: wrapped);
        }

        if (_wine.ToWindowsPath(wrapped, plan.Executable) is null)
        {
            return SharedProfileLaunchResult.Failed(
                SharedProfileLaunchOutcome.PathOutsidePrefix,
                WindowsBuildOnHost.OutsidePrefix(wrapped, plan.Executable),
                plan,
                wine: wrapped,
                unmappedPath: plan.Executable);
        }

        plan = plan.ThroughWrapper(wrapped.Wrapper!.LauncherPath, new Dictionary<string, string>());
        lock (_launches.Gate)
        {
            // a second start of a running wrapper goes to the first one, so it would not start this game
            _launches.Forget(ended: true);
            if (_launches.RunsThrough(wrapped.Wrapper.BundlePath))
            {
                return SharedProfileLaunchResult.Failed(
                    SharedProfileLaunchOutcome.WrapperBusy,
                    $"A game that Borea started through the Wine wrapper '{wrapped.Wrapper.BundlePath}' is still running. The wrapper runs one game at a time, so close that game first.",
                    plan,
                    wine: wrapped);
            }

            var started = Start(plan);
            if (started.Process is { } running)
            {
                var key = Guid.NewGuid();
                _launches.Processes[key] = running;
                _launches.Wrappers[key] = wrapped;
            }

            return started.Result;
        }
    }

    private (SharedProfileLaunchResult Result, IStartedProcess? Process) Start(LaunchPlan plan)
    {
        IStartedProcess process;
        try
        {
            process = _starter.Start(plan);
        }
        catch (Win32Exception exception)
        {
            return (SharedProfileLaunchResult.Failed(
                SharedProfileLaunchOutcome.StartFailed,
                $"The system did not start '{plan.Executable}': {exception.Message}",
                plan), null);
        }

        return (SharedProfileLaunchResult.Success(
            plan,
            process.Id,
            "Started the game without a mod loader. It uses the shared profile, not a Borea instance."), process);
    }

    internal static OsPlatform? CurrentPlatform() =>
        OperatingSystem.IsWindows() ? OsPlatform.Windows
        : OperatingSystem.IsLinux() ? OsPlatform.Linux
        : OperatingSystem.IsMacOS() ? OsPlatform.MacOs
        : null;

    private static string PlatformName(OsPlatform? platform) => platform switch
    {
        OsPlatform.Windows => "Windows",
        OsPlatform.Linux => "Linux",
        OsPlatform.MacOs => "macOS",
        _ => "this operating system",
    };
}
