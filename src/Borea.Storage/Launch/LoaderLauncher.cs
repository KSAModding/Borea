using System.ComponentModel;
using System.Diagnostics;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Logging;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Storage.Game;
using Borea.Storage.Instances;
using Borea.Storage.ModLoaders;

namespace Borea.Storage.Launch;

/// <summary>
/// ILauncher over the configured loader directories and a process starter.
/// It remembers every launch per instance until the process has ended, which
/// includes a restart that took over its streams, so a second launch of a
/// running instance is refused, and then until the next launch of the
/// instance, so its exit watch still finds it.
/// </summary>
public sealed class LoaderLauncher : ILauncher, IDisposable
{
    private readonly IGamePathProvider _pathProvider;
    private readonly IProcessStarter _starter;
    private readonly OsPlatform? _platform;
    private readonly Func<string?> _findDotnet;
    private readonly Func<bool> _isGameRunning;
    private readonly IWinePrefixProbe _wine;
    private readonly LoaderConfigurator _configurator;
    private readonly IInstanceGameResolver _games;
    private readonly RunningLaunches _launches;
    private readonly object _gate;
    private readonly Dictionary<Guid, IStartedProcess> _running;
    private readonly Dictionary<Guid, (DateTime? GameLogAtLaunch, DateTime? CrashLogAtLaunch, string LoaderName, string LoaderReleases)> _starts;
    private readonly Dictionary<Guid, (IStartedProcess Process, DateTime? CrashLogAtLaunch)> _ended;
    private readonly Dictionary<Guid, WineInstall> _wrappers;
    private readonly Dictionary<Guid, (int ProcessId, ModMetadata Loader, string LoaderDirectory, string GamePath)> _keptBuilds;
    private readonly IBoreaLog? _log;
    private readonly bool _ownsLaunches;
    private readonly TimeSpan _startupWindow;
    private readonly TimeSpan _crashLogWait;

    /// <summary>How long a launch is watched when the game does not write its log first.</summary>
    public static readonly TimeSpan DefaultStartupWindow = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How long an exit watch waits for the crash log of a process that ended
    /// with an error. The monitor process of the game writes it only after it
    /// saw the game end.
    /// </summary>
    public static readonly TimeSpan DefaultCrashLogWait = TimeSpan.FromSeconds(10);

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
    /// <param name="isGameRunning">Whether a KSA or StarMap process runs. Null looks for one.</param>
    /// <param name="games">The game folder of each instance. Null resolves it over <paramref name="pathProvider"/>.</param>
    /// <param name="log">Takes the problems that do not change a result. Null writes them nowhere.</param>
    public LoaderLauncher(IGamePathProvider pathProvider, IProcessStarter starter, RunningLaunches launches, Func<bool>? isGameRunning = null, IInstanceGameResolver? games = null, IBoreaLog? log = null)
        : this(pathProvider, starter, SharedProfileLauncher.CurrentPlatform(), () => DotnetHost.Find(SharedProfileLauncher.CurrentPlatform()), DefaultStartupWindow, launches ?? throw new ArgumentNullException(nameof(launches)), isGameRunning, games: games, log: log)
    {
    }

    internal LoaderLauncher(IGamePathProvider pathProvider, IProcessStarter starter, OsPlatform? platform, Func<string?> findDotnet)
        : this(pathProvider, starter, platform, findDotnet, DefaultStartupWindow)
    {
    }

    internal LoaderLauncher(IGamePathProvider pathProvider, IProcessStarter starter, OsPlatform? platform, Func<string?> findDotnet, TimeSpan startupWindow, RunningLaunches? launches = null, Func<bool>? isGameRunning = null, TimeSpan? crashLogWait = null, IWinePrefixProbe? wine = null, IInstanceGameResolver? games = null, IBoreaLog? log = null)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _starter = starter ?? throw new ArgumentNullException(nameof(starter));
        _platform = platform;
        _findDotnet = findDotnet ?? throw new ArgumentNullException(nameof(findDotnet));
        _isGameRunning = isGameRunning ?? RunningProcesses.IsGameRunning;
        _wine = wine ?? new WinePrefixProbe(platform);
        _configurator = new LoaderConfigurator(_wine);
        _games = games ?? new InstanceGameResolver(pathProvider, new InstalledGameVersionProvider(pathProvider));
        if (startupWindow < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(startupWindow), "The startup window cannot be negative.");

