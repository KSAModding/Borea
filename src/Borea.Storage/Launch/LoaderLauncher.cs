using System.ComponentModel;
using System.Diagnostics;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Mods;
using Borea.Core.Paths;

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
    private readonly object _gate = new();
    private readonly Dictionary<Guid, IStartedProcess> _running = new();
    private readonly Dictionary<Guid, (DateTime StartedAtUtc, string LoaderName)> _starts = new();
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
        : this(pathProvider, starter, SharedProfileLauncher.CurrentPlatform(), () => DotnetHost.Find(Environment.GetEnvironmentVariable("PATH")), startupWindow)
    {
    }

    internal LoaderLauncher(IGamePathProvider pathProvider, IProcessStarter starter, OsPlatform? platform, Func<string?> findDotnet)
        : this(pathProvider, starter, platform, findDotnet, DefaultStartupWindow)
    {
    }

    internal LoaderLauncher(IGamePathProvider pathProvider, IProcessStarter starter, OsPlatform? platform, Func<string?> findDotnet, TimeSpan startupWindow)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _starter = starter ?? throw new ArgumentNullException(nameof(starter));
        _platform = platform;
        _findDotnet = findDotnet ?? throw new ArgumentNullException(nameof(findDotnet));
        if (startupWindow < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(startupWindow), "The startup window cannot be negative.");

        _startupWindow = startupWindow;
    }

    public LaunchResult Launch(Instance instance, ModMetadata? loader)
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

            var launch = loader.Provides?.Launch;
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
                Path.GetFullPath(_pathProvider.GetInstanceRoot(instance.InstanceId)));

            // StarMap lists only its Windows app host, and its assembly runs through dotnet
            // on every other system. This goes once the listing says how the loader starts there.
            if (_platform != OsPlatform.Windows && DotnetHost.AssemblyBeside(plan.Executable) is { } assembly)
            {
                var host = _findDotnet();
                if (host is null)
                {
                    return LaunchResult.Failed(
                        LaunchOutcome.DotnetMissing,
                        $"{loader.Name} runs through dotnet on this system, and Borea did not find dotnet on the PATH. Install the .NET runtime that {loader.Name} needs and try again.");
                }

                plan = plan.ThroughHost(host, assembly);
            }

            if (!File.Exists(plan.Executable))
            {
                return LaunchResult.Failed(
                    LaunchOutcome.LaunchTargetMissing,
                    $"'{plan.Executable}' is not there. Reinstall {loader.Name} or correct its directory in the settings.",
                    plan);
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
            _starts[instance.InstanceId] = (DateTime.UtcNow, loader.Name);

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
        (DateTime StartedAtUtc, string LoaderName) start;
        lock (_gate)
        {
            if (!_running.TryGetValue(instance.InstanceId, out process) || process.Id != started.ProcessId || !_starts.TryGetValue(instance.InstanceId, out start))
                return started;
        }

        var gameLog = _pathProvider.GetInstanceGameLogPath(instance.InstanceId);
        var exited = false;
        try
        {
            var watched = Stopwatch.StartNew();
            while (watched.Elapsed < _startupWindow)
            {
                var slice = Min(PollInterval, _startupWindow - watched.Elapsed);
                if (await process.WaitForExitAsync(slice, cancellationToken).ConfigureAwait(false))
                {
                    exited = true;
                    break;
                }

                // the game writes its log once it runs, so the loader got past loading the mods
                if (WrittenSince(gameLog, start.StartedAtUtc))
                    break;
            }

            exited = exited || process.HasExited;
        }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException)
        {
            // the handle was released by another call while this one watched
            return started;
        }

        var output = process.RecentOutput;
        var exitCode = exited ? process.ExitCode : null;
        WriteLaunchLog(_pathProvider.GetInstanceLaunchLogPath(instance.InstanceId), started.Plan, output, exitCode);

        if (exitCode is null or 0)
            return started.WithOutput(output);

        var blamed = Blame(instance, output);
        var message = blamed is null
            ? $"{start.LoaderName} stopped right after starting (exit code {exitCode}). The details show what it wrote."
            : $"{blamed.Metadata.Listing?.Name ?? blamed.ModId} {blamed.Version} stopped {start.LoaderName} from starting. It may not work with this version of KSA. Disable it and try again, or look for an update.";
        return LaunchResult.ExitedEarly(started.Plan, exitCode.Value, output, blamed?.ModId, message);
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

    private static bool WrittenSince(string path, DateTime sinceUtc)
    {
        try
        {
            return File.Exists(path) && File.GetLastWriteTimeUtc(path) >= sinceUtc;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// What the loader wrote while it was watched, next to the game's log, so
    /// the player can open it later. Each launch replaces the file.
    /// </summary>
    private static void WriteLaunchLog(string path, LaunchPlan plan, IReadOnlyList<string> output, int? exitCode)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var header = new[]
            {
                $"Launch at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
                $"Executable: {plan.Executable}",
                exitCode is null ? "The loader was still running when Borea stopped watching." : $"The loader exited with code {exitCode}.",
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

    /// <summary>Releases the handles. The processes keep running.</summary>
    public void Dispose()
    {
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
