using System.ComponentModel;
using System.Diagnostics;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Storage.Instances;

namespace Borea.Storage.Launch;

/// <summary>
/// ILauncher over the configured loader directories and a process starter.
/// It remembers every launch per instance until the process exits, so a
/// second launch of a running instance is refused.
/// </summary>
public sealed class LoaderLauncher : ILauncher, IDisposable
{
    private readonly IGamePathProvider _pathProvider;
    private readonly IProcessStarter _starter;
    private readonly OsPlatform? _platform;
    private readonly Func<string?> _findDotnet;
    private readonly object _gate;
    private readonly Dictionary<Guid, IStartedProcess> _running;
    private readonly Dictionary<Guid, (DateTime? GameLogAtLaunch, string LoaderName)> _starts;
    private readonly bool _ownsLaunches;
    private readonly TimeSpan _startupWindow;

    /// <summary>How long a launch is watched when the game does not write its log first.</summary>
    public static readonly TimeSpan DefaultStartupWindow = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public LoaderLauncher(IGamePathProvider pathProvider, IProcessStarter starter)
        : this(pathProvider, starter, DefaultStartupWindow)
    {
    }

    /// <param name="startupWindow">How long <see cref="WatchStartAsync"/> watches at most.</param>
    public LoaderLauncher(IGamePathProvider pathProvider, IProcessStarter starter, TimeSpan startupWindow)
        : this(pathProvider, starter, SharedProfileLauncher.CurrentPlatform(), () => DotnetHost.Find(SharedProfileLauncher.CurrentPlatform()), startupWindow)
    {
    }

    /// <param name="launches">The launches this launcher shares with others. Disposing the launcher keeps them.</param>
    public LoaderLauncher(IGamePathProvider pathProvider, IProcessStarter starter, RunningLaunches launches)
        : this(pathProvider, starter, SharedProfileLauncher.CurrentPlatform(), () => DotnetHost.Find(SharedProfileLauncher.CurrentPlatform()), DefaultStartupWindow, launches ?? throw new ArgumentNullException(nameof(launches)))
    {
    }

    internal LoaderLauncher(IGamePathProvider pathProvider, IProcessStarter starter, OsPlatform? platform, Func<string?> findDotnet)
        : this(pathProvider, starter, platform, findDotnet, DefaultStartupWindow)
    {
    }

    internal LoaderLauncher(IGamePathProvider pathProvider, IProcessStarter starter, OsPlatform? platform, Func<string?> findDotnet, TimeSpan startupWindow, RunningLaunches? launches = null)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _starter = starter ?? throw new ArgumentNullException(nameof(starter));
        _platform = platform;
        _findDotnet = findDotnet ?? throw new ArgumentNullException(nameof(findDotnet));
        if (startupWindow < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(startupWindow), "The startup window cannot be negative.");