        _startupWindow = startupWindow;
        _crashLogWait = crashLogWait ?? DefaultCrashLogWait;
        _ownsLaunches = launches is null;
        _launches = launches ?? new RunningLaunches();
        _gate = _launches.Gate;
        _running = _launches.Processes;
        _starts = _launches.Starts;
        _ended = _launches.Ended;
        _wrappers = _launches.Wrappers;
        _keptBuilds = _launches.KeptBuilds;
        _log = log;
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
            _launches.Forget(ended: true, HasEnded);

            if (_running.ContainsKey(instance.InstanceId))
            {
                return LaunchResult.Failed(
                    LaunchOutcome.AlreadyRunning,
                    $"Instance '{instance.Name}' is already running from a launch Borea started. Close the game first.");
            }

            var game = _games.Resolve(instance);
            if (game is { Problem: not KeptBuildProblem.None, KeptBuild: { } missing })
            {
                return LaunchResult.Failed(
                    LaunchOutcome.GameBuildMissing,
                    $"Instance '{instance.Name}' runs on KSA {missing}, and '{_pathProvider.GetGameBuildFolder(missing)}' is gone or holds another build. Borea starts nothing, so the instance does not start on another build. To use the game folder of the settings instead, select \"Use the current game build\" in the instance menu of the desktop App, or remove GameBuild from the instance.toml of the instance.");
            }

            var gameDirectory = game.GameDirectory;
            var hostStart = WindowsBuildOnHost.Check(_platform, gameDirectory, _wine);
            if (hostStart.Refusal is { } refusal)
                return LaunchResult.Failed(LaunchOutcome.WindowsBuild, refusal, wine: hostStart.Wine);

            // a second start of a running wrapper goes to the first one without its environment
            var wrapped = hostStart.Wrapped;
            var bundle = wrapped?.Wrapper!.BundlePath;
            if (bundle is not null && _launches.RunsThrough(bundle))
            {
                return LaunchResult.Failed(
                    LaunchOutcome.WrapperBusy,
                    $"A game that Borea started through the Wine wrapper '{bundle}' is still running. The wrapper runs one game at a time, so close that game first.",
                    wine: wrapped);
            }

            // An entry for the platform of the game build replaces [provides].launch, with no fallback (RFC 0067).
            var entry = GamePlatform.Of(_platform, gameDirectory) is { } platform ? loader.Provides?.Platforms.GetValueOrDefault(platform) : null;
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

            if (wrapped is not null && handover.Variable is null)
            {
                return LaunchResult.Failed(
                    LaunchOutcome.WrapperNeedsVariable,
                    $"{loader.Name} takes the instance folder only after '{handover.Flag}', and the Wine wrapper '{bundle}' passes no arguments to it. Borea starts nothing. The loader author can add a variable to the [provides.instance] table of the listing.",
                    wine: wrapped);
            }

            var launchArguments = instance.LaunchArguments.Concat(arguments ?? Array.Empty<string>()).ToList();
            if (wrapped is not null && launchArguments.Count > 0)
            {
                return LaunchResult.Failed(
                    LaunchOutcome.WrapperArguments,
                    $"The Wine wrapper '{bundle}' passes no launch arguments to {loader.Name}, so Borea starts nothing. Remove the launch arguments of instance '{instance.Name}' to start it through the wrapper.",
                    wine: wrapped);
            }

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

