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

    private static LoaderProvides StarMapProvides(string launch = "StarMap.exe", InstanceHandover? instance = null, Dictionary<OsPlatform, LoaderPlatformLaunch>? platforms = null) => new(
        launch: launch,
        contentDir: InstallAnchor.Mods,
        configure: new LoaderConfigure("StarMapConfig.json", ConfigureFormat.Json, "GameLocation"),
        instance: instance ?? new InstanceHandover("-InstancePath", "STARMAP_INSTANCE_PATH"),
        platforms: platforms);

    private string PlaceDotnet()
    {
        var dotnet = Path.Combine(_tempRoot, "dotnet", "dotnet");
        Directory.CreateDirectory(Path.GetDirectoryName(dotnet)!);
        File.WriteAllBytes(dotnet, Array.Empty<byte>());
        return dotnet;
    }

    private static string NoDotnetNeeded() => throw new InvalidOperationException("This start needs no dotnet host.");

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

    [Fact]
    public void Launch_NoEntryForThisPlatform_StartsTheDefaultLaunch()
    {
        var executable = PlaceStarMap();
        PlaceStarMap("StarMap.dll");
        var platforms = new Dictionary<OsPlatform, LoaderPlatformLaunch> { [OsPlatform.MacOs] = new("StarMap.dll", "dotnet") };
        using var launcher = new LoaderLauncher(_paths, _starter, OsPlatform.Linux, NoDotnetNeeded);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides(platforms: platforms)));

        Assert.True(result.Started);
        Assert.Equal(executable, Assert.Single(_starter.Plans).Executable);
    }

    [Fact]
    public void Launch_EntryWithoutRuntime_StartsItsLaunch()
    {
        PlaceStarMap();
        var executable = PlaceStarMap("linux/StarMap");
        var instanceRoot = Path.GetFullPath(_paths.GetInstanceRoot(_instance.InstanceId));
        var platforms = new Dictionary<OsPlatform, LoaderPlatformLaunch> { [OsPlatform.Linux] = new("linux/StarMap") };
        using var launcher = new LoaderLauncher(_paths, _starter, OsPlatform.Linux, NoDotnetNeeded);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides(platforms: platforms)));

        Assert.True(result.Started);
        var plan = Assert.Single(_starter.Plans);
        Assert.Equal(executable, plan.Executable);
        Assert.Equal(new[] { "-InstancePath", instanceRoot }, plan.Arguments);
        Assert.Equal(Path.GetFullPath(StarMapDirectory), plan.WorkingDirectory);
    }

    [Theory]
    [InlineData(OsPlatform.Linux)]
    [InlineData(OsPlatform.MacOs)]
    [InlineData(OsPlatform.Windows)]
    public void Launch_DotnetEntry_StartsDotnetWithTheEntryFileThenTheHandoverAndTheArguments(OsPlatform platform)
    {
        PlaceStarMap();
        var assembly = PlaceStarMap("StarMap.dll");
        var dotnet = PlaceDotnet();
        var instanceRoot = Path.GetFullPath(_paths.GetInstanceRoot(_instance.InstanceId));
        _instance.SetLaunchArguments(["-saved"]);
        var platforms = new Dictionary<OsPlatform, LoaderPlatformLaunch> { [platform] = new("StarMap.dll", "dotnet") };
        using var launcher = new LoaderLauncher(_paths, _starter, platform, () => dotnet);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides(platforms: platforms)), ["-given"]);

        Assert.True(result.Started);
        var plan = Assert.Single(_starter.Plans);
        Assert.Same(plan, result.Plan);
        Assert.Equal(dotnet, plan.Executable);
        Assert.Equal(new[] { assembly, "-InstancePath", instanceRoot, "-saved", "-given" }, plan.Arguments);
        Assert.Equal(instanceRoot, plan.EnvironmentVariables["STARMAP_INSTANCE_PATH"]);
        Assert.Equal(Path.GetFullPath(StarMapDirectory), plan.WorkingDirectory);
    }

    [Fact]
    public void Launch_DotnetEntryWithoutDotnet_StartsNothing()
    {
        PlaceStarMap();
        PlaceStarMap("StarMap.dll");
        var platforms = new Dictionary<OsPlatform, LoaderPlatformLaunch> { [OsPlatform.Linux] = new("StarMap.dll", "dotnet") };
        using var launcher = new LoaderLauncher(_paths, _starter, OsPlatform.Linux, () => null);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides(platforms: platforms)));

        Assert.Equal(LaunchOutcome.DotnetMissing, result.Outcome);
        Assert.Contains("dotnet", result.Message);
        Assert.Contains("StarMap", result.Message);
        Assert.Empty(_starter.Plans);
        Assert.False(launcher.IsRunning(_instance.InstanceId));
    }

    [Fact]
    public void Launch_UnknownRuntimeInTheOwnEntry_StartsNothing()
    {
        PlaceStarMap();
        PlaceStarMap("StarMap.dll");
        var platforms = new Dictionary<OsPlatform, LoaderPlatformLaunch> { [OsPlatform.Linux] = new("StarMap.dll", "mono") };
        using var launcher = new LoaderLauncher(_paths, _starter, OsPlatform.Linux, NoDotnetNeeded);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides(platforms: platforms)));

        Assert.Equal(LaunchOutcome.UnknownRuntime, result.Outcome);
        Assert.Equal("mono", result.UnknownName);
        Assert.Contains("'mono'", result.Message);
        Assert.Empty(_starter.Plans);
    }

    [Fact]
    public void Launch_UnknownKeyInTheOwnEntry_StartsNothing()
    {
        PlaceStarMap();
        PlaceStarMap("StarMap.dll");
        var platforms = new Dictionary<OsPlatform, LoaderPlatformLaunch> { [OsPlatform.Linux] = new("StarMap.dll", "dotnet", ["arch"]) };
        using var launcher = new LoaderLauncher(_paths, _starter, OsPlatform.Linux, NoDotnetNeeded);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides(platforms: platforms)));

        Assert.Equal(LaunchOutcome.UnknownPlatformKey, result.Outcome);
        Assert.Equal("arch", result.UnknownName);
        Assert.Contains("'arch'", result.Message);
        Assert.Empty(_starter.Plans);
    }

    [Fact]
    public void Launch_EntryFileMissing_StartsNothingInsteadOfTheDefault()
    {
        PlaceStarMap();
        var platforms = new Dictionary<OsPlatform, LoaderPlatformLaunch> { [OsPlatform.Linux] = new("StarMap.dll", "dotnet") };
        using var launcher = new LoaderLauncher(_paths, _starter, OsPlatform.Linux, NoDotnetNeeded);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides(platforms: platforms)));

        Assert.Equal(LaunchOutcome.LaunchTargetMissing, result.Outcome);
        Assert.Equal(Path.Combine(Path.GetFullPath(StarMapDirectory), "StarMap.dll"), result.Plan!.Executable);
        Assert.Contains("StarMap.dll", result.Message);
        Assert.Empty(_starter.Plans);
    }

    [Fact]
    public void Launch_UnknownRuntimeAndKeyInOtherEntries_ChangeNothing()
    {
        var executable = PlaceStarMap();
        var platforms = new Dictionary<OsPlatform, LoaderPlatformLaunch>
        {
            [OsPlatform.Linux] = new("StarMap.dll", "mono"),
            [OsPlatform.MacOs] = new("StarMap.dll", "dotnet", ["arch"]),
        };
        using var launcher = new LoaderLauncher(_paths, _starter, OsPlatform.Windows, NoDotnetNeeded);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides(platforms: platforms)));

        Assert.True(result.Started);
        Assert.Equal(executable, Assert.Single(_starter.Plans).Executable);
    }

    [Fact]
    public void Launch_UnknownSystem_StartsTheDefaultLaunch()
    {
        var executable = PlaceStarMap();
        var platforms = new Dictionary<OsPlatform, LoaderPlatformLaunch> { [OsPlatform.Linux] = new("StarMap.dll", "dotnet") };
        using var launcher = new LoaderLauncher(_paths, _starter, platform: null, NoDotnetNeeded);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides(platforms: platforms)));

        Assert.True(result.Started);
        Assert.Equal(executable, Assert.Single(_starter.Plans).Executable);
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
    public void Dispose_SharedLaunches_KeepsTheLaunchForTheNextLauncher()
    {
        PlaceStarMap();
        var launches = new RunningLaunches();
        var listing = LoaderListing(provides: StarMapProvides());
        var first = new LoaderLauncher(_paths, _starter, launches);
        first.Launch(_instance, listing);
        var process = Assert.Single(_starter.Processes);

        first.Dispose();
        using var second = new LoaderLauncher(_paths, _starter, launches);

        Assert.False(process.Disposed);
        Assert.True(second.IsRunning(_instance.InstanceId));
        Assert.Equal(LaunchOutcome.AlreadyRunning, second.Launch(_instance, listing).Outcome);
    }

    [Fact]
    public void Constructor_NullDependency_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new LoaderLauncher(null!, _starter));
        Assert.Throws<ArgumentNullException>(() => new LoaderLauncher(_paths, null!));
        Assert.Throws<ArgumentNullException>(() => new LoaderLauncher(_paths, _starter, (RunningLaunches)null!));
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
        Assert.Contains($"The loader exited with code {LoaderExitCode.Describe(-532462766, OperatingSystem.IsWindows())}.", log);
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

    private static readonly string[] ClrCrash =
    [
        "Fatal error.",
        "Internal CLR error. (0x80131506)",
        "   at System.Reflection.RuntimeModule.GetTypes()",
        "   at StarMap.Core.ModRepository.RuntimeMod.InitializeMod(StarMap.Core.ModRepository.ModRegistry)",
        "   at StarMap.Core.ModRepository.ModLoader.PrepareMods()",
    ];

    private void WriteManifest(Instance instance, string toml)
    {
        var path = _paths.GetInstanceManifestPath(instance.InstanceId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, toml);
    }

    private void PlaceMod(Instance instance, string modId, string modToml, params string[] assemblies)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_paths.GetInstanceModsFolder(instance.InstanceId), modId)).FullName;
        File.WriteAllText(Path.Combine(folder, "mod.toml"), modToml);
        foreach (var assembly in assemblies)
            File.WriteAllBytes(Path.Combine(folder, assembly + ".dll"), []);
    }

    private async Task<LaunchResult> CrashAsync(Instance instance, int exitCode, params string[] output)
    {
        var started = _launcher.Launch(instance, LoaderListing(provides: StarMapProvides()));
        var process = Assert.Single(_starter.Processes);
        process.Output.AddRange(output);
        process.HasExited = true;
        process.ExitCode = exitCode;
        return await _launcher.WatchStartAsync(instance, started);
    }

    [Fact]
    public async Task WatchStart_CrashWhileLoadingMods_BlamesTheFirstCodeModStarMapDidNotReport()
    {
        PlaceStarMap();
        var instance = InstanceWith("ModMenu", "Broken", "Textures", "KSArmory");
        PlaceMod(instance, "ModMenu", "name = \"ModMenu\"", "ModMenu");
        PlaceMod(instance, "OldTools", "name = \"OldTools\"", "OldTools");
        PlaceMod(instance, "Broken", "name = \"Broken", "Broken");
        PlaceMod(instance, "Textures", "name = \"Textures\"");
        PlaceMod(instance, "KSArmory", "name = \"KSArmory\"\n[StarMap]\nEntryAssembly = \"KSArmory.Core\"", "KSArmory.Core");
        WriteManifest(instance, """
            [[mods]]
            id = "Core"
            enabled = true

            [[mods]]
            id = "ModMenu"
            enabled = true

            [[mods]]
            id = "OldTools"
            enabled = false

            [[mods]]
            id = "Broken"
            enabled = true

            [[mods]]
            id = "Textures"
            enabled = true

            [[mods]]
            id = "ksarmory"
            enabled = true
            """);

        var result = await CrashAsync(instance, -1073741819, ["StarMap - Using Instance Path: Main", "StarMap - Loaded mod: ModMenu from manifest", "StarMap - Not loading mod: OldTools because it is disabled in manifest", .. ClrCrash]);

        Assert.Equal("KSArmory", result.BlamedModId);
        Assert.Equal(LoaderCrashCause.ModLoading, result.CrashCause);
        Assert.Contains("StarMap stopped while it loaded KSArmory 0.8.44, so that mod is the likely cause.", result.Message);
    }

    [Fact]
    public async Task WatchStart_CrashInTryCreateModAfterAnInvalidModToml_BlamesThatMod()
    {
        PlaceStarMap();
        var instance = InstanceWith("ModMenu", "Broken", "KSArmory");
        PlaceMod(instance, "ModMenu", "name = \"ModMenu\"", "ModMenu");
        PlaceMod(instance, "Broken", "name = \"Broken");
        PlaceMod(instance, "KSArmory", "name = \"KSArmory\"", "KSArmory");
        WriteManifest(instance, "[[mods]]\nid = \"ModMenu\"\n\n[[mods]]\nid = \"Broken\"\n\n[[mods]]\nid = \"KSArmory\"\n");

        var result = await CrashAsync(instance, -532462766, ["StarMap - Using Instance Path: Main", "StarMap - Loaded mod: ModMenu from manifest", "Unhandled exception. Tomlet.Exceptions.TomlException: The mod.toml is not valid TOML.", "   at StarMap.Core.ModRepository.RuntimeMod.TryCreateMod(KSA.ModEntry, System.Runtime.Loader.AssemblyLoadContext, StarMap.Core.ModRepository.RuntimeMod ByRef)"]);

        Assert.Equal("Broken", result.BlamedModId);
        Assert.Equal(LoaderCrashCause.ModLoading, result.CrashCause);
    }

    [Fact]
    public async Task WatchStart_CrashAfterAllModsWereReported_BlamesTheWaitingModWithOnlyOptionalDependenciesMissing()
    {
        PlaceStarMap();
        var instance = InstanceWith("ModMenu", "KSArmory");
        PlaceMod(instance, "ModMenu", "name = \"ModMenu\"", "ModMenu");
        PlaceMod(instance, "KSArmory", "name = \"KSArmory\"\n\n[StarMap]\nEntryAssembly = \"KSArmory\"\n\n[[StarMap.ModDependencies]]\nModId = \"Extras\"\nOptional = true\n", "KSArmory");
        WriteManifest(instance, "[[mods]]\nid = \"KSArmory\"\n\n[[mods]]\nid = \"ModMenu\"\n");

        var result = await CrashAsync(instance, -1073741819, ["StarMap - Using Instance Path: Main", "StarMap - Delaying load of mod: KSArmory due to missing dependencies: Extras", "StarMap - Loaded mod: ModMenu from manifest", .. ClrCrash]);

        Assert.Equal("KSArmory", result.BlamedModId);
    }

    [Fact]
    public async Task WatchStart_CrashWhileLoadingModsWithoutAModLeft_SaysSoWithoutBlamingAMod()
    {
        PlaceStarMap();
        var instance = InstanceWith("ModMenu");
        PlaceMod(instance, "ModMenu", "name = \"ModMenu\"", "ModMenu");
        WriteManifest(instance, "[[mods]]\nid = \"ModMenu\"\n");

        var result = await CrashAsync(instance, -1073741819, ["StarMap - Using Instance Path: Main", "StarMap - Loaded mod: ModMenu from manifest", .. ClrCrash]);

        Assert.Null(result.BlamedModId);
        Assert.Equal(LoaderCrashCause.ModLoading, result.CrashCause);
        Assert.Contains("exit code -1073741819", result.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("[[mods]\nid = ")]
    [InlineData("mods = \"KSArmory\"")]
    [InlineData("[[mods]]\nid = 5\nenabled = \"yes\"")]
    public async Task WatchStart_CrashWhileLoadingModsWithoutAReadableManifest_BlamesNoMod(string? manifest)
    {
        PlaceStarMap();
        var instance = InstanceWith("KSArmory");
        PlaceMod(instance, "KSArmory", "name = \"KSArmory\"", "KSArmory");
        if (manifest is not null)
            WriteManifest(instance, manifest);

        var result = await CrashAsync(instance, -1073741819, ["StarMap - Using Instance Path: Main", .. ClrCrash]);

        Assert.Equal(LaunchOutcome.ExitedEarly, result.Outcome);
        Assert.Null(result.BlamedModId);
        Assert.Equal(LoaderCrashCause.ModLoading, result.CrashCause);
    }

    [Fact]
    public async Task WatchStart_AssemblyNamedWhileLoadingMods_BlamesTheModOfTheAssembly()
    {
        PlaceStarMap();
        var instance = InstanceWith("ModMenu", "KSArmory");
        PlaceMod(instance, "ModMenu", "name = \"ModMenu\"", "ModMenu");
        PlaceMod(instance, "KSArmory", "name = \"KSArmory\"", "KSArmory");
        WriteManifest(instance, "[[mods]]\nid = \"ModMenu\"\n\n[[mods]]\nid = \"KSArmory\"\n");

        var result = await CrashAsync(instance, -532462766, ["StarMap - Using Instance Path: Main", "StarMap - Loaded mod: ModMenu from manifest", "Could not load file or assembly 'ModMenu, Version=1.0.0.0'.", "   at StarMap.Core.ModRepository.RuntimeMod.InitializeMod(StarMap.Core.ModRepository.ModRegistry)"]);

        Assert.Equal("ModMenu", result.BlamedModId);
        Assert.Equal(LoaderCrashCause.ModAssembly, result.CrashCause);
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
