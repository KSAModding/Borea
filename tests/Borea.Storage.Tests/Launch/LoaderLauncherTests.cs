using System.ComponentModel;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Storage.Launch;
using Borea.Storage.Paths;
using Borea.Storage.Tests.Mods;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Launch;

public sealed class LoaderLauncherTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly FakeProcessStarter _starter = new();
    private readonly LoaderLauncher _launcher;
    private readonly Instance _instance = new("Test", InstanceSource.Custom.Value);

    public LoaderLauncherTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _launcher = new LoaderLauncher(_paths, _starter);
    }

    private string StarMapDirectory => Path.Combine(_tempRoot, "StarMap");

    /// <summary>Puts an empty file where the loader's executable would be.</summary>
    private string PlaceStarMap(string launch = "StarMap.exe")
    {
        var executable = Path.Combine(StarMapDirectory, launch.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllBytes(executable, Array.Empty<byte>());
        return executable;
    }

    private static ModMetadata LoaderListing(string modId = "StarMap", LoaderProvides? provides = null, bool standalone = true) => new(
        specVersion: 1,
        modId: modId,
        source: "TestSource",
        name: "StarMap",
        authors: new[] { "KlaasWhite" },
        abstractText: "The loader.",
        license: "MIT",
        links: MetadataFixtures.SampleLinks(),
        gameMin: "2026.8.3.5117",
        type: ContentType.ModLoader,
        install: standalone ? new InstallDescriptor(target: InstallAnchor.Standalone) : null,
        provides: provides);

    private static LoaderProvides StarMapProvides(string launch = "StarMap.exe", InstanceHandover? instance = null) => new(
        launch: launch,
        contentDir: InstallAnchor.Mods,
        configure: new LoaderConfigure("StarMapConfig.json", ConfigureFormat.Json, "GameLocation"),
        instance: instance ?? new InstanceHandover("-InstancePath", "STARMAP_INSTANCE_PATH"));

    [Fact]
    public void Launch_ListingWithFlagAndVariable_StartsItInItsDirectoryWithTheInstanceRoot()
    {
        var executable = PlaceStarMap();
        var instanceRoot = Path.GetFullPath(_paths.GetInstanceRoot(_instance.InstanceId));

        var result = _launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.True(result.Started);
        Assert.Equal(LaunchOutcome.Started, result.Outcome);
        Assert.Equal(Assert.Single(_starter.Processes).Id, result.ProcessId);
        Assert.Contains("StarMap", result.Message);

        var plan = Assert.Single(_starter.Plans);
        Assert.Same(plan, result.Plan);
        Assert.Equal(executable, plan.Executable);
        Assert.Equal(new[] { "-InstancePath", instanceRoot }, plan.Arguments);
        Assert.Equal(instanceRoot, plan.EnvironmentVariables["STARMAP_INSTANCE_PATH"]);
        Assert.Equal(Path.GetFullPath(StarMapDirectory), plan.WorkingDirectory);
        Assert.True(_launcher.IsRunning(_instance.InstanceId));
    }

    [Fact]
    public void Launch_SavedAndGivenArguments_FollowTheHandoverInThatOrder()
    {
        PlaceStarMap();
        var instanceRoot = Path.GetFullPath(_paths.GetInstanceRoot(_instance.InstanceId));
        _instance.SetLaunchArguments(["-saved", "a saved value"]);

        var result = _launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()), ["-given", "a given value"]);

        Assert.True(result.Started);
        Assert.Equal(new[] { "-InstancePath", instanceRoot, "-saved", "a saved value", "-given", "a given value" }, Assert.Single(_starter.Plans).Arguments);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Launch_HandoverFlagInTheArguments_StartsNothing(bool saved)
    {
        PlaceStarMap();
        string[] arguments = ["-instancepath", "D:/Other"];
        if (saved)
            _instance.SetLaunchArguments(arguments);

        var result = _launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()), saved ? null : arguments);

        Assert.False(result.Started);
        Assert.Equal(LaunchOutcome.HandoverFlagInArguments, result.Outcome);
        Assert.Contains("'-instancepath'", result.Message);
        Assert.Empty(_starter.Plans);
        Assert.False(_launcher.IsRunning(_instance.InstanceId));
    }

    [Theory]
    [InlineData(OsPlatform.Linux)]
    [InlineData(OsPlatform.MacOs)]
    public void Launch_AssemblyBesideTheAppHostOutsideWindows_StartsItThroughDotnet(OsPlatform platform)
    {
        PlaceStarMap();
        var assembly = PlaceStarMap("StarMap.dll");
        var dotnet = Path.Combine(_tempRoot, "dotnet", "dotnet");
        Directory.CreateDirectory(Path.GetDirectoryName(dotnet)!);
        File.WriteAllBytes(dotnet, Array.Empty<byte>());
        var instanceRoot = Path.GetFullPath(_paths.GetInstanceRoot(_instance.InstanceId));
        using var launcher = new LoaderLauncher(_paths, _starter, platform, () => dotnet);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.True(result.Started);
        var plan = Assert.Single(_starter.Plans);
        Assert.Same(plan, result.Plan);
        Assert.Equal(dotnet, plan.Executable);
        Assert.Equal(new[] { assembly, "-InstancePath", instanceRoot }, plan.Arguments);
        Assert.Equal(instanceRoot, plan.EnvironmentVariables["STARMAP_INSTANCE_PATH"]);
        Assert.Equal(Path.GetFullPath(StarMapDirectory), plan.WorkingDirectory);
    }

    [Fact]
    public void Launch_AssemblyBesideTheAppHostOnWindows_StartsTheAppHost()
    {
        var executable = PlaceStarMap();
        PlaceStarMap("StarMap.dll");
        using var launcher = new LoaderLauncher(_paths, _starter, OsPlatform.Windows, () => throw new InvalidOperationException("Windows needs no dotnet host."));

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.True(result.Started);
        Assert.Equal(executable, Assert.Single(_starter.Plans).Executable);
    }

    [Fact]
    public void Launch_NoAssemblyBesideTheAppHostOnLinux_StartsTheTarget()
    {
        var executable = PlaceStarMap();
        using var launcher = new LoaderLauncher(_paths, _starter, OsPlatform.Linux, () => throw new InvalidOperationException("Only an assembly needs a dotnet host."));

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.True(result.Started);
        Assert.Equal(executable, Assert.Single(_starter.Plans).Executable);
    }

    [Fact]
    public void Launch_AssemblyBesideTheAppHostWithoutDotnet_ReportsIt()
    {
        PlaceStarMap();
        PlaceStarMap("StarMap.dll");
        using var launcher = new LoaderLauncher(_paths, _starter, OsPlatform.Linux, () => null);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.Equal(LaunchOutcome.DotnetMissing, result.Outcome);
        Assert.Contains("dotnet", result.Message);
        Assert.Contains("StarMap", result.Message);
        Assert.Empty(_starter.Plans);
        Assert.False(launcher.IsRunning(_instance.InstanceId));
    }

    [Fact]
    public void Launch_NestedLaunchTarget_ResolvesUnderTheLoaderDirectory()
    {
        var executable = PlaceStarMap("bin/StarMap.exe");

        var result = _launcher.Launch(_instance, LoaderListing(provides: StarMapProvides("bin/StarMap.exe")));

        Assert.True(result.Started);
        Assert.Equal(executable, result.Plan!.Executable);
    }

    [Fact]
    public void Launch_NoLoader_ReportsInsteadOfStartingTheGame()
    {
        var result = _launcher.Launch(_instance, loader: null);

        Assert.Equal(LaunchOutcome.NoLoader, result.Outcome);
        Assert.False(result.Started);
        Assert.Empty(_starter.Plans);
        Assert.False(_launcher.IsRunning(_instance.InstanceId));
    }

    [Fact]
    public void Launch_ListingWithoutLaunchTarget_ReportsIt()
    {
        PlaceStarMap();

        var result = _launcher.Launch(_instance, LoaderListing(provides: new LoaderProvides(contentDir: InstallAnchor.Mods), standalone: false));

        Assert.Equal(LaunchOutcome.NoLaunchTarget, result.Outcome);
        Assert.Contains("StarMap", result.Message);
        Assert.Empty(_starter.Plans);
    }

    [Fact]
    public void Launch_ListingWithoutProvides_ReportsNoLaunchTarget()
    {
        PlaceStarMap();

        var result = _launcher.Launch(_instance, LoaderListing(provides: null, standalone: false));

        Assert.Equal(LaunchOutcome.NoLaunchTarget, result.Outcome);
        Assert.Empty(_starter.Plans);
    }

    [Fact]
    public void Launch_ListingWithOnlyAFlag_PassesTheFlagAndSetsNoVariable()
    {
        PlaceStarMap();
        var instanceRoot = Path.GetFullPath(_paths.GetInstanceRoot(_instance.InstanceId));

        var result = _launcher.Launch(_instance, LoaderListing(provides: StarMapProvides(instance: new InstanceHandover("-Instance", null))));

        Assert.True(result.Started);
        Assert.Equal(new[] { "-Instance", instanceRoot }, result.Plan!.Arguments);
        Assert.Empty(result.Plan.EnvironmentVariables);
    }

    [Fact]
    public void Launch_ListingWithOnlyAVariable_SetsTheVariableAndPassesNoArguments()
    {
        PlaceStarMap();
        var instanceRoot = Path.GetFullPath(_paths.GetInstanceRoot(_instance.InstanceId));

        var result = _launcher.Launch(_instance, LoaderListing(provides: StarMapProvides(instance: new InstanceHandover(null, "LOADER_INSTANCE"))));

        Assert.True(result.Started);
        Assert.Empty(result.Plan!.Arguments);
        Assert.Equal(instanceRoot, Assert.Single(result.Plan.EnvironmentVariables, pair => pair.Key == "LOADER_INSTANCE").Value);
        Assert.Single(result.Plan.EnvironmentVariables);
    }

    [Fact]
    public void Launch_ListingWithoutInstanceTable_ReportsInsteadOfGuessing()
    {
        PlaceStarMap();
        var provides = new LoaderProvides(
            launch: "StarMap.exe",
            contentDir: InstallAnchor.Mods,
            configure: new LoaderConfigure("StarMapConfig.json", ConfigureFormat.Json, "GameLocation"));

        var result = _launcher.Launch(_instance, LoaderListing(provides: provides));

        Assert.Equal(LaunchOutcome.NoInstanceHandover, result.Outcome);
        Assert.Contains("StarMap", result.Message);
        Assert.Contains("[provides.instance]", result.Message);
        Assert.Null(result.Plan);
        Assert.Empty(_starter.Plans);
        Assert.False(_launcher.IsRunning(_instance.InstanceId));
    }

    [Fact]
    public void Launch_NoLoaderDirectoryConfigured_ReportsIt()
    {
        // The real provider with no loader directories, so StarMap is known but not located.
        using var launcher = new LoaderLauncher(new GamePathProvider(gameDirectory: null), _starter);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.Equal(LaunchOutcome.NoLoaderDirectory, result.Outcome);
        Assert.Contains("StarMap", result.Message);
        Assert.Empty(_starter.Plans);
    }

    [Fact]
    public void Launch_ExecutableMissing_ReportsIt()
    {
        Directory.CreateDirectory(StarMapDirectory);

        var result = _launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.Equal(LaunchOutcome.LaunchTargetMissing, result.Outcome);
        Assert.Contains(Path.Combine(StarMapDirectory, "StarMap.exe"), result.Message);
        Assert.Equal(Path.Combine(Path.GetFullPath(StarMapDirectory), "StarMap.exe"), result.Plan!.Executable);
        Assert.Empty(_starter.Plans);
        Assert.False(_launcher.IsRunning(_instance.InstanceId));
    }

    [Fact]
    public void Launch_SecondLaunchWhileTheFirstRuns_IsRefused()
    {
        PlaceStarMap();
        var listing = LoaderListing(provides: StarMapProvides());
        _launcher.Launch(_instance, listing);

        var second = _launcher.Launch(_instance, listing);

        Assert.Equal(LaunchOutcome.AlreadyRunning, second.Outcome);
        Assert.Contains("Test", second.Message);
        Assert.Single(_starter.Plans);
        Assert.True(_launcher.IsRunning(_instance.InstanceId));
    }

    [Fact]
    public void Launch_AfterTheProcessExited_StartsAgainAndReleasesTheOldHandle()
    {
        PlaceStarMap();
        var listing = LoaderListing(provides: StarMapProvides());
        _launcher.Launch(_instance, listing);
        var first = Assert.Single(_starter.Processes);

        first.HasExited = true;

        Assert.False(_launcher.IsRunning(_instance.InstanceId));
        Assert.True(first.Disposed);

        var second = _launcher.Launch(_instance, listing);

        Assert.True(second.Started);
        Assert.Equal(_starter.Processes[^1].Id, second.ProcessId);
        Assert.NotEqual(first.Id, second.ProcessId);
        Assert.True(_launcher.IsRunning(_instance.InstanceId));
    }

    [Fact]
    public void Launch_TwoInstances_BothRun()
    {
        PlaceStarMap();
        var listing = LoaderListing(provides: StarMapProvides());
        var other = new Instance("Other", InstanceSource.Custom.Value);

        var first = _launcher.Launch(_instance, listing);
        var second = _launcher.Launch(other, listing);

        Assert.True(first.Started);
        Assert.True(second.Started);
        Assert.True(_launcher.IsRunning(_instance.InstanceId));
        Assert.True(_launcher.IsRunning(other.InstanceId));
        Assert.NotEqual(first.Plan!.Arguments[1], second.Plan!.Arguments[1]);
    }

    [Fact]
    public void Launch_SystemRefusesToStart_ReportsStartFailed()
    {
        PlaceStarMap();
        _starter.Failure = new Win32Exception("Access is denied.");

        var result = _launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.Equal(LaunchOutcome.StartFailed, result.Outcome);
        Assert.Contains("Access is denied.", result.Message);
        Assert.Same(Assert.Single(_starter.Plans), result.Plan);
        Assert.False(_launcher.IsRunning(_instance.InstanceId));
    }

    [Fact]
    public void Launch_AModInsteadOfALoader_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _launcher.Launch(_instance, MetadataFixtures.MinimalMetadata()));
    }

    [Fact]
    public void Launch_NullInstance_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _launcher.Launch(null!, LoaderListing(provides: StarMapProvides())));
    }

    [Fact]
    public void IsRunning_UnknownInstance_IsFalse()
    {
        Assert.False(_launcher.IsRunning(Guid.NewGuid()));
    }

    [Fact]
    public void Dispose_ReleasesTheHandlesAndForgetsTheLaunches()
    {
        PlaceStarMap();
        _launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));
        var process = Assert.Single(_starter.Processes);

        _launcher.Dispose();

        Assert.True(process.Disposed);
        Assert.False(_launcher.IsRunning(_instance.InstanceId));
    }

    [Fact]
    public void Constructor_NullDependency_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new LoaderLauncher(null!, _starter));
        Assert.Throws<ArgumentNullException>(() => new LoaderLauncher(_paths, null!));
        Assert.Throws<ArgumentNullException>(() => new LoaderLauncher(_paths, _starter, OsPlatform.Linux, null!));
    }

    private static readonly string[] KsArmoryCrash =
    [
        "StarMap - Using Instance Path: C:\\Instances\\Main",
        "Unhandled exception. System.Reflection.ReflectionTypeLoadException: Unable to load one or more of the requested types.",
        "Method 'DrawAxes' in type 'KSArmory.RoundFollowable' from assembly 'KSArmory, Version=0.8.44.0, Culture=neutral, PublicKeyToken=null' does not have an implementation.",
        "   at StarMap.Core.ModRepository.ModLoader.PrepareMods()",
    ];

    private static Instance InstanceWith(params string[] modIds)
    {
        var instance = new Instance("Main", InstanceSource.Custom.Value);
        foreach (var modId in modIds)
        {
            var release = MetadataFixtures.MinimalRelease(modId, "0.8.44");
            instance.AddMod(new InstalledMod(modId, release.Version, InstallReason.Manual, DateTimeOffset.UtcNow, release));
        }

        return instance;
    }

    [Fact]
    public async Task WatchStart_LoaderStopsWithAnErrorNamingAMod_ReportsTheMod()
    {
        PlaceStarMap();
        var instance = InstanceWith("KSArmory");
        var started = _launcher.Launch(instance, LoaderListing(provides: StarMapProvides()));
        var process = Assert.Single(_starter.Processes);
        process.Output.AddRange(KsArmoryCrash);
        process.HasExited = true;
        process.ExitCode = -532462766;

        var result = await _launcher.WatchStartAsync(instance, started);

        Assert.Equal(LaunchOutcome.ExitedEarly, result.Outcome);
        Assert.False(result.Started);
        Assert.Equal(-532462766, result.ExitCode);
        Assert.Equal("KSArmory", result.BlamedModId);
        Assert.Contains("KSArmory 0.8.44 stopped StarMap from starting", result.Message);
        Assert.Equal(KsArmoryCrash, result.Output);
        Assert.Same(started.Plan, result.Plan);

        var log = File.ReadAllLines(_paths.GetInstanceLaunchLogPath(instance.InstanceId));
        Assert.Contains("The loader exited with code -532462766.", log);
        Assert.Contains(KsArmoryCrash[2], log);
    }

    [Fact]
    public async Task WatchStart_AssemblyInAModFolder_BlamesThatModEvenWithAnotherId()
    {
        PlaceStarMap();
        var instance = InstanceWith("kessler-armory");
        var folder = Directory.CreateDirectory(Path.Combine(_paths.GetInstanceModsFolder(instance.InstanceId), "kessler-armory", "bin"));
        File.WriteAllBytes(Path.Combine(folder.FullName, "KSArmory.dll"), []);
        var started = _launcher.Launch(instance, LoaderListing(provides: StarMapProvides()));
        var process = Assert.Single(_starter.Processes);
        process.Output.AddRange(KsArmoryCrash);
        process.HasExited = true;
        process.ExitCode = 1;

        var result = await _launcher.WatchStartAsync(instance, started);

        Assert.Equal("kessler-armory", result.BlamedModId);
    }

    [Fact]
    public async Task WatchStart_ErrorNamingNoInstalledMod_ReportsTheExitCode()
    {
        PlaceStarMap();
        var instance = InstanceWith("MeasureTools");
        var started = _launcher.Launch(instance, LoaderListing(provides: StarMapProvides()));
        var process = Assert.Single(_starter.Processes);
        process.Output.Add("Could not load file or assembly 'Brutal.Core.Common, Version=1.0.0.0'.");
        process.HasExited = true;
        process.ExitCode = 3;

        var result = await _launcher.WatchStartAsync(instance, started);

        Assert.Equal(LaunchOutcome.ExitedEarly, result.Outcome);
        Assert.Null(result.BlamedModId);
        Assert.Contains("exit code 3", result.Message);
    }

    [Fact]
    public async Task WatchStart_LoaderExitsWithZeroAndTheGameComesUp_StaysStarted()
    {
        PlaceStarMap();
        using var launcher = new LoaderLauncher(_paths, _starter, TimeSpan.FromMinutes(5));
        var started = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));
        var process = Assert.Single(_starter.Processes);
        process.Output.Add("Restarting.");
        process.HasExited = true;
        process.ExitCode = 0;
        var gameLog = _paths.GetInstanceGameLogPath(_instance.InstanceId);
        Directory.CreateDirectory(Path.GetDirectoryName(gameLog)!);
        File.WriteAllText(gameLog, "INFO loaded settings");

        var result = await launcher.WatchStartAsync(_instance, started);

        Assert.True(result.Started);
        Assert.Equal(["Restarting."], result.Output);
    }

    [Fact]
    public async Task WatchStart_LoaderExitsWithZeroAndTheGameWritesARunLog_StaysStarted()
    {
        PlaceStarMap();
        using var launcher = new LoaderLauncher(_paths, _starter, TimeSpan.FromMinutes(5));
        var started = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));
        var process = Assert.Single(_starter.Processes);
        process.HasExited = true;
        process.ExitCode = 0;
        var runLog = Path.Combine(Path.GetDirectoryName(_paths.GetInstanceGameLogPath(_instance.InstanceId))!, "KittenSpaceAgency.260915-112433.43720.log");
        Directory.CreateDirectory(Path.GetDirectoryName(runLog)!);
        File.WriteAllText(runLog, "11:24:36.689  INFO loaded settings from settings.toml");

        var watch = launcher.WatchStartAsync(_instance, started);

        Assert.True(await Task.WhenAny(watch, Task.Delay(TimeSpan.FromSeconds(10))) == watch, "The watch did not stop when the game wrote its run log.");
        Assert.True((await watch).Started);
    }

    [Fact]
    public async Task WatchStart_LoaderExitsWithZeroWithoutTheGame_ReportsItWithoutBlamingAMod()
    {
        PlaceStarMap();
        var instance = InstanceWith("KSArmory");
        using var launcher = new LoaderLauncher(_paths, _starter, TimeSpan.FromMilliseconds(100));
        var started = launcher.Launch(instance, LoaderListing(provides: StarMapProvides()));
        var process = Assert.Single(_starter.Processes);
        process.Output.Add("GameLocation is empty in StarMapConfig.json.");
        process.HasExited = true;
        process.ExitCode = 0;

        var result = await launcher.WatchStartAsync(instance, started);

        Assert.Equal(LaunchOutcome.ExitedEarly, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.BlamedModId);
        Assert.Contains("stopped without starting the game", result.Message);
        Assert.Equal(["GameLocation is empty in StarMapConfig.json."], result.Output);
    }

    [Fact]
    public async Task WatchStart_LoaderStillRunningAfterTheWindow_StaysStarted()
    {
        PlaceStarMap();
        using var launcher = new LoaderLauncher(_paths, _starter, TimeSpan.FromMilliseconds(50));
        var started = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        var result = await launcher.WatchStartAsync(_instance, started);

        Assert.True(result.Started);
        Assert.True(launcher.IsRunning(_instance.InstanceId));
        Assert.Contains("still running", File.ReadAllText(_paths.GetInstanceLaunchLogPath(_instance.InstanceId)));
    }

    [Fact]
    public async Task WatchStart_GameWritesItsLog_StopsWatchingAndStaysStarted()
    {
        PlaceStarMap();
        using var launcher = new LoaderLauncher(_paths, _starter, TimeSpan.FromMinutes(5));
        var started = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));
        var gameLog = _paths.GetInstanceGameLogPath(_instance.InstanceId);
        Directory.CreateDirectory(Path.GetDirectoryName(gameLog)!);
        File.WriteAllText(gameLog, "INFO loaded system 'Sol'");

        var watch = launcher.WatchStartAsync(_instance, started);

        Assert.True(await Task.WhenAny(watch, Task.Delay(TimeSpan.FromSeconds(10))) == watch, "The watch did not stop when the game wrote its log.");
        Assert.True((await watch).Started);
    }

    [Fact]
    public async Task WatchStart_ResultThatDidNotStart_IsReturnedAsItIs()
    {
        var failed = _launcher.Launch(_instance, loader: null);

        Assert.Same(failed, await _launcher.WatchStartAsync(_instance, failed));
    }

    public void Dispose()
    {
        _launcher.Dispose();

        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