            if (entry?.Runtime == LoaderRuntime.Dotnet && wrapped is not null)
            {
                return LaunchResult.Failed(
                    LaunchOutcome.WrapperRuntime,
                    $"The start entry for Windows in the listing of {loader.Name} runs through '{entry.RuntimeName}', and the Wine wrapper '{bundle}' starts only the file itself. Borea starts nothing.",
                    plan,
                    wine: wrapped);
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

            if (wrapped is not null)
            {
                // the loader runs in the prefix, so it reads the instance root in the form of the prefix
                var instanceRoot = plan.EnvironmentVariables[handover.Variable!];
                var windowsRoot = _wine.ToWindowsPath(wrapped, instanceRoot);
                var unmapped = _wine.ToWindowsPath(wrapped, plan.Executable) is null ? plan.Executable : windowsRoot is null ? instanceRoot : null;
                if (unmapped is not null)
                    return LaunchResult.Failed(LaunchOutcome.PathOutsidePrefix, WindowsBuildOnHost.OutsidePrefix(wrapped, unmapped), plan, wine: wrapped, unmappedPath: unmapped);

                if (WineDotnetRuntime.Missing(wrapped, plan.Executable) is { } runtime)
                {
                    return LaunchResult.Failed(
                        LaunchOutcome.WrapperRuntimeMissing,
                        $"{loader.Name} needs the .NET runtime {runtime.Version} for Windows x64, and the Wine wrapper '{bundle}' does not have it. Borea starts nothing. Download the .NET {runtime.Channel} runtime installer for Windows x64 from {runtime.DownloadPage} and install it inside the wrapper, in Sikarugir through its Install Software action. Then start the game again.",
                        plan,
                        wine: wrapped,
                        missingRuntime: runtime);
                }

                plan = plan.ThroughWrapper(wrapped.Wrapper!.LauncherPath, new Dictionary<string, string> { [handover.Variable!] = windowsRoot! });
            }

            if (UseGameFolder(instance, loader, game, Path.GetFullPath(loaderDirectory), plan, out var gamePath) is { } refused)
                return refused;

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

            var gameLog = _pathProvider.GetInstanceGameLogPath(instance.InstanceId);
            _running[instance.InstanceId] = process;
            _starts[instance.InstanceId] = (LastWrite(gameLog), LastCrashLogWrite(gameLog), loader.Name, LoaderCrashReport.ReleasesPage(loader));
            _ended.Remove(instance.InstanceId);
            if (wrapped is not null)
                _wrappers[instance.InstanceId] = wrapped;
            if (game.KeptBuild is not null && gamePath is not null)
                _keptBuilds[instance.InstanceId] = (process.Id, loader, Path.GetFullPath(loaderDirectory), gamePath);
            else
                _keptBuilds.Remove(instance.InstanceId);

            return LaunchResult.Success(
                plan,
                process.Id,
                $"Started {loader.Name} for instance '{instance.Name}'.");
        }
    }

    /// <summary>
    /// Writes the game folder of the instance to the loader's configuration
    /// when it holds another value, or says why the start cannot go on. The
    /// loader reads the file again when it restarts a running game, so the file
    /// does not change under a running game.
    /// </summary>
    /// <param name="gamePath">The game path that the file names for the start, or null when Borea does not set it.</param>
    private LaunchResult? UseGameFolder(Instance instance, ModMetadata loader, InstanceGame game, string loaderDirectory, LaunchPlan plan, out string? gamePath)
    {
        gamePath = null;
        if (!LoaderConfigurator.TakesGamePath(loader))
        {
            return game.KeptBuild is { } kept
                ? LaunchResult.Failed(
                    LaunchOutcome.LoaderTakesNoGamePath,
                    $"The listing of {loader.Name} names no game path that Borea can write, so Borea cannot start instance '{instance.Name}' on KSA {kept}. Borea starts nothing. The loader author can add a [provides.configure] table to the listing.",
                    plan)
                : null;
        }

        // a game folder that is not absolute gives the loader no usable value, so Borea writes nothing, the same as the Wine refresh
        if (game.GameDirectory is not { } gameDirectory || !Path.IsPathFullyQualified(gameDirectory))
            return null;

        string wanted;
        try
        {
            wanted = _configurator.GamePathValue(gameDirectory);
        }
        catch (InvalidOperationException) when (_wine.Find(Path.GetFullPath(gameDirectory)) is { } prefix)
        {
            var unmapped = Path.GetFullPath(gameDirectory);
            return LaunchResult.Failed(LaunchOutcome.PathOutsidePrefix, WindowsBuildOnHost.OutsidePrefix(prefix, unmapped), plan, wine: prefix, unmappedPath: unmapped);
        }

        string? held;
        try
        {
            held = LoaderConfigurator.ReadGamePath(loader, loaderDirectory);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            var file = LoaderConfigurator.GamePathFile(loader, loaderDirectory);
            return LaunchResult.Failed(
                LaunchOutcome.LoaderConfigurationUnreadable,
                $"Borea cannot read the game folder from '{file}', so Borea starts nothing, and {loader.Name} cannot start with this file either. Repair or delete the file. Without it, Borea writes a new one at the next start. {exception.Message}",
                plan,
                configurationFile: file);
        }

        if (SameGamePath(held, wanted))
        {
            gamePath = wanted;
            return null;
        }

        // a loader starts no game from a missing or empty game path, so only a value can belong to a running game
        if (!string.IsNullOrWhiteSpace(held) && _isGameRunning())
        {
            return LaunchResult.Failed(
                LaunchOutcome.OtherBuildRunning,
                $"A game is running, and {loader.Name} reads the game folder '{held}' for it. If Borea wrote the game folder of instance '{instance.Name}' now, a restart of the running game would load the wrong build. Borea starts nothing. Close the game first.",
                plan);
        }

        LoaderConfigurator.WriteGamePath(loader, loaderDirectory, wanted);
        gamePath = wanted;
        return null;
    }

    /// <summary>
    /// Writes the game folder of the settings back to the loader's game path
    /// after a game on a kept build ended, so a Borea that does not write the
    /// file before each start does not start the kept build or a folder that
    /// is gone. The file keeps a value that another start or the player wrote
    /// since, and a game that still runs keeps it too, because a restart of
    /// that game reads it. It holds the gate like a launch, so a start in
    /// between does not lose the game path it wrote.
    /// </summary>
    private void RestoreGameFolder(Instance instance, int processId)
    {
        if (_pathProvider.GetGameDirectoryPath() is not { } gameDirectory || !Path.IsPathFullyQualified(gameDirectory))
            return;

        lock (_gate)
        {
            if (!_keptBuilds.TryGetValue(instance.InstanceId, out var kept) || kept.ProcessId != processId)
                return;

            try
            {
                if (!SameGamePath(LoaderConfigurator.ReadGamePath(kept.Loader, kept.LoaderDirectory), kept.GamePath) || _isGameRunning())
                    return;

                LoaderConfigurator.WriteGamePath(kept.Loader, kept.LoaderDirectory, _configurator.GamePathValue(gameDirectory));
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                _log?.Write($"The game of instance {instance.InstanceId} on a kept build ended, and Borea could not write the game folder '{gameDirectory}' back to the configuration of {kept.Loader.Name} in '{kept.LoaderDirectory}'.", exception);
            }
        }
    }

    internal static bool SameGamePath(string? held, string wanted) =>
        held is not null && string.Equals(GameFolderOf(held), GameFolderOf(wanted), PathComparison);

    // StarMap accepts the path of KSA.dll as well as the game folder, and a
    // Windows path of a Wine prefix is not a full path on the host
    private static string GameFolderOf(string path)
    {
        var folder = (Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : path.Replace('/', '\\')).TrimEnd('/', '\\');
        var name = folder[(folder.LastIndexOfAny(['/', '\\']) + 1)..];
        return string.Equals(name, "KSA.dll", PathComparison) ? folder[..^name.Length].TrimEnd('/', '\\') : folder;
    }

    private static StringComparison PathComparison => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    public async Task<LaunchResult> WatchStartAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(started);
        if (!started.Started || started.Plan is null)
            return started;

        IStartedProcess? process;
        (DateTime? GameLogAtLaunch, DateTime? CrashLogAtLaunch, string LoaderName, string LoaderReleases) start;
        WineInstall? wrapped;
        lock (_gate)
        {
            if (!_running.TryGetValue(instance.InstanceId, out process) || process.Id != started.ProcessId || !_starts.TryGetValue(instance.InstanceId, out start))
                return started;

            wrapped = _wrappers.GetValueOrDefault(instance.InstanceId);
        }

        var gameLog = _pathProvider.GetInstanceGameLogPath(instance.InstanceId);
        var ended = false;
        var gameStarted = false;
        int? exitCode;
        try
        {
            var watched = Stopwatch.StartNew();
            while (watched.Elapsed < _startupWindow)
            {
                var slice = Min(PollInterval, _startupWindow - watched.Elapsed);
                if (!ended)
                    ended = await process.WaitForExitAsync(slice, cancellationToken).ConfigureAwait(false);
                else
                    await Task.Delay(slice, cancellationToken).ConfigureAwait(false);

                // the game writes its log once it runs, so the loader got past loading the mods
                if (WrittenSince(gameLog, start.GameLogAtLaunch))
                {
                    gameStarted = true;
                    break;
                }

                // a loader that exits with 0 may have restarted itself, so the log decides;
                // an error exit needs no more waiting, unless the launcher of a wrapper made it
                if (ended && process.ExitCode is not 0 && wrapped is null)
                    break;
            }

            exitCode = process.HasExited ? process.ExitCode : null;

            // the streams of a process that exited alone close a moment later
            if (exitCode is not null && !ended)
                ended = await process.WaitForExitAsync(PollInterval, cancellationToken).ConfigureAwait(false);

            gameStarted = gameStarted || WrittenSince(gameLog, start.GameLogAtLaunch);
        }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException)
        {
            // the handle was released by another call while this one watched
            return started;
        }

        // a loader that exits with 0 and leaves a game process that holds its streams has restarted itself
        var restarted = exitCode == 0 && !ended && _isGameRunning();
        var output = process.RecentOutput;
        WriteLaunchLog(_pathProvider.GetInstanceLaunchLogPath(instance.InstanceId), started.Plan, output, exitCode, restarted, _platform == OsPlatform.Windows);

        // the exit code of the launcher of a wrapper says nothing about the game, so only the game log decides
        if (wrapped is not null)
        {
            if (exitCode is null || gameStarted)
                return started.WithOutput(output);

            RestoreGameFolder(instance, process.Id);
            return LaunchResult.ExitedEarly(
                started.Plan,
                exitCode.Value,
                output,
                blamedModId: null,
                $"The Wine wrapper '{wrapped.Wrapper!.BundlePath}' stopped before {start.LoaderName} started the game. The details show what it wrote.",
                wine: wrapped);
        }

        if (exitCode is null || (exitCode == 0 && (gameStarted || restarted)))
            return started.WithOutput(output);

        // nothing watches the exit of a start that stopped early
        RestoreGameFolder(instance, process.Id);

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

        var modsFolder = _pathProvider.GetInstanceModsFolder(instance.InstanceId);
        var gameDirectory = _games.Resolve(instance).GameDirectory;
        // only a missing game assembly shows a mod or a loader made for another KSA build
        var missing = LoaderCrashReport.MissingAssemblyNames(output).Where(LoaderCrashReport.IsGameAssembly).ToHashSet(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<LoadOrderMod>? loadOrder = null;
        var blamed = ModBlame.Find(instance, modsFolder, LoaderCrashReport.AssemblyNames(output));
        var cause = blamed is null ? LoaderCrashCause.Unknown : LoaderCrashCause.ModAssembly;
        if (blamed is null && LoaderCrashReport.StoppedWhileLoadingMods(output))
        {
            cause = LoaderCrashCause.ModLoading;
            loadOrder = await LoadOrderReader.ReadAsync(_pathProvider, instance.InstanceId, cancellationToken).ConfigureAwait(false);
            var likely = LoaderCrashReport.LikelyLoadingMod(output, loadOrder);
            blamed = (likely is null ? null : instance.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, likely)))
                ?? (missing.Count > 0 ? AssemblyReferences.Mods(instance, modsFolder, loadOrder, missing.Contains).FirstOrDefault() : null);
        }
        else if (blamed is null && missing.Count > 0)
        {
            // GameSurveyer.TryLoadCoreAndGame loads ./StarMap.Core.dll before any
            // mod, so a missing assembly that a DLL in the loader folder needs
            // stopped it first. Subfolders do not count, they can hold mods.
            if (AssemblyReferences.Referencing(started.Plan.WorkingDirectory, missing, recurse: false))
            {
                cause = LoaderCrashCause.GameAssembly;
            }
            else
            {
                loadOrder = await LoadOrderReader.ReadAsync(_pathProvider, instance.InstanceId, cancellationToken).ConfigureAwait(false);
                blamed = AssemblyReferences.Mods(instance, modsFolder, loadOrder, missing.Contains).FirstOrDefault();
                cause = blamed is null ? LoaderCrashCause.Unknown : LoaderCrashCause.ModAssembly;
            }
        }

        if (blamed is null && cause == LoaderCrashCause.Unknown && LoaderCrashReport.StoppedWhileLoaderStarted(output) && LoaderCrashReport.NamesGameAssembly(output))
            cause = LoaderCrashCause.GameAssembly;

        IReadOnlyList<InstalledMod> others = [];
        if (blamed is not null && missing.Count > 0)
        {
            loadOrder ??= await LoadOrderReader.ReadAsync(_pathProvider, instance.InstanceId, cancellationToken).ConfigureAwait(false);
            var notInGame = GameAssemblyNotInGame(gameDirectory, modsFolder);
            others = AssemblyReferences.Mods(instance, modsFolder, loadOrder, name => missing.Contains(name) || notInGame(name)).Where(mod => mod != blamed).ToList();
        }

        var build = cause == LoaderCrashCause.GameAssembly ? InstalledGameVersionProvider.Read(gameDirectory)?.RawVersion : null;
        var message = (blamed, cause) switch
        {
            (null, LoaderCrashCause.GameAssembly) when build is not null => $"{start.LoaderName} was made for another KSA build than the installed {build} and cannot load the game. When a {start.LoaderName} release for {build} is out, install that release. If {build} is older than the build that {start.LoaderName} was made for, update KSA instead. {start.LoaderName} releases: {start.LoaderReleases}",
            (null, _) => $"{start.LoaderName} stopped right after starting (exit code {exitCode}). The details show what it wrote.",
            (_, LoaderCrashCause.ModLoading) => $"{start.LoaderName} stopped while it loaded {blamed.Metadata.Listing?.Name ?? blamed.ModId} {blamed.Version}, so that mod is the likely cause. Disable it and try again, or look for an update.",
            _ => $"{blamed.Metadata.Listing?.Name ?? blamed.ModId} {blamed.Version} stopped {start.LoaderName} from starting. It may not work with this version of KSA. Disable it and try again, or look for an update.",
        };
        if (blamed is not null && others.Count > 0)
            message += $" These mods need parts of KSA that this version does not have, so they will likely fail too: {string.Join(", ", others.Select(mod => mod.Metadata.Listing?.Name ?? mod.ModId))}.";

        return LaunchResult.ExitedEarly(started.Plan, exitCode.Value, output, blamed?.ModId, message, cause, otherBlamedModIds: others.Select(mod => mod.ModId).ToList());
    }

    /// <summary>
    /// Whether an assembly name is one of the game that neither the game folder
    /// nor a mod folder holds, which a mod made for another KSA build references.
    /// Always false when the game folder holds no KSA.dll.
    /// </summary>
    private static Func<string, bool> GameAssemblyNotInGame(string? gameDirectory, string modsFolder)
    {
        var held = AssemblyReferences.Held(gameDirectory ?? string.Empty, recurse: false);
        if (!held.Contains("KSA"))
            return _ => false;

        held.UnionWith(AssemblyReferences.Held(modsFolder, recurse: true));
        return name => LoaderCrashReport.IsGameAssembly(name) && !held.Contains(name);
    }

    public async Task<GameExit> WatchExitAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(started);
        if (!started.Started || started.ProcessId is not { } processId)
            return GameExit.Unknown;

        IStartedProcess process;
        DateTime? crashLogAtLaunch;
        lock (_gate)
        {
            if (_running.TryGetValue(instance.InstanceId, out var running) && running.Id == processId && _starts.TryGetValue(instance.InstanceId, out var start))
                (process, crashLogAtLaunch) = (running, start.CrashLogAtLaunch);
            else if (_ended.TryGetValue(instance.InstanceId, out var ended) && ended.Process.Id == processId)
                (process, crashLogAtLaunch) = ended;
            else
                return GameExit.Unknown;
        }

        int? endedWith;
        try
        {
            await process.WaitForExitAsync(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            endedWith = process.ExitCode;
        }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException)
        {
            // a released handle keeps the exit code of a process that had ended
            endedWith = process.ExitCode;
        }

        if (endedWith is not { } exitCode)
            return GameExit.Unknown;

        RestoreGameFolder(instance, processId);

        // a quit and a restart exit with 0, and neither is a crash
        var output = process.RecentOutput;
        var crash = exitCode == 0 ? null : await FindCrashAsync(instance, processId, crashLogAtLaunch, cancellationToken).ConfigureAwait(false);
        return new GameExit(crash is null ? GameExitKind.Closed : GameExitKind.Crashed, exitCode, output, crash);
    }

    /// <summary>
    /// The crash log that the game wrote for the process after the launch. The
    /// log names the process, so the crash of another run, such as the new
    /// process of a restart, is never taken for this one.
    /// </summary>
    private async Task<GameCrash?> FindCrashAsync(Instance instance, int processId, DateTime? crashLogAtLaunch, CancellationToken cancellationToken)
    {
        var gameLog = _pathProvider.GetInstanceGameLogPath(instance.InstanceId);
        var modsFolder = _pathProvider.GetInstanceModsFolder(instance.InstanceId);
        var watched = Stopwatch.StartNew();
        while (true)
        {
            var log = GameLogFiles.FindCrashLogs(gameLog)
                .Where(log => log.ProcessId == processId && (crashLogAtLaunch is null || log.File.LastWriteTimeUtc > crashLogAtLaunch))
                .MaxBy(log => log.File.LastWriteTimeUtc);
            if (log is not null && GameCrashLogs.Read(log, instance, modsFolder) is { } crash)
                return crash;

            if (watched.Elapsed >= _crashLogWait)
                return null;

            await Task.Delay(Min(PollInterval, _crashLogWait - watched.Elapsed), cancellationToken).ConfigureAwait(false);
        }
    }

    private static DateTime? LastCrashLogWrite(string gameLogPath) => GameLogFiles.FindCrashLogs(gameLogPath)
        .Select(log => (DateTime?)log.File.LastWriteTimeUtc)
        .Max();

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
    private static void WriteLaunchLog(string path, LaunchPlan plan, IReadOnlyList<string> output, int? exitCode, bool restarted, bool windows)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var header = new[]
            {
                $"Launch at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
                $"Executable: {plan.Executable}",
                (exitCode, restarted) switch
                {
                    (null, _) => "The loader was still running when Borea stopped watching.",
                    (_, true) => "The loader restarted itself. Its new process was still running when Borea stopped watching.",
                    ({ } code, _) => $"The loader exited with code {LoaderExitCode.Describe(code, windows)}.",
                },
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
            _launches.Forget(ended: true, HasEnded);
            return _running.ContainsKey(instanceId);
        }
    }

    /// <summary>
    /// Whether a launch has ended. After the loader exited, a process that
    /// still holds its streams keeps the launch only while a game process
    /// runs, because then that process is the restart. A browser or a file
    /// manager that the game opened before it closed does not keep it.
    /// </summary>
    private bool HasEnded(IStartedProcess process) => process.HasEnded || (process.HasExited && !_isGameRunning());

    /// <summary>Releases the handles, unless the launches are shared. The processes keep running.</summary>
    public void Dispose()
    {
        if (!_ownsLaunches)
            return;

        lock (_gate)
        {
            _launches.Forget(ended: false);
        }
    }
}