        _startupWindow = startupWindow;
        _ownsLaunches = launches is null;
        launches ??= new RunningLaunches();
        _gate = launches.Gate;
        _running = launches.Processes;
        _starts = launches.Starts;
    }

    public LaunchResult Launch(Instance instance, ModMetadata? loader, IReadOnlyList<string>? arguments = null)
    {
        if (instance is null)
            throw new ArgumentNullException(nameof(instance));

        if (loader is null)
        {
            return LaunchResult.Failed(
                LaunchOutcome.NoLoader,
                "No mod loader is set for this launch. The game reads no instance path on its own, so install a loader first.");
        }

        if (loader.Type != ContentType.ModLoader)
            throw new ArgumentException("Only a mod loader can start the game.", nameof(loader));

        lock (_gate)
        {
            Forget(exited: true);

            if (_running.ContainsKey(instance.InstanceId))
            {
                return LaunchResult.Failed(
                    LaunchOutcome.AlreadyRunning,
                    $"Instance '{instance.Name}' is already running from a launch Borea started. Close the game first.");
            }

            // An entry for this platform replaces [provides].launch, with no fallback (RFC 0067).
            var entry = _platform is { } platform ? loader.Provides?.Platforms.GetValueOrDefault(platform) : null;
            if (entry?.UnknownKeys is [var unknownKey, ..])
            {
                return LaunchResult.Failed(
                    LaunchOutcome.UnknownPlatformKey,
                    $"The start entry for this system in the listing of {loader.Name} has the key '{unknownKey}', which Borea does not know. Borea starts nothing. Look for a Borea update.",
                    unknownName: unknownKey);
            }

            if (entry?.Runtime == LoaderRuntime.Unknown)
            {
                return LaunchResult.Failed(
                    LaunchOutcome.UnknownRuntime,
                    $"{loader.Name} runs through '{entry.RuntimeName}' on this system, which Borea does not know. Borea starts nothing. Look for a Borea update.",
                    unknownName: entry.RuntimeName);
            }

            var launch = entry?.Launch ?? loader.Provides?.Launch;
            if (launch is null)
            {
                return LaunchResult.Failed(
                    LaunchOutcome.NoLaunchTarget,
                    $"The listing of {loader.Name} does not say what to run, so Borea cannot start it.");
            }

            // The launcher uses the table of the listing its caller passes and
            // keeps no copy. The caller passes the live listing, because a
            // release file never carries the table and a stale copy could name
            // a flag the installed loader no longer reads.
            var handover = loader.Provides?.Instance;
            if (handover is null)
            {
                return LaunchResult.Failed(
                    LaunchOutcome.NoInstanceHandover,
                    $"The listing of {loader.Name} does not say how it takes an instance, so Borea cannot start one with it. The loader author can add a [provides.instance] table to the listing.");
            }

            var launchArguments = instance.LaunchArguments.Concat(arguments ?? Array.Empty<string>()).ToList();
            if (handover.FlagIn(launchArguments) is { } flag)
            {
                return LaunchResult.Failed(
                    LaunchOutcome.HandoverFlagInArguments,
                    $"{loader.Name} takes the instance folder after '{flag}', and Borea passes that on every launch. A second one could make {loader.Name} use another folder, so remove '{flag}' from the launch arguments.");
            }

            var loaderDirectory = _pathProvider.GetLoaderDirectoryPath(loader.ModId);
            if (loaderDirectory is null)
            {
                return LaunchResult.Failed(
                    LaunchOutcome.NoLoaderDirectory,
                    $"Borea does not know where {loader.Name} is installed. Set its directory in the settings.");
            }

            var plan = LaunchPlan.ForLoader(
                Path.GetFullPath(loaderDirectory),
                launch,
                handover,
                Path.GetFullPath(_pathProvider.GetInstanceRoot(instance.InstanceId)),
                launchArguments);

            if (!File.Exists(plan.Executable))
            {
                return LaunchResult.Failed(
                    LaunchOutcome.LaunchTargetMissing,
                    $"'{plan.Executable}' is not there. Reinstall {loader.Name} or correct its directory in the settings.",
                    plan);
            }

            if (entry?.Runtime == LoaderRuntime.Dotnet)
            {
                var host = _findDotnet();
                if (host is null)
                {
                    return LaunchResult.Failed(
                        LaunchOutcome.DotnetMissing,
                        $"{loader.Name} runs through dotnet on this system, and Borea did not find dotnet. Install the .NET runtime that {loader.Name} needs and try again.");
                }

                plan = plan.ThroughHost(host, plan.Executable);
            }

            IStartedProcess process;
            try
            {
                process = _starter.Start(plan);
            }
            catch (Win32Exception exception)
            {
                return LaunchResult.Failed(
                    LaunchOutcome.StartFailed,
                    $"The system did not start '{plan.Executable}': {exception.Message}",
                    plan);
            }

            _running[instance.InstanceId] = process;
            _starts[instance.InstanceId] = (LastWrite(_pathProvider.GetInstanceGameLogPath(instance.InstanceId)), loader.Name);

            return LaunchResult.Success(
                plan,
                process.Id,
                $"Started {loader.Name} for instance '{instance.Name}'.");
        }
    }

    public async Task<LaunchResult> WatchStartAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(started);
        if (!started.Started || started.Plan is null)
            return started;

        IStartedProcess? process;
        (DateTime? GameLogAtLaunch, string LoaderName) start;
        lock (_gate)
        {
            if (!_running.TryGetValue(instance.InstanceId, out process) || process.Id != started.ProcessId || !_starts.TryGetValue(instance.InstanceId, out start))
                return started;
        }

        var gameLog = _pathProvider.GetInstanceGameLogPath(instance.InstanceId);
        var exited = false;
        var gameStarted = false;
        try
        {
            var watched = Stopwatch.StartNew();
            while (watched.Elapsed < _startupWindow)
            {
                var slice = Min(PollInterval, _startupWindow - watched.Elapsed);
                if (!exited)
                    exited = await process.WaitForExitAsync(slice, cancellationToken).ConfigureAwait(false);
                else
                    await Task.Delay(slice, cancellationToken).ConfigureAwait(false);

                // the game writes its log once it runs, so the loader got past loading the mods
                if (WrittenSince(gameLog, start.GameLogAtLaunch))
                {
                    gameStarted = true;
                    break;
                }

                // a loader that exits with 0 may have restarted itself, so the log decides;
                // an error exit needs no more waiting
                if (exited && process.ExitCode is not 0)
                    break;
            }

            exited = exited || process.HasExited;
            gameStarted = gameStarted || WrittenSince(gameLog, start.GameLogAtLaunch);
        }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException)
        {
            // the handle was released by another call while this one watched
            return started;
        }

        var output = process.RecentOutput;
        var exitCode = exited ? process.ExitCode : null;
        WriteLaunchLog(_pathProvider.GetInstanceLaunchLogPath(instance.InstanceId), started.Plan, output, exitCode, _platform == OsPlatform.Windows);

        if (exitCode is null || (exitCode == 0 && gameStarted))
            return started.WithOutput(output);

        // StarMap also exits with 0 when it cannot start at all, for example without a game path
        if (exitCode == 0)
        {
            return LaunchResult.ExitedEarly(
                started.Plan,
                0,
                output,
                blamedModId: null,
                $"{start.LoaderName} stopped without starting the game. The details show what it wrote.");
        }

        var blamed = Blame(instance, output);
        var cause = blamed is null ? LoaderCrashCause.Unknown : LoaderCrashCause.ModAssembly;
        if (blamed is null && LoaderCrashReport.StoppedWhileLoadingMods(output))
        {
            cause = LoaderCrashCause.ModLoading;
            var loadOrder = await LoadOrderReader.ReadAsync(_pathProvider, instance.InstanceId, cancellationToken).ConfigureAwait(false);
            var likely = LoaderCrashReport.LikelyLoadingMod(output, loadOrder);
            blamed = likely is null ? null : instance.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, likely));
        }

        var message = (blamed, cause) switch
        {
            (null, _) => $"{start.LoaderName} stopped right after starting (exit code {exitCode}). The details show what it wrote.",
            (_, LoaderCrashCause.ModLoading) => $"{start.LoaderName} stopped while it loaded {blamed.Metadata.Listing?.Name ?? blamed.ModId} {blamed.Version}, so that mod is the likely cause. Disable it and try again, or look for an update.",
            _ => $"{blamed.Metadata.Listing?.Name ?? blamed.ModId} {blamed.Version} stopped {start.LoaderName} from starting. It may not work with this version of KSA. Disable it and try again, or look for an update.",
        };
        return LaunchResult.ExitedEarly(started.Plan, exitCode.Value, output, blamed?.ModId, message, cause);
    }

    /// <summary>
    /// The installed mod behind the first assembly the output names: a mod
    /// whose id is the assembly's name, or whose folder holds that assembly.
    /// </summary>
    private InstalledMod? Blame(Instance instance, IReadOnlyList<string> output)
    {
        var modsFolder = _pathProvider.GetInstanceModsFolder(instance.InstanceId);
        foreach (var assembly in LoaderCrashReport.AssemblyNames(output))
        {
            var byId = instance.Mods.FirstOrDefault(mod => string.Equals(mod.ModId, assembly, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
                return byId;

            foreach (var mod in instance.Mods)
            {
                var folder = Path.Combine(modsFolder, mod.ModId);
                if (Directory.Exists(folder) && Directory.EnumerateFiles(folder, assembly + ".dll", new EnumerationOptions { RecurseSubdirectories = true, MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true }).Any())
                    return mod;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a session log appeared or changed after the launch. The files'
    /// own times are compared, because the file system clock is coarser than
    /// DateTime.UtcNow and a fresh write can look older than the launch.
    /// </summary>
    private static bool WrittenSince(string gameLogPath, DateTime? atLaunch) =>
        LastWrite(gameLogPath) is { } now && (atLaunch is null || now > atLaunch);

    private static DateTime? LastWrite(string gameLogPath) => GameLogFiles.Find(gameLogPath)
        .Where(log => log.Kind != GameLogKind.Archive)
        .Select(log => (DateTime?)log.File.LastWriteTimeUtc)
        .Max();

    /// <summary>
    /// What the loader wrote while it was watched, next to the game's log, so
    /// the player can open it later. Each launch replaces the file.
    /// </summary>
    private static void WriteLaunchLog(string path, LaunchPlan plan, IReadOnlyList<string> output, int? exitCode, bool windows)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var header = new[]
            {
                $"Launch at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
                $"Executable: {plan.Executable}",
                exitCode is { } code ? $"The loader exited with code {LoaderExitCode.Describe(code, windows)}." : "The loader was still running when Borea stopped watching.",
                string.Empty,
            };
            File.WriteAllLines(path, header.Concat(output));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // the log is a convenience, a launch result does not depend on it
        }
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

    public bool IsRunning(Guid instanceId)
    {
        lock (_gate)
        {
            Forget(exited: true);
            return _running.ContainsKey(instanceId);
        }
    }

    /// <summary>Releases the handles, unless the launches are shared. The processes keep running.</summary>
    public void Dispose()
    {
        if (!_ownsLaunches)
            return;

        lock (_gate)
        {
            Forget(exited: false);
        }
    }

    /// <summary>Drops the exited launches, or every launch, and releases their handles.</summary>
    private void Forget(bool exited)
    {
        foreach (var (instanceId, process) in _running.ToArray())
        {
            if (exited && !process.HasExited)
                continue;

            _running.Remove(instanceId);
            _starts.Remove(instanceId);
            process.Dispose();
        }
    }
}
