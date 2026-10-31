using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Logging;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Storage.Files;
using Borea.Storage.Game;
using Borea.Storage.Launch;
using Borea.Storage.ModLoaders;
using Borea.Storage.Paths;
using Borea.Storage.Tests.Game;
using Borea.Storage.Tests.Logging;
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

    /// <summary>A launcher that sees a game process when <paramref name="gameRuns"/> says so, and does not look at the machine.</summary>
    private LoaderLauncher LauncherSeeingGame(Func<bool> gameRuns, TimeSpan? startupWindow = null, IBoreaLog? log = null) => new(
        _paths,
        _starter,
        SharedProfileLauncher.CurrentPlatform(),
        () => DotnetHost.Find(SharedProfileLauncher.CurrentPlatform()),
        startupWindow ?? LoaderLauncher.DefaultStartupWindow,
        isGameRunning: gameRuns,
        log: log);

    /// <summary>Puts an empty file where the loader's executable would be.</summary>
    private string PlaceStarMap(string launch = "StarMap.exe")
    {
        var executable = Path.Combine(StarMapDirectory, launch.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllBytes(executable, Array.Empty<byte>());
        return executable;
    }

    internal static ModMetadata LoaderListing(string modId = "StarMap", LoaderProvides? provides = null, bool standalone = true, ReleaseSource? releases = null) => new(
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
        releases: releases,
        provides: provides);

    internal static LoaderProvides StarMapProvides(string launch = "StarMap.exe", InstanceHandover? instance = null, Dictionary<OsPlatform, LoaderPlatformLaunch>? platforms = null) => new(
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

    /// <summary>KSA.exe in the game folder, and the Linux app host too when asked.</summary>
    private static void PlaceWindowsBuild(TestGamePathProvider paths, bool linuxAppHost = false)
    {
        var game = Directory.CreateDirectory(paths.GetGameDirectoryPath()!).FullName;
        File.WriteAllBytes(Path.Combine(game, "KSA.exe"), Array.Empty<byte>());
        if (linuxAppHost)
            File.WriteAllBytes(Path.Combine(game, "KSA"), Array.Empty<byte>());
    }

    [Fact]
    public void Launch_WindowsBuildInAWinePrefixOnMacOs_StartsNothing()
    {
        WineFixtures.Prefix(_tempRoot);
        PlaceWindowsBuild(_paths);
        PlaceStarMap();
        PlaceStarMap("StarMap.dll");
        var dotnet = PlaceDotnet();
        var platforms = new Dictionary<OsPlatform, LoaderPlatformLaunch> { [OsPlatform.MacOs] = new("StarMap.dll", "dotnet") };
        using var launcher = new LoaderLauncher(_paths, _starter, OsPlatform.MacOs, () => dotnet);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides(platforms: platforms)));

        Assert.Equal(LaunchOutcome.WindowsBuild, result.Outcome);
        Assert.Contains("Wine prefix", result.Message);
        Assert.Equal(Path.GetFileName(_tempRoot), Path.GetFileName(result.Wine?.PrefixRoot));
        Assert.Null(result.Wine?.Wrapper);
        Assert.Empty(_starter.Plans);
        Assert.False(launcher.IsRunning(_instance.InstanceId));
    }

    [Fact]
    public void Launch_WindowsBuildInAWrapperOnLinux_RefusesItAsAPrefix()
    {
        var prefix = WineFixtures.Prefix(WineFixtures.WrapperPrefix(Path.Combine(_tempRoot, "Applications")));
        WineFixtures.Wrapper(prefix);
        var paths = new TestGamePathProvider(Path.Combine(prefix, "drive_c"));
        PlaceWindowsBuild(paths);
        using var launcher = new LoaderLauncher(paths, _starter, OsPlatform.Linux, NoDotnetNeeded);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.Equal(LaunchOutcome.WindowsBuild, result.Outcome);
        Assert.Contains("Wine prefix", result.Message);
        Assert.Equal("prefix", Path.GetFileName(result.Wine?.PrefixRoot));
        Assert.Null(result.Wine?.Wrapper);
        Assert.Empty(_starter.Plans);
    }

    [Fact]
    public void Launch_WindowsBuildWithoutAPrefixOnLinux_StartsNothing()
    {
        PlaceWindowsBuild(_paths);
        PlaceStarMap();
        using var launcher = new LoaderLauncher(_paths, _starter, OsPlatform.Linux, NoDotnetNeeded);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.Equal(LaunchOutcome.WindowsBuild, result.Outcome);
        Assert.Contains("found no Wine prefix", result.Message);
        Assert.Null(result.Wine);
        Assert.Empty(_starter.Plans);
    }

    [Theory]
    [InlineData(OsPlatform.Linux, true)]
    [InlineData(OsPlatform.Windows, false)]
    public void Launch_GameTheHostCanRun_StartsTheLoader(OsPlatform platform, bool linuxAppHost)
    {
        WineFixtures.Prefix(_tempRoot);
        PlaceWindowsBuild(_paths, linuxAppHost);
        PlaceStarMap();
        using var launcher = new LoaderLauncher(_paths, _starter, platform, NoDotnetNeeded);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.True(result.Started);
        Assert.Single(_starter.Plans);
    }

    /// <summary>
    /// The Windows build in the game folder of a prefix at the temp root, with
    /// a wrapper app in it. Unless <paramref name="drives"/> says otherwise,
    /// drive c: is drive_c and drive z: the root of the host, as in every
    /// prefix Wine creates.
    /// </summary>
    private FakeWineProbe PlaceWrapper(Dictionary<char, string>? drives = null)
    {
        PlaceWindowsBuild(_paths);
        drives ??= new Dictionary<char, string>
        {
            ['c'] = Directory.CreateDirectory(Path.Combine(_tempRoot, "drive_c")).FullName,
            ['z'] = Path.GetPathRoot(Path.GetFullPath(_tempRoot))!,
        };
        var bundle = Path.Combine(Path.GetFullPath(_tempRoot), "Kitten Space Agency.app");
        var wrapper = new WineWrapper(bundle, Path.Combine(bundle, "Contents", "MacOS", "launcher"));
        return new FakeWineProbe(new WineInstall(Path.GetFullPath(_tempRoot), WineFixtures.Drives(drives), wrapper));
    }

    private LoaderLauncher WrapperLauncher(FakeWineProbe wine, TimeSpan? startupWindow = null) =>
        new(_paths, _starter, OsPlatform.MacOs, NoDotnetNeeded, startupWindow ?? LoaderLauncher.DefaultStartupWindow, wine: wine);

    /// <summary>The path a program in the prefix of <see cref="PlaceWrapper"/> uses for a host path on drive z:.</summary>
    private static string OnDriveZ(string hostPath)
    {
        var full = Path.GetFullPath(hostPath);
        return @"Z:\" + string.Join('\\', full[Path.GetPathRoot(full)!.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Launch_WindowsBuildInAWrapperOnMacOs_StartsTheDefaultLaunchThroughTheWrapperWithOnlyTheVariable()
    {
        var executable = PlaceStarMap();
        PlaceStarMap("StarMap.dll");
        var wine = PlaceWrapper();
        var platforms = new Dictionary<OsPlatform, LoaderPlatformLaunch> { [OsPlatform.MacOs] = new("StarMap.dll", "dotnet") };
        using var launcher = WrapperLauncher(wine);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides(platforms: platforms)));

        Assert.True(result.Started);
        var plan = Assert.Single(_starter.Plans);
        Assert.Same(plan, result.Plan);
        Assert.Equal(Path.Combine(_tempRoot, "Kitten Space Agency.app", "Contents", "MacOS", "launcher"), plan.Executable);
        Assert.Equal([executable], plan.Arguments);
        Assert.Equal(Path.GetFullPath(StarMapDirectory), plan.WorkingDirectory);
        var variable = Assert.Single(plan.EnvironmentVariables);
        Assert.Equal("STARMAP_INSTANCE_PATH", variable.Key);
        Assert.Equal(OnDriveZ(_paths.GetInstanceRoot(_instance.InstanceId)), variable.Value);
        Assert.StartsWith(@"Z:", variable.Value);
        Assert.EndsWith(@"\Instances\" + _instance.InstanceId, variable.Value);
        Assert.Equal(OnDriveZ(SettingsGame), JsonNode.Parse(File.ReadAllText(ConfigFile))!["GameLocation"]!.GetValue<string>());
        Assert.True(launcher.IsRunning(_instance.InstanceId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Launch_WrapperAndLaunchArguments_StartsNothing(bool saved)
    {
        PlaceStarMap();
        using var launcher = WrapperLauncher(PlaceWrapper());
        if (saved)
            _instance.SetLaunchArguments(["-windowed"]);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()), saved ? null : ["-windowed"]);

        Assert.Equal(LaunchOutcome.WrapperArguments, result.Outcome);
        Assert.Contains("Kitten Space Agency.app", result.Message);
        Assert.NotNull(result.Wine?.Wrapper);
        Assert.Empty(_starter.Plans);
        Assert.False(launcher.IsRunning(_instance.InstanceId));
    }

    [Fact]
    public void Launch_WrapperAndAListingWithOnlyAFlag_StartsNothing()
    {
        PlaceStarMap();
        using var launcher = WrapperLauncher(PlaceWrapper());

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides(instance: new InstanceHandover("-InstancePath", null))));

        Assert.Equal(LaunchOutcome.WrapperNeedsVariable, result.Outcome);
        Assert.Contains("'-InstancePath'", result.Message);
        Assert.NotNull(result.Wine?.Wrapper);
        Assert.Empty(_starter.Plans);
    }

    [Fact]
    public void Launch_WrapperAndADotnetEntryForWindows_StartsNothing()
    {
        PlaceStarMap();
        PlaceStarMap("StarMap.dll");
        using var launcher = WrapperLauncher(PlaceWrapper());
        var platforms = new Dictionary<OsPlatform, LoaderPlatformLaunch> { [OsPlatform.Windows] = new("StarMap.dll", "dotnet") };

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides(platforms: platforms)));

        Assert.Equal(LaunchOutcome.WrapperRuntime, result.Outcome);
        Assert.Contains("'dotnet'", result.Message);
        Assert.Empty(_starter.Plans);
    }

    [Theory]
    [InlineData("")]
    [InlineData("loader")]
    [InlineData("loader instance")]
    public void Launch_WrapperAndAPathOnNoDrive_StartsNothingAndNamesThePath(string onADrive)
    {
        var executable = PlaceStarMap();
        var instanceRoot = Path.GetFullPath(_paths.GetInstanceRoot(_instance.InstanceId));
        var drives = new Dictionary<char, string> { ['c'] = Directory.CreateDirectory(Path.Combine(_tempRoot, "drive_c")).FullName };
        if (onADrive.Contains("loader"))
            drives['d'] = Path.GetFullPath(StarMapDirectory);
        if (onADrive.Contains("instance"))
            drives['e'] = Directory.CreateDirectory(_paths.GetInstancesRoot()).FullName;
        using var launcher = WrapperLauncher(PlaceWrapper(drives));

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        var unmapped = onADrive switch
        {
            "" => executable,
            "loader" => instanceRoot,
            _ => SettingsGame,
        };
        Assert.Equal(LaunchOutcome.PathOutsidePrefix, result.Outcome);
        Assert.Equal(unmapped, result.UnmappedPath);
        Assert.Contains($"'{unmapped}'", result.Message);
        Assert.Empty(_starter.Plans);
        Assert.False(File.Exists(ConfigFile));
        Assert.False(launcher.IsRunning(_instance.InstanceId));
    }

    private const string NeedsDotnet10 = """
        {
          "runtimeOptions": {
            "tfm": "net10.0",
            "framework": { "name": "Microsoft.NETCore.App", "version": "10.0.0" }
          }
        }
        """;

    /// <summary>The runtimeconfig.json of StarMap.exe.</summary>
    private void PlaceRuntimeConfig(string text = NeedsDotnet10) =>
        File.WriteAllText(Path.Combine(StarMapDirectory, "StarMap.runtimeconfig.json"), text);

    /// <summary>A shared framework folder below a dotnet folder of drive c: of the prefix at the temp root.</summary>
    private void PlaceRuntime(string version, string dotnet = @"Program Files\dotnet", string framework = "Microsoft.NETCore.App") =>
        Directory.CreateDirectory(Path.Combine([_tempRoot, "drive_c", .. dotnet.Split('\\'), "shared", framework, version]));

    [Theory]
    [InlineData(null, "Microsoft.NETCore.App")]
    [InlineData("9.0.8", "Microsoft.NETCore.App")]
    [InlineData("10.1.0-rc.1.25451.107", "Microsoft.NETCore.App")]
    [InlineData("10.0.12", "Microsoft.WindowsDesktop.App")]
    public void Launch_WrapperWhosePrefixLacksTheRuntimeOfTheLoader_StartsNothingAndSaysHowToInstallIt(string? installed, string framework)
    {
        PlaceStarMap();
        PlaceRuntimeConfig();
        using var launcher = WrapperLauncher(PlaceWrapper());
        if (installed is not null)
            PlaceRuntime(installed, framework: framework);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.Equal(LaunchOutcome.WrapperRuntimeMissing, result.Outcome);
        Assert.Equal("10.0.0", result.MissingRuntime?.Version.ToString());
        Assert.Equal("https://dotnet.microsoft.com/download/dotnet/10.0", result.MissingRuntime?.DownloadPage.AbsoluteUri);
        Assert.NotNull(result.Wine?.Wrapper);
        Assert.Contains(".NET runtime 10.0.0 for Windows x64", result.Message);
        Assert.Contains("Kitten Space Agency.app", result.Message);
        Assert.Contains("Install Software", result.Message);
        Assert.Contains("https://dotnet.microsoft.com/download/dotnet/10.0", result.Message);
        Assert.Empty(_starter.Plans);
        Assert.False(launcher.IsRunning(_instance.InstanceId));
    }

    [Theory]
    [InlineData("10.0.0")]
    [InlineData("10.0.12")]
    [InlineData("10.3.1")]
    public void Launch_WrapperWithAFittingRuntimeInProgramFiles_StartsTheLoader(string installed)
    {
        var executable = PlaceStarMap();
        PlaceRuntimeConfig();
        using var launcher = WrapperLauncher(PlaceWrapper());
        PlaceRuntime("9.0.8");
        PlaceRuntime(installed);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.True(result.Started);
        Assert.Equal([executable], Assert.Single(_starter.Plans).Arguments);
    }

    [Theory]
    [InlineData(@"Software\\dotnet\\Setup\\InstalledVersions\\x64")]
    [InlineData(@"Software\\Wow6432Node\\dotnet\\Setup\\InstalledVersions\\x64")]
    public void Launch_WrapperWithARuntimeThatOnlySystemRegNames_StartsTheLoader(string key)
    {
        PlaceStarMap();
        PlaceRuntimeConfig();
        using var launcher = WrapperLauncher(PlaceWrapper());
        PlaceRuntime("10.0.3", dotnet: "dotnet-x64");
        File.WriteAllText(
            Path.Combine(_tempRoot, "system.reg"),
            $"WINE REGISTRY Version 2\n;; All keys relative to \\\\Machine\n\n#arch=win64\n\n[{key}] 1727000000\n#time=1db0f6a1c2d3e4f\n\"InstallLocation\"=\"C:\\\\dotnet-x64\\\\\"\n");

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.True(result.Started);
        Assert.Single(_starter.Plans);
    }

    [Fact]
    public void Launch_WrapperWithARuntimeForAnotherArchitectureInSystemReg_StartsNothing()
    {
        PlaceStarMap();
        PlaceRuntimeConfig();
        using var launcher = WrapperLauncher(PlaceWrapper());
        PlaceRuntime("10.0.3", dotnet: "dotnet-arm64");
        File.WriteAllText(
            Path.Combine(_tempRoot, "system.reg"),
            "WINE REGISTRY Version 2\n\n[Software\\\\dotnet\\\\Setup\\\\InstalledVersions\\\\arm64] 1727000000\n\"InstallLocation\"=\"C:\\\\dotnet-arm64\\\\\"\n");

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.Equal(LaunchOutcome.WrapperRuntimeMissing, result.Outcome);
        Assert.Empty(_starter.Plans);
    }

    [Fact]
    public void Launch_AgainAfterThePlayerInstalledTheRuntime_StartsTheLoader()
    {
        PlaceStarMap();
        PlaceRuntimeConfig();
        using var launcher = WrapperLauncher(PlaceWrapper());
        var listing = LoaderListing(provides: StarMapProvides());
        Assert.Equal(LaunchOutcome.WrapperRuntimeMissing, launcher.Launch(_instance, listing).Outcome);

        PlaceRuntime("10.0.12");
        var again = launcher.Launch(_instance, listing);

        Assert.True(again.Started);
        Assert.Single(_starter.Plans);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{ "runtimeOptions": { "tfm": "net10.0", "includedFrameworks": [ { "name": "Microsoft.NETCore.App", "version": "10.0.10" } ] } }""")]
    [InlineData("""{ "runtimeOptions": { "framework": """)]
    public void Launch_WrapperAndALoaderThatNamesNoSharedRuntime_StartsTheLoader(string? runtimeConfig)
    {
        PlaceStarMap();
        if (runtimeConfig is not null)
            PlaceRuntimeConfig(runtimeConfig);
        using var launcher = WrapperLauncher(PlaceWrapper());

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.True(result.Started);
        Assert.Single(_starter.Plans);
    }

    [Theory]
    [InlineData(OsPlatform.Windows, false)]
    [InlineData(OsPlatform.Linux, true)]
    public void Launch_HostThatRunsTheGameItself_StartsTheSamePlanWithoutLookingForARuntime(OsPlatform platform, bool linuxAppHost)
    {
        var executable = PlaceStarMap();
        PlaceRuntimeConfig();
        WineFixtures.Prefix(_tempRoot);
        PlaceWindowsBuild(_paths, linuxAppHost);
        var instanceRoot = Path.GetFullPath(_paths.GetInstanceRoot(_instance.InstanceId));
        using var launcher = new LoaderLauncher(_paths, _starter, platform, NoDotnetNeeded, LoaderLauncher.DefaultStartupWindow, wine: PlaceWrapper());

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.True(result.Started);
        var plan = Assert.Single(_starter.Plans);
        Assert.Equal(executable, plan.Executable);
        Assert.Equal(["-InstancePath", instanceRoot], plan.Arguments);
        Assert.Equal(instanceRoot, Assert.Single(plan.EnvironmentVariables).Value);
        Assert.Equal(Path.GetFullPath(StarMapDirectory), plan.WorkingDirectory);
    }

    [Fact]
    public void Launch_SecondInstanceThroughTheSameWrapperWhileTheFirstRuns_IsRefused()
    {
        PlaceStarMap();
        var listing = LoaderListing(provides: StarMapProvides());
        var other = new Instance("Other", InstanceSource.Custom.Value);
        using var launcher = WrapperLauncher(PlaceWrapper());
        Assert.True(launcher.Launch(_instance, listing).Started);

        var second = launcher.Launch(other, listing);

        Assert.Equal(LaunchOutcome.WrapperBusy, second.Outcome);
        Assert.Contains("Kitten Space Agency.app", second.Message);
        Assert.Single(_starter.Plans);
        Assert.False(launcher.IsRunning(other.InstanceId));

        Assert.Single(_starter.Processes).HasExited = true;

        Assert.True(launcher.Launch(other, listing).Started);
    }

    [Fact]
    public void Launch_InstanceWhileAStartWithoutALoaderRunsThroughTheWrapper_IsRefused()
    {
        PlaceStarMap();
        var wine = PlaceWrapper();
        var launches = new RunningLaunches();
        var shared = new SharedProfileLauncher(_paths, _starter, OsPlatform.MacOs, wine, launches);
        using var launcher = new LoaderLauncher(_paths, _starter, OsPlatform.MacOs, NoDotnetNeeded, LoaderLauncher.DefaultStartupWindow, launches, wine: wine);
        var listing = LoaderListing(provides: StarMapProvides());
        Assert.True(shared.Launch().Started);
        Assert.False(Assert.Single(_starter.Processes).Disposed);

        var result = launcher.Launch(_instance, listing);

        Assert.Equal(LaunchOutcome.WrapperBusy, result.Outcome);
        Assert.Single(_starter.Plans);

        _starter.Processes[0].HasExited = true;

        Assert.True(launcher.Launch(_instance, listing).Started);
        Assert.True(_starter.Processes[0].Disposed);
    }

    [Fact]
    public void LaunchWithoutALoader_WhileAGameRunsThroughTheWrapper_IsRefused()
    {
        PlaceStarMap();
        var wine = PlaceWrapper();
        var launches = new RunningLaunches();
        var shared = new SharedProfileLauncher(_paths, _starter, OsPlatform.MacOs, wine, launches);
        using var launcher = new LoaderLauncher(_paths, _starter, OsPlatform.MacOs, NoDotnetNeeded, LoaderLauncher.DefaultStartupWindow, launches, wine: wine);
        Assert.True(launcher.Launch(_instance, LoaderListing(provides: StarMapProvides())).Started);

        var whileTheInstanceRuns = shared.Launch();

        Assert.Equal(SharedProfileLaunchOutcome.WrapperBusy, whileTheInstanceRuns.Outcome);
        Assert.Contains("Kitten Space Agency.app", whileTheInstanceRuns.Message);
        Assert.NotNull(whileTheInstanceRuns.Wine?.Wrapper);
        Assert.Single(_starter.Plans);

        _starter.Processes[0].HasExited = true;
        Assert.True(shared.Launch().Started);

        Assert.Equal(SharedProfileLaunchOutcome.WrapperBusy, shared.Launch().Outcome);
        Assert.Equal(2, _starter.Plans.Count);
    }

    [Fact]
    public void Launch_DotnetEntryOnANativeMacOsInstall_StillStartsDotnet()
    {
        PlaceStarMap();
        var assembly = PlaceStarMap("StarMap.dll");
        var dotnet = PlaceDotnet();
        var platforms = new Dictionary<OsPlatform, LoaderPlatformLaunch> { [OsPlatform.MacOs] = new("StarMap.dll", "dotnet") };
        Directory.CreateDirectory(_paths.GetGameDirectoryPath()!);
        using var launcher = new LoaderLauncher(_paths, _starter, OsPlatform.MacOs, () => dotnet, LoaderLauncher.DefaultStartupWindow, wine: PlaceWrapperProbeWithoutTheGame());

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides(platforms: platforms)));

        Assert.True(result.Started);
        var plan = Assert.Single(_starter.Plans);
        Assert.Equal(dotnet, plan.Executable);
        Assert.Equal(assembly, plan.Arguments[0]);
    }

    /// <summary>A probe that finds a wrapper around the game folder, which holds no game build.</summary>
    private FakeWineProbe PlaceWrapperProbeWithoutTheGame()
    {
        var bundle = Path.Combine(Path.GetFullPath(_tempRoot), "Kitten Space Agency.app");
        var drives = WineFixtures.Drives(new Dictionary<char, string> { ['z'] = Path.GetPathRoot(Path.GetFullPath(_tempRoot))! });
        return new FakeWineProbe(new WineInstall(Path.GetFullPath(_tempRoot), drives, new WineWrapper(bundle, Path.Combine(bundle, "Contents", "MacOS", "launcher"))));
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

    [Theory]
    [InlineData("mono", null, LaunchOutcome.UnknownRuntime, "mono")]
    [InlineData("dotnet", new[] { "arch" }, LaunchOutcome.UnknownPlatformKey, "arch")]
    public void Launch_UnknownRuntimeOrKeyInTheOwnEntry_StartsNothing(string runtime, string[]? unknownKeys, LaunchOutcome expected, string unknownName)
    {
        PlaceStarMap();
        PlaceStarMap("StarMap.dll");
        var platforms = new Dictionary<OsPlatform, LoaderPlatformLaunch> { [OsPlatform.Linux] = new("StarMap.dll", runtime, unknownKeys) };
        using var launcher = new LoaderLauncher(_paths, _starter, OsPlatform.Linux, NoDotnetNeeded);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides(platforms: platforms)));

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(unknownName, result.UnknownName);
        Assert.Contains($"'{unknownName}'", result.Message);
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
    public void Launch_GivenVersionFolder_StartsTheLoaderFromItAndNotFromTheRecordedFolder()
    {
        PlaceStarMap();
        var version = Path.Combine(_tempRoot, "Loaders", "StarMap", "0.4.7");
        Directory.CreateDirectory(version);
        File.WriteAllBytes(Path.Combine(version, "StarMap.exe"), []);

        var result = _launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()), version, arguments: null);

        Assert.True(result.Started);
        var plan = Assert.Single(_starter.Plans);
        Assert.Equal(Path.Combine(Path.GetFullPath(version), "StarMap.exe"), plan.Executable);
        Assert.Equal(Path.GetFullPath(version), plan.WorkingDirectory);
    }

    [Fact]
    public void Launch_GivenVersionFolderMissing_StartsNothingEvenWithARecordedFolder()
    {
        PlaceStarMap();
        var version = Path.Combine(_tempRoot, "Loaders", "StarMap", "0.4.7");

        var result = _launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()), version, arguments: null);

        Assert.Equal(LaunchOutcome.NoLoaderDirectory, result.Outcome);
        Assert.Contains(version, result.Message);
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

    private static readonly GameVersion KeptVersion = GameVersion.Parse("2026.8.3.5117");

    private string ConfigFile => Path.Combine(StarMapDirectory, "StarMapConfig.json");

    private string SettingsGame => Path.GetFullPath(_paths.GetGameDirectoryPath()!);

    /// <summary>A real game assembly as KSA.dll in <paramref name="folder"/>, which reads as <see cref="KeptVersion"/>.</summary>
    private static string PlaceGameAssembly(string folder)
    {
        Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "GameVersionFixture.dll"), Path.Combine(folder, "KSA.dll"));
        return Path.GetFullPath(folder);
    }

    /// <summary>Puts <see cref="KeptVersion"/> into its folder in the library and lets the instance run on it.</summary>
    private string KeepBuild()
    {
        var folder = PlaceGameAssembly(_paths.GetGameBuildFolder(KeptVersion));
        _instance.SetGameBuild(KeptVersion);
        return folder;
    }

    private void WriteConfig(object content) => File.WriteAllText(ConfigFile, JsonSerializer.Serialize(content));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Launch_FileNamesAnotherFolder_WritesTheFolderOfTheInstance(bool keptBuild)
    {
        PlaceStarMap();
        WriteConfig(new { GameLocation = Path.Combine(_tempRoot, "Other Game"), Other = "kept" });
        var expected = keptBuild ? KeepBuild() : SettingsGame;
        using var launcher = LauncherSeeingGame(() => false);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.True(result.Started);
        var config = JsonNode.Parse(File.ReadAllText(ConfigFile))!;
        Assert.Equal(expected, config["GameLocation"]!.GetValue<string>());
        Assert.Equal("kept", config["Other"]!.GetValue<string>());
    }

    [Fact]
    public void Launch_FileAlreadyNamesTheFolder_WritesNothingAndStartsWhileAGameRuns()
    {
        PlaceStarMap();
        // a write would indent the compact text
        WriteConfig(new { GameLocation = SettingsGame + Path.DirectorySeparatorChar, Other = "kept" });
        var before = File.ReadAllBytes(ConfigFile);
        using var launcher = LauncherSeeingGame(() => true);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.True(result.Started);
        Assert.Equal(before, File.ReadAllBytes(ConfigFile));
    }

    [Fact]
    public void Launch_GameRunsAndTheFileNamesAnotherFolder_IsRefusedAndWritesNothing()
    {
        PlaceStarMap();
        var other = Path.Combine(_tempRoot, "Other Game");
        WriteConfig(new { GameLocation = other });
        var before = File.ReadAllBytes(ConfigFile);
        KeepBuild();
        using var launcher = LauncherSeeingGame(() => true);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.Equal(LaunchOutcome.OtherBuildRunning, result.Outcome);
        Assert.Contains($"'{other}'", result.Message);
        Assert.Empty(_starter.Plans);
        Assert.Equal(before, File.ReadAllBytes(ConfigFile));
        Assert.False(launcher.IsRunning(_instance.InstanceId));
    }

    [Theory]
    [InlineData("letter case")]
    [InlineData("separators")]
    [InlineData("KSA.dll")]
    public void Launch_GameRunsAndTheFileNamesTheFolderInAnotherForm_StartsAndWritesNothing(string form)
    {
        PlaceStarMap();
        var held = form switch
        {
            "letter case" => SettingsGame.ToUpperInvariant(),
            "separators" => Path.Combine(Path.GetDirectoryName(SettingsGame)!, ".", Path.GetFileName(SettingsGame)).Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.AltDirectorySeparatorChar,
            _ => Path.Combine(SettingsGame, "KSA.dll"),
        };
        WriteConfig(new { GameLocation = held });
        var before = File.ReadAllBytes(ConfigFile);
        using var launcher = LauncherSeeingGame(() => true);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        // the file systems of Linux tell letter case apart
        if (form == "letter case" && OperatingSystem.IsLinux())
        {
            Assert.Equal(LaunchOutcome.OtherBuildRunning, result.Outcome);
        }
        else
        {
            Assert.True(result.Started, result.Message);
        }

        Assert.Equal(before, File.ReadAllBytes(ConfigFile));
    }

    [Fact]
    public void Launch_FileThatIsNoJson_IsRefusedByNameAndStartsNothing()
    {
        PlaceStarMap();
        File.WriteAllText(ConfigFile, "{ \"GameLocation\": ");
        using var launcher = LauncherSeeingGame(() => false);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.Equal(LaunchOutcome.LoaderConfigurationUnreadable, result.Outcome);
        Assert.Contains($"'{ConfigFile}'", result.Message);
        Assert.Contains("Repair or delete the file", result.Message);
        Assert.Equal(ConfigFile, result.ConfigurationFile);
        Assert.Empty(_starter.Plans);
        Assert.Equal("{ \"GameLocation\": ", File.ReadAllText(ConfigFile));
        Assert.False(launcher.IsRunning(_instance.InstanceId));
    }

    [Fact]
    public void Launch_AdoptedFolderNamesAnotherGame_KeepsTheValueOfThePlayerWhileAGameRuns()
    {
        PlaceStarMap();
        WriteConfig(new { GameLocation = PlaceGameAssembly(Path.Combine(_tempRoot, "Other Game")) });
        var before = File.ReadAllBytes(ConfigFile);
        using var launcher = LauncherSeeingGame(() => true);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()), StarMapDirectory, arguments: null, isAdopted: true);

        Assert.True(result.Started, result.Message);
        Assert.Equal(before, File.ReadAllBytes(ConfigFile));
    }

    [Fact]
    public void Launch_AdoptedFolderNamesTheGameByARelativePath_KeepsTheValueOfThePlayerWhileAGameRuns()
    {
        PlaceStarMap();
        // StarMap reads a relative path against its own folder, where Borea starts it
        WriteConfig(new { GameLocation = Path.GetRelativePath(StarMapDirectory, PlaceGameAssembly(SettingsGame)) });
        var before = File.ReadAllBytes(ConfigFile);
        using var launcher = LauncherSeeingGame(() => true);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()), StarMapDirectory, arguments: null, isAdopted: true);

        Assert.True(result.Started, result.Message);
        Assert.Equal(before, File.ReadAllBytes(ConfigFile));
    }

    [Theory]
    [InlineData("no game")]
    [InlineData("kept folder")]
    [InlineData("instance on a kept build")]
    public void Launch_AdoptedFolderWithAValueThatCannotStay_GetsTheFolderOfTheInstance(string held)
    {
        PlaceStarMap();
        var expected = SettingsGame;
        var value = held switch
        {
            "no game" => Directory.CreateDirectory(Path.Combine(_tempRoot, "Empty")).FullName,
            "kept folder" => PlaceGameAssembly(_paths.GetGameBuildFolder(KeptVersion)),
            _ => PlaceGameAssembly(Path.Combine(_tempRoot, "Other Game")),
        };
        if (held == "instance on a kept build")
            expected = KeepBuild();

        WriteConfig(new { GameLocation = value });
        using var launcher = LauncherSeeingGame(() => false);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()), StarMapDirectory, arguments: null, isAdopted: true);

        Assert.True(result.Started, result.Message);
        Assert.Equal(expected, JsonNode.Parse(File.ReadAllText(ConfigFile))!["GameLocation"]!.GetValue<string>());
    }

    [UnixFact("On Windows a host path starts with a drive letter, so the loader in the prefix reads it as a path of the prefix.")]
    public void Launch_AdoptedFolderInAPrefixNamesTheGameByItsHostPath_GetsThePathOfThePrefix()
    {
        PlaceStarMap();
        using var launcher = new LoaderLauncher(_paths, _starter, OsPlatform.MacOs, NoDotnetNeeded, LoaderLauncher.DefaultStartupWindow, isGameRunning: () => false, wine: PlaceWrapper());
        // an older Borea wrote the host path, which the loader in the prefix cannot open
        WriteConfig(new { GameLocation = PlaceGameAssembly(SettingsGame) });

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()), StarMapDirectory, arguments: null, isAdopted: true);

        Assert.True(result.Started, result.Message);
        Assert.Equal(OnDriveZ(SettingsGame), JsonNode.Parse(File.ReadAllText(ConfigFile))!["GameLocation"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(LaunchOutcome.GameBuildMissing)]
    [InlineData(LaunchOutcome.LoaderTakesNoGamePath)]
    public void Launch_KeptBuildTheLoaderCannotStart_StartsNothingAndWritesNothing(LaunchOutcome expected)
    {
        PlaceStarMap();
        // the settings folder holds a good build, so a fallback to it would start
        PlaceGameAssembly(SettingsGame);
        var provides = StarMapProvides();
        if (expected == LaunchOutcome.GameBuildMissing)
        {
            _instance.SetGameBuild(KeptVersion);
        }
        else
        {
            KeepBuild();
            provides = new LoaderProvides(launch: "StarMap.exe", contentDir: InstallAnchor.Mods, instance: provides.Instance);
        }

        using var launcher = LauncherSeeingGame(() => false);

        var result = launcher.Launch(_instance, LoaderListing(provides: provides));

        Assert.Equal(expected, result.Outcome);
        Assert.Contains(KeptVersion.ToString(), result.Message);
        Assert.Empty(_starter.Plans);
        Assert.False(File.Exists(ConfigFile));
        Assert.False(launcher.IsRunning(_instance.InstanceId));
    }

    [Fact]
    public void Launch_LoaderWithoutAGamePath_StillStartsAnInstanceWithoutAKeptBuild()
    {
        PlaceStarMap();
        var provides = new LoaderProvides(launch: "StarMap.exe", contentDir: InstallAnchor.Mods, instance: new InstanceHandover("-InstancePath", null));
        using var launcher = LauncherSeeingGame(() => false);

        var result = launcher.Launch(_instance, LoaderListing(provides: provides));

        Assert.True(result.Started);
        Assert.False(File.Exists(ConfigFile));
    }

    [Fact]
    public void Launch_RelativeGameFolderInTheSettings_StartsAndWritesNothing()
    {
        PlaceStarMap();
        // a hand-edited settings file can hold a relative game folder
        var paths = new GamePathProvider("KSA", new Dictionary<string, string> { ["StarMap"] = StarMapDirectory }, boreaRoot: _tempRoot);
        using var launcher = new LoaderLauncher(paths, _starter, SharedProfileLauncher.CurrentPlatform(), NoDotnetNeeded, LoaderLauncher.DefaultStartupWindow, isGameRunning: () => false);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.True(result.Started);
        Assert.False(File.Exists(ConfigFile));
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
    public void IsRunning_LoaderExitedWhileItsRestartRuns_KeepsTheLaunchUntilTheRestartEnds()
    {
        PlaceStarMap();
        using var launcher = LauncherSeeingGame(() => true);
        var listing = LoaderListing(provides: StarMapProvides());
        launcher.Launch(_instance, listing);
        var process = Assert.Single(_starter.Processes);
        process.HasExited = true;
        process.ExitCode = 0;
        process.HasEnded = false;

        Assert.True(launcher.IsRunning(_instance.InstanceId));
        Assert.Equal(LaunchOutcome.AlreadyRunning, launcher.Launch(_instance, listing).Outcome);
        Assert.False(process.Disposed);

        process.HasEnded = true;

        Assert.False(launcher.IsRunning(_instance.InstanceId));
        Assert.True(process.Disposed);
    }

    [Fact]
    public void IsRunning_LoaderExitedWhileAProcessThatIsNoGameHoldsItsStreams_EndsTheLaunch()
    {
        // for example a browser that the game opened before it closed
        PlaceStarMap();
        using var launcher = LauncherSeeingGame(() => false);
        var listing = LoaderListing(provides: StarMapProvides());
        launcher.Launch(_instance, listing);
        var process = Assert.Single(_starter.Processes);
        process.HasExited = true;
        process.ExitCode = 0;
        process.HasEnded = false;

        Assert.False(launcher.IsRunning(_instance.InstanceId));
        Assert.True(process.Disposed);
        Assert.True(launcher.Launch(_instance, listing).Started);
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

        var result = await CrashAsync(instance, 1, KsArmoryCrash);

        Assert.Equal("kessler-armory", result.BlamedModId);
    }

    [Fact]
    public async Task WatchStart_ErrorNamingNoInstalledMod_ReportsTheExitCode()
    {
        PlaceStarMap();
        var instance = InstanceWith("MeasureTools");

        var result = await CrashAsync(instance, 3, "Could not load file or assembly '0Harmony, Version=2.3.0.0'.");

        Assert.Equal(LaunchOutcome.ExitedEarly, result.Outcome);
        Assert.Null(result.BlamedModId);
        Assert.Contains("exit code 3", result.Message);
    }

    private static readonly string[] StarMap5638Crash =
    [
        "Unhandled exception. System.Reflection.ReflectionTypeLoadException: Unable to load one or more of the requested types.",
        "Could not load file or assembly 'Brutal.Core.Numerics, Version=2026.9.0.0, Culture=neutral, PublicKeyToken=null'. The system cannot find the file specified.",
        "   at System.Reflection.RuntimeModule.GetDefinedTypes()",
        "   at StarMap.GameSurveyer.TryLoadCoreAndGame()",
        "   at StarMap.Program.Main(String[] args)",
    ];

    [Fact]
    public async Task WatchStart_LoaderCannotLoadAGameAssembly_SaysItWasMadeForAnotherBuild()
    {
        PlaceStarMap();
        var game = Directory.CreateDirectory(_paths.GetGameDirectoryPath()!).FullName;
        File.Copy(Path.Combine(AppContext.BaseDirectory, "GameVersionFixture.dll"), Path.Combine(game, "KSA.dll"));
        var instance = InstanceWith("MeasureTools");
        var releases = new ReleaseSource([new ReleaseHost("github", "StarMapLoader/StarMap")]);
        var started = _launcher.Launch(instance, LoaderListing(provides: StarMapProvides(), releases: releases));
        var process = Assert.Single(_starter.Processes);
        process.Output.AddRange(StarMap5638Crash);
        process.HasExited = true;
        process.ExitCode = -532462766;

        var result = await _launcher.WatchStartAsync(instance, started);

        Assert.Equal(LoaderCrashCause.GameAssembly, result.CrashCause);
        Assert.Null(result.BlamedModId);
        Assert.Contains("2026.8.3.5117", result.Message);
        Assert.Contains("https://github.com/StarMapLoader/StarMap/releases", result.Message);
    }

    [Fact]
    public async Task WatchStart_KeptBuildCannotBeLoaded_NamesTheKeptBuild()
    {
        PlaceStarMap();
        // the settings folder holds another build, which the text must not name
        var game = Directory.CreateDirectory(_paths.GetGameDirectoryPath()!).FullName;
        File.Copy(Path.Combine(AppContext.BaseDirectory, "LoaderVersionFixture.dll"), Path.Combine(game, "KSA.dll"));
        KeepBuild();

        var result = await CrashAsync(_instance, -532462766, StarMap5638Crash);

        Assert.Equal(LoaderCrashCause.GameAssembly, result.CrashCause);
        Assert.Contains(KeptVersion.ToString(), result.Message);
        Assert.DoesNotContain("0.4.6.0", result.Message);
        Assert.DoesNotContain("update KSA", result.Message);
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
    public async Task WatchStart_GameAssemblyNamedWhileLoadingMods_KeepsTheModLoadingCause()
    {
        PlaceStarMap();
        var instance = InstanceWith("KSArmory");

        var result = await CrashAsync(instance, -532462766, ["Could not load file or assembly 'Brutal.Core.Numerics, Version=2026.9.0.0'.", .. ClrCrash]);

        Assert.Equal(LoaderCrashCause.ModLoading, result.CrashCause);
    }

    private const string MissingNumerics = "Could not load file or assembly 'Brutal.Core.Numerics, Version=2026.9.0.0, Culture=neutral, PublicKeyToken=null'. The system cannot find the file specified.";

    // what StarMap 0.4.8 wrote on KSA 2026.10.19.5638 when GetTypes failed on a mod built for an older build
    private static readonly string[] StaleModCrash =
    [
        "StarMap - Using Instance Path: C:\\Instances\\Main",
        "StarMap - Delaying load of mod: AdvancedFlightComputer due to missing dependencies: KittenExtensions",
        "Unhandled exception. System.Reflection.ReflectionTypeLoadException: Unable to load one or more of the requested types.",
        MissingNumerics,
        MissingNumerics,
        "   at System.Reflection.RuntimeModule.GetDefinedTypes()",
        "   at System.Reflection.RuntimeModule.GetTypes()",
        "   at StarMap.Core.ModRepository.RuntimeMod.InitializeMod(ModRegistry modRegistry)",
        "   at StarMap.Core.ModRepository.ModLoader.PrepareMods()",
        "   at StarMap.Core.ModRepository.ModLoader.Init()",
        "   at StarMap.Core.StarMapCore.Init()",
        "   at StarMap.GameSurveyer.TryLoadCoreAndGame()",
        "   at StarMap.Program.Main(String[] args)",
        "System.IO.FileNotFoundException: " + MissingNumerics,
        "File name: 'Brutal.Core.Numerics, Version=2026.9.0.0, Culture=neutral, PublicKeyToken=null'",
    ];

    // the last 200 lines of the same error for a mod with many types, which hold no frame and no StarMap line
    private static readonly string[] CutStaleModCrash = Enumerable.Range(0, 100)
        .SelectMany(_ => new[] { "System.IO.FileNotFoundException: " + MissingNumerics, "File name: 'Brutal.Core.Numerics, Version=2026.9.0.0, Culture=neutral, PublicKeyToken=null'" })
        .ToArray();

    /// <summary>Places a mod whose entry assembly references the named assemblies, and the manifest that enables the mods in this order.</summary>
    private void PlaceCodeMods(Instance instance, params (string ModId, string[] References)[] mods)
    {
        foreach (var (modId, references) in mods)
        {
            PlaceMod(instance, modId, $"name = \"{modId}\"");
            ReferencingAssembly.Write(Path.Combine(_paths.GetInstanceModsFolder(instance.InstanceId), modId, modId + ".dll"), references);
        }

        WriteManifest(instance, string.Join("\n\n", mods.Select(mod => $"[[mods]]\nid = \"{mod.ModId}\"")));
    }

    [Fact]
    public async Task WatchStart_CutErrorOfAModThatNeedsAMissingGameAssembly_BlamesThatModAndNotTheLoader()
    {
        PlaceStarMap();
        ReferencingAssembly.Write(Path.Combine(StarMapDirectory, "StarMap.Core.dll"), "KSA", "Brutal.Numerics");
        var instance = InstanceWith("ModMenu", "KSArmory");
        PlaceCodeMods(instance, ("ModMenu", ["KSA", "Brutal.Core.Logging"]), ("KSArmory", ["KSA", "Brutal.Core.Numerics"]));

        var result = await CrashAsync(instance, -532462766, CutStaleModCrash);

        Assert.Equal(LoaderCrashCause.ModAssembly, result.CrashCause);
        Assert.Equal("KSArmory", result.BlamedModId);
        Assert.Empty(result.OtherBlamedModIds);
        Assert.Contains("KSArmory 0.8.44 stopped StarMap from starting.", result.Message);
    }

    [Fact]
    public async Task WatchStart_MissingGameAssemblyThatTheLoaderNeeds_SaysItWasMadeForAnotherBuild()
    {
        PlaceStarMap();
        ReferencingAssembly.Write(Path.Combine(StarMapDirectory, "StarMap.Core.dll"), "KSA", "Brutal.Core.Numerics");
        var instance = InstanceWith("KSArmory");
        PlaceCodeMods(instance, ("KSArmory", ["KSA", "Brutal.Core.Numerics"]));

        var result = await CrashAsync(instance, -532462766, CutStaleModCrash);

        Assert.Equal(LoaderCrashCause.GameAssembly, result.CrashCause);
        Assert.Null(result.BlamedModId);
        Assert.Empty(result.OtherBlamedModIds);
    }

    [Fact]
    public async Task WatchStart_GameAssemblyNamedWithoutAFrame_BlamesNeitherTheLoaderNorAMod()
    {
        PlaceStarMap();
        var instance = InstanceWith("MeasureTools");
        PlaceCodeMods(instance, ("MeasureTools", ["KSA", "Brutal.Numerics"]));

        var result = await CrashAsync(instance, -532462766, CutStaleModCrash);

        Assert.Equal(LoaderCrashCause.Unknown, result.CrashCause);
        Assert.Null(result.BlamedModId);
        Assert.Contains("StarMap stopped right after starting", result.Message);
    }

    [Fact]
    public async Task WatchStart_ModsThatNeedAMissingAssemblyWhileLoadingMods_BlamesTheLoadingModAndNamesTheOthers()
    {
        PlaceStarMap();
        var instance = InstanceWith("AdvancedFlightComputer", "Compendium", "DeltaVMap", "ModMenu");
        PlaceCodeMods(
            instance,
            ("AdvancedFlightComputer", ["KSA", "Brutal.Core.Numerics"]),
            ("Compendium", ["KSA", "Brutal.Core.Numerics"]),
            ("ModMenu", ["KSA", "Brutal.Core.Logging"]),
            ("DeltaVMap", ["KSA", "Brutal.Core.Numerics"]));

        var result = await CrashAsync(instance, -532462766, StaleModCrash);

        Assert.Equal(LoaderCrashCause.ModLoading, result.CrashCause);
        Assert.Equal("Compendium", result.BlamedModId);
        Assert.Equal(["AdvancedFlightComputer", "DeltaVMap"], result.OtherBlamedModIds);
        Assert.Contains("These mods need parts of KSA that this version does not have, so they will likely fail too: AdvancedFlightComputer, DeltaVMap.", result.Message);
    }

    [Fact]
    public async Task WatchStart_LoadingFrameWithoutTheInstancePath_BlamesTheFirstModThatNeedsTheMissingAssembly()
    {
        PlaceStarMap();
        var instance = InstanceWith("ModMenu", "DeltaVMap", "KSArmory");
        PlaceCodeMods(instance, ("ModMenu", ["KSA", "Brutal.Core.Logging"]), ("DeltaVMap", ["KSA", "Brutal.Core.Numerics"]), ("KSArmory", ["KSA", "Brutal.Core.Numerics"]));

        var result = await CrashAsync(instance, -532462766, [.. StaleModCrash.Skip(2)]);

        Assert.Equal(LoaderCrashCause.ModLoading, result.CrashCause);
        Assert.Equal("DeltaVMap", result.BlamedModId);
        Assert.Equal(["KSArmory"], result.OtherBlamedModIds);
    }

    [Fact]
    public async Task WatchStart_CutErrorWithModsMadeForAnotherBuild_NamesEveryModThatNeedsAGameAssemblyTheGameLacks()
    {
        PlaceStarMap();
        var game = Directory.CreateDirectory(_paths.GetGameDirectoryPath()!).FullName;
        foreach (var assembly in new[] { "KSA", "Brutal.Numerics", "Brutal.Logging" })
            ReferencingAssembly.Write(Path.Combine(game, assembly + ".dll"));
        var instance = InstanceWith("KSArmory", "ModMenu", "MeasureTools", "Helper");
        PlaceCodeMods(
            instance,
            ("KSArmory", ["KSA", "Brutal.Core.Numerics"]),
            ("ModMenu", ["KSA", "Brutal.Core.Logging"]),
            ("MeasureTools", ["KSA", "Brutal.Numerics", "KSA.Helper"]),
            ("Helper", ["KSA"]));
        ReferencingAssembly.Write(Path.Combine(_paths.GetInstanceModsFolder(instance.InstanceId), "Helper", "KSA.Helper.dll"));

        var result = await CrashAsync(instance, -532462766, CutStaleModCrash);

        Assert.Equal(LoaderCrashCause.ModAssembly, result.CrashCause);
        Assert.Equal("KSArmory", result.BlamedModId);
        Assert.Equal(["ModMenu"], result.OtherBlamedModIds);
        Assert.EndsWith("These mods need parts of KSA that this version does not have, so they will likely fail too: ModMenu.", result.Message);
    }

    [Fact]
    public async Task WatchStart_CutErrorThatNamesAMissingAssemblyOfNoGame_BlamesNoMod()
    {
        PlaceStarMap();
        ReferencingAssembly.Write(Path.Combine(StarMapDirectory, "0Harmony.dll"));
        var instance = InstanceWith("ModMenu", "KSArmory");
        PlaceCodeMods(instance, ("ModMenu", ["KSA", "0Harmony"]), ("KSArmory", ["KSA", "0Harmony"]));
        const string missingHarmony = "Could not load file or assembly '0Harmony, Version=2.4.0.0, Culture=neutral, PublicKeyToken=null'.";

        var result = await CrashAsync(instance, -532462766, [.. Enumerable.Repeat("System.IO.FileLoadException: " + missingHarmony, 100)]);

        Assert.Equal(LoaderCrashCause.Unknown, result.CrashCause);
        Assert.Null(result.BlamedModId);
        Assert.Empty(result.OtherBlamedModIds);
    }

    [Fact]
    public async Task WatchStart_CutErrorThatNamesAMissingAssemblyOfTheLoaderItself_DoesNotSayItWasMadeForAnotherBuild()
    {
        PlaceStarMap();
        ReferencingAssembly.Write(Path.Combine(StarMapDirectory, "StarMap.Core.dll"), "KSA", "StarMap.API");
        var instance = InstanceWith("KSArmory");

        var result = await CrashAsync(instance, -532462766, ["Could not load file or assembly 'StarMap.API, Version=0.4.8.0, Culture=neutral, PublicKeyToken=null'. The system cannot find the file specified."]);

        Assert.Equal(LoaderCrashCause.Unknown, result.CrashCause);
        Assert.Null(result.BlamedModId);
    }

    [Fact]
    public async Task WatchStart_LoaderSubfolderReferencesTheMissingAssembly_BlamesTheModAndNotTheLoader()
    {
        PlaceStarMap();
        ReferencingAssembly.Write(Path.Combine(StarMapDirectory, "StarMap.Core.dll"), "KSA", "Brutal.Numerics");
        ReferencingAssembly.Write(Path.Combine(StarMapDirectory, "Content", "OldMod", "OldMod.dll"), "KSA", "Brutal.Core.Numerics");
        var instance = InstanceWith("KSArmory");
        PlaceCodeMods(instance, ("KSArmory", ["KSA", "Brutal.Core.Numerics"]));

        var result = await CrashAsync(instance, -532462766, CutStaleModCrash);

        Assert.Equal(LoaderCrashCause.ModAssembly, result.CrashCause);
        Assert.Equal("KSArmory", result.BlamedModId);
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

    [Theory]
    [InlineData("System.IO.FileNotFoundException: Could not load file or assembly '/Users/me/mods/Foo/Foo.dll'. The system cannot find the file specified.")]
    [InlineData(@"System.IO.FileNotFoundException: Could not load file or assembly 'C:\Games\KSA\mods\Foo\Foo.dll'. The system cannot find the file specified.")]
    public async Task WatchStart_AssemblyNamedByItsPath_BlamesTheModThatHoldsIt(string line)
    {
        PlaceStarMap();
        var instance = InstanceWith("MeasureTools", "foo-tools");
        PlaceMod(instance, "MeasureTools", "name = \"MeasureTools\"", "MeasureTools");
        PlaceMod(instance, "foo-tools", "name = \"Foo\"", "Foo");

        var result = await CrashAsync(instance, -532462766, line);

        Assert.Equal(LaunchOutcome.ExitedEarly, result.Outcome);
        Assert.Equal("foo-tools", result.BlamedModId);
        Assert.Equal(LoaderCrashCause.ModAssembly, result.CrashCause);
        Assert.Contains("foo-tools 0.8.44 stopped StarMap from starting", result.Message);
    }

    [Theory]
    [InlineData("Could not load file or assembly '*'.")]
    [InlineData("Could not load file or assembly '/Users/me/mods/Foo/F?o.dll'.")]
    [InlineData(@"Could not load file or assembly 'C:\Games\KSA\mods\Foo\F*.dll'.")]
    public async Task WatchStart_AssemblyNameWithAWildcard_BlamesNoModAndReportsTheStop(string line)
    {
        PlaceStarMap();
        var instance = InstanceWith("foo-tools");
        PlaceMod(instance, "foo-tools", "name = \"Foo\"", "Foo");

        var result = await CrashAsync(instance, 3, line);

        Assert.Equal(LaunchOutcome.ExitedEarly, result.Outcome);
        Assert.Equal(3, result.ExitCode);
        Assert.Null(result.BlamedModId);
        Assert.Contains("StarMap stopped right after starting (exit code 3).", result.Message);
        Assert.Equal([line], result.Output);
    }

    [Fact]
    public async Task WatchStart_ModFolderThatCannotBeSearched_IsSkipped()
    {
        PlaceStarMap();
        var instance = InstanceWith("gone", "foo-tools");
        var modsFolder = _paths.GetInstanceModsFolder(instance.InstanceId);
        var removed = Directory.CreateDirectory(Path.Combine(_tempRoot, "Removed")).FullName;
        var link = Path.Combine(modsFolder, "gone");
        var linker = new DirectoryLinker();
        Assert.True(linker.TryCreate(link, removed).Linked);
        Directory.Delete(removed);
        PlaceMod(instance, "foo-tools", "name = \"Foo\"", "Foo");

        try
        {
            var result = await CrashAsync(instance, -532462766, "Could not load file or assembly 'Foo, Version=1.0.0.0'.");

            Assert.Equal(LaunchOutcome.ExitedEarly, result.Outcome);
            Assert.Equal("foo-tools", result.BlamedModId);
        }
        finally
        {
            linker.Remove(link);
        }
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
    public async Task WatchStart_LoaderExitsWithZeroWhileItsRestartRuns_StaysStarted()
    {
        PlaceStarMap();
        using var launcher = LauncherSeeingGame(() => true, TimeSpan.FromMilliseconds(100));
        var started = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));
        var process = Assert.Single(_starter.Processes);
        process.Output.Add("StarMap - RESTARTING");
        process.HasExited = true;
        process.ExitCode = 0;
        process.HasEnded = false;

        var result = await launcher.WatchStartAsync(_instance, started);

        Assert.True(result.Started);
        Assert.Equal(["StarMap - RESTARTING"], result.Output);
        Assert.True(launcher.IsRunning(_instance.InstanceId));
        Assert.Contains("restarted itself", File.ReadAllText(_paths.GetInstanceLaunchLogPath(_instance.InstanceId)));
    }

    [Fact]
    public async Task WatchStart_LoaderExitsWithZeroWhileAProcessThatIsNoGameHoldsItsStreams_ReportsTheEarlyExit()
    {
        PlaceStarMap();
        using var launcher = LauncherSeeingGame(() => false, TimeSpan.FromMilliseconds(100));
        var started = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));
        var process = Assert.Single(_starter.Processes);
        process.HasExited = true;
        process.ExitCode = 0;
        process.HasEnded = false;

        var result = await launcher.WatchStartAsync(_instance, started);

        Assert.Equal(LaunchOutcome.ExitedEarly, result.Outcome);
        Assert.Contains("stopped without starting the game", result.Message);
        Assert.DoesNotContain("restarted itself", File.ReadAllText(_paths.GetInstanceLaunchLogPath(_instance.InstanceId)));
    }

    [Fact]
    public async Task WatchStart_LoaderStopsWithAnErrorWhileAProcessItStartedRuns_ReportsTheError()
    {
        PlaceStarMap();
        using var launcher = LauncherSeeingGame(() => true, TimeSpan.FromMilliseconds(100));
        var started = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));
        var process = Assert.Single(_starter.Processes);
        process.HasExited = true;
        process.ExitCode = 3;
        process.HasEnded = false;

        var result = await launcher.WatchStartAsync(_instance, started);

        Assert.Equal(LaunchOutcome.ExitedEarly, result.Outcome);
        Assert.Equal(3, result.ExitCode);
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
    public async Task WatchStart_WrapperLauncherExitsWithAnErrorBeforeTheGameComesUp_StaysStarted()
    {
        PlaceStarMap();
        using var launcher = WrapperLauncher(PlaceWrapper(), TimeSpan.FromMinutes(5));
        var started = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));
        var process = Assert.Single(_starter.Processes);
        process.Output.AddRange(KsArmoryCrash);
        process.HasExited = true;
        process.ExitCode = 1;
        var gameLog = _paths.GetInstanceGameLogPath(_instance.InstanceId);
        var gameComesUp = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            Directory.CreateDirectory(Path.GetDirectoryName(gameLog)!);
            await File.WriteAllTextAsync(gameLog, "INFO loaded settings");
        });

        var result = await launcher.WatchStartAsync(_instance, started);
        await gameComesUp;

        Assert.True(result.Started);
        Assert.Null(result.BlamedModId);
    }

    [Fact]
    public async Task WatchStart_WrapperLauncherEndsWithoutTheGame_NamesTheWrapperAndBlamesNoMod()
    {
        PlaceStarMap();
        var instance = InstanceWith("KSArmory");
        var wine = PlaceWrapper();
        using var launcher = WrapperLauncher(wine, TimeSpan.FromMilliseconds(100));
        var started = launcher.Launch(instance, LoaderListing(provides: StarMapProvides()));
        var process = Assert.Single(_starter.Processes);
        process.Output.AddRange(KsArmoryCrash);
        process.HasExited = true;
        process.ExitCode = 0;

        var result = await launcher.WatchStartAsync(instance, started);

        Assert.Equal(LaunchOutcome.ExitedEarly, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.BlamedModId);
        Assert.Contains($"Wine wrapper '{Path.Combine(Path.GetFullPath(_tempRoot), "Kitten Space Agency.app")}'", result.Message);
        Assert.NotNull(result.Wine?.Wrapper);
        Assert.Equal(KsArmoryCrash, result.Output);
    }

    [Fact]
    public async Task WatchStart_ResultThatDidNotStart_IsReturnedAsItIs()
    {
        var failed = _launcher.Launch(_instance, loader: null);

        Assert.Same(failed, await _launcher.WatchStartAsync(_instance, failed));
    }

    private const int UnhandledException = -532462766;

    /// <summary>The end of a tail that Brutal.Monitor kept of a run that crashed, with an exception the game caught before it.</summary>
    private static string[] CrashTail(params string[] innerFrames) =>
    [
        "[Brutal.Monitor] the most recent 37 log lines - full history in KittenSpaceAgency.260926-122536.1000.log",
        "System.InvalidOperationException: There is an error in XML document (4364, 39).",
        " ---> System.FormatException: The input string '0,389' was not in a correct format.",
        "   at KSArmory.Settings.Read()",
        "loaded system 'Test'",
        "Unhandled exception System.Reflection.TargetInvocationException: Exception has been thrown by the target of an invocation.",
        " ---> System.NullReferenceException: Object reference not set to an instance of an object.",
        .. innerFrames,
        "   at KSA.ConfigOnStartPopup.SetVehicles()",
        "   at KSA.Program.Main(String[] inArgs)",
        "   --- End of inner exception stack trace ---",
        "   at StarMap.GameSurveyer.RunGame()",
        "   at StarMap.Program.Main(String[] args).",
        "Last chance dump: Failed Write dump failed - HRESULT: 0x80004005..",
    ];

    private string LogsFolder(Instance instance) => Path.GetDirectoryName(_paths.GetInstanceGameLogPath(instance.InstanceId))!;

    /// <summary>Writes a crash log the way Brutal.Monitor names it for the run of the process.</summary>
    private string WriteCrashLog(Instance instance, int processId, string kind = "abnormal-exit", string[]? lines = null)
    {
        var name = kind == "abnormal-exit"
            ? $"KittenSpaceAgency.260926-122536.{processId}.20260926_122557.abnormal-exit.log"
            : $"KittenSpaceAgency.260926-122536.{processId}.{kind}.log";
        var path = Path.Combine(LogsFolder(instance), name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines ?? CrashTail());
        return path;
    }

    /// <summary>A launch whose game wrote its log, so its start was watched as good, and whose process still runs.</summary>
    private async Task<(LaunchResult Started, FakeStartedProcess Process)> StartGameAsync(LoaderLauncher launcher, Instance instance, bool isAdopted = false)
    {
        var launched = isAdopted
            ? launcher.Launch(instance, LoaderListing(provides: StarMapProvides()), StarMapDirectory, arguments: null, isAdopted: true)
            : launcher.Launch(instance, LoaderListing(provides: StarMapProvides()));
        var process = _starter.Processes[^1];
        var gameLog = _paths.GetInstanceGameLogPath(instance.InstanceId);
        Directory.CreateDirectory(Path.GetDirectoryName(gameLog)!);
        File.WriteAllText(gameLog, "INFO loaded settings");
        var started = await launcher.WatchStartAsync(instance, launched);
        Assert.True(started.Started);
        return (started, process);
    }

    private LoaderLauncher LauncherWithCrashLogWait(TimeSpan wait) =>
        new(_paths, _starter, OsPlatform.Windows, NoDotnetNeeded, LoaderLauncher.DefaultStartupWindow, crashLogWait: wait);

    [Fact]
    public async Task WatchExit_GameCrashesAfterTheStart_ReportsItsCrashLogAndException()
    {
        PlaceStarMap();
        var (started, process) = await StartGameAsync(_launcher, _instance);

        var watch = _launcher.WatchExitAsync(_instance, started);
        Assert.False(watch.IsCompleted);
        var log = WriteCrashLog(_instance, process.Id);
        process.Exit(UnhandledException);
        var exit = await watch.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(GameExitKind.Crashed, exit.Kind);
        Assert.Equal(UnhandledException, exit.ExitCode);
        var crash = Assert.IsType<GameCrash>(exit.Crash);
        Assert.Equal(log, crash.LogPath);
        Assert.Equal($"KittenSpaceAgency.260926-122536.{process.Id}", crash.Run);
        Assert.Equal("System.NullReferenceException: Object reference not set to an instance of an object.", crash.Exception);
        Assert.Null(crash.BlamedModId);
    }

    [Fact]
    public async Task WatchExit_CrashLogWrittenAfterTheProcessEnded_IsWaitedFor()
    {
        PlaceStarMap();
        using var launcher = LauncherWithCrashLogWait(TimeSpan.FromSeconds(30));
        var (started, process) = await StartGameAsync(launcher, _instance);
        process.Exit(UnhandledException);

        var watch = launcher.WatchExitAsync(_instance, started);
        await Task.Delay(300);
        Assert.False(watch.IsCompleted);
        var log = WriteCrashLog(_instance, process.Id, "previous-crash");

        Assert.Equal(log, (await watch.WaitAsync(TimeSpan.FromSeconds(30))).Crash?.LogPath);
    }

    [Fact]
    public async Task WatchExit_StackNamesAModAssembly_BlamesThatMod()
    {
        PlaceStarMap();
        var instance = InstanceWith("ModMenu", "kessler-armory");
        PlaceMod(instance, "ModMenu", "name = \"ModMenu\"", "ModMenu");
        PlaceMod(instance, "kessler-armory", "name = \"KSArmory\"", "KSArmory");
        var (started, process) = await StartGameAsync(_launcher, instance);
        WriteCrashLog(instance, process.Id, lines: CrashTail("   at KSArmory.RoundFollowable.DrawAxes()"));
        process.Exit(UnhandledException);

        var exit = await _launcher.WatchExitAsync(instance, started);

        Assert.Equal("kessler-armory", exit.Crash?.BlamedModId);
    }

    [Fact]
    public async Task WatchExit_OnlyTheGameAndTheRuntimeInTheStack_BlamesNoMod()
    {
        PlaceStarMap();
        var instance = InstanceWith("KSArmory");
        PlaceMod(instance, "KSArmory", "name = \"KSArmory\"", "KSArmory", "KSA", "System");
        var (started, process) = await StartGameAsync(_launcher, instance);
        WriteCrashLog(instance, process.Id);
        process.Exit(UnhandledException);

        var exit = await _launcher.WatchExitAsync(instance, started);

        Assert.Equal(GameExitKind.Crashed, exit.Kind);
        Assert.Null(exit.Crash?.BlamedModId);
    }

    [Fact]
    public async Task WatchExit_QuitOrRestartWithZero_IsClosedEvenWithACrashLog()
    {
        PlaceStarMap();
        var (started, process) = await StartGameAsync(_launcher, _instance);
        process.Output.Add("StarMap - RESTARTING");
        WriteCrashLog(_instance, process.Id);
        process.Exit(0);

        var exit = await _launcher.WatchExitAsync(_instance, started);

        Assert.Equal(GameExitKind.Closed, exit.Kind);
        Assert.Equal(0, exit.ExitCode);
        Assert.Null(exit.Crash);
        Assert.Equal(["StarMap - RESTARTING"], exit.Output);
    }

    [Fact]
    public async Task WatchExit_RunLogWithTheExceptionButNoCrashLog_IsClosed()
    {
        PlaceStarMap();
        using var launcher = LauncherWithCrashLogWait(TimeSpan.FromMilliseconds(100));
        var (started, process) = await StartGameAsync(launcher, _instance);
        File.WriteAllLines(Path.Combine(LogsFolder(_instance), $"KittenSpaceAgency.260926-122536.{process.Id}.log"), ["12:25:43.983 ERROR " + CrashTail()[5], .. CrashTail()[6..]]);
        process.Exit(UnhandledException);

        var exit = await launcher.WatchExitAsync(_instance, started);

        Assert.Equal(GameExitKind.Closed, exit.Kind);
        Assert.Equal(UnhandledException, exit.ExitCode);
        Assert.Null(exit.Crash);
    }

    [Fact]
    public async Task WatchExit_CrashLogOfAnotherProcess_IsNotThisCrash()
    {
        PlaceStarMap();
        using var launcher = LauncherWithCrashLogWait(TimeSpan.FromMilliseconds(100));
        var (started, process) = await StartGameAsync(launcher, _instance);
        WriteCrashLog(_instance, process.Id + 1);
        process.Exit(UnhandledException);

        Assert.Equal(GameExitKind.Closed, (await launcher.WatchExitAsync(_instance, started)).Kind);
    }

    [Fact]
    public async Task WatchExit_CrashLogFromBeforeTheLaunch_IsNotThisCrash()
    {
        PlaceStarMap();
        var old = WriteCrashLog(_instance, 1000);
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-3));
        using var launcher = LauncherWithCrashLogWait(TimeSpan.FromMilliseconds(100));
        var (started, process) = await StartGameAsync(launcher, _instance);
        Assert.Equal(1000, process.Id);
        process.Exit(UnhandledException);

        Assert.Equal(GameExitKind.Closed, (await launcher.WatchExitAsync(_instance, started)).Kind);
    }

    [Fact]
    public async Task WatchExit_LaunchForgottenBeforeTheWatch_StillReportsTheCrash()
    {
        PlaceStarMap();
        var (started, process) = await StartGameAsync(_launcher, _instance);
        WriteCrashLog(_instance, process.Id);
        process.Exit(UnhandledException);
        Assert.False(_launcher.IsRunning(_instance.InstanceId));
        Assert.True(process.Disposed);

        var exit = await _launcher.WatchExitAsync(_instance, started);

        Assert.Equal(GameExitKind.Crashed, exit.Kind);
    }

    [Fact]
    public async Task WatchExit_LaunchThatDidNotStartOrOfAnEarlierLaunch_IsUnknown()
    {
        PlaceStarMap();
        var (started, process) = await StartGameAsync(_launcher, _instance);
        process.Exit(0);
        await StartGameAsync(_launcher, _instance);

        Assert.Same(GameExit.Unknown, await _launcher.WatchExitAsync(_instance, _launcher.Launch(_instance, loader: null)));
        Assert.Same(GameExit.Unknown, await _launcher.WatchExitAsync(_instance, started));
    }

    private string GameLocation() => JsonNode.Parse(File.ReadAllText(ConfigFile))!["GameLocation"]!.GetValue<string>();

    [Theory]
    [InlineData("folder")]
    [InlineData("KSA.dll")]
    public async Task WatchExit_GameOnAKeptBuildEnds_WritesTheSettingsFolderBack(string form)
    {
        PlaceStarMap();
        var kept = KeepBuild();
        using var launcher = LauncherSeeingGame(() => false);
        var (started, process) = await StartGameAsync(launcher, _instance);
        Assert.Equal(kept, GameLocation());
        if (form == "KSA.dll")
            WriteConfig(new { GameLocation = Path.Combine(kept, "KSA.dll") });
        process.Exit(0);

        var exit = await launcher.WatchExitAsync(_instance, started);

        Assert.Equal(GameExitKind.Closed, exit.Kind);
        Assert.Equal(SettingsGame, GameLocation());
    }

    [Fact]
    public async Task WatchExit_GameOnAKeptBuildEndsInAnAdoptedFolder_GivesTheGamePathOfThePlayerBack()
    {
        PlaceStarMap();
        var player = PlaceGameAssembly(Path.Combine(_tempRoot, "Other Game"));
        WriteConfig(new { GameLocation = player });
        var kept = KeepBuild();
        using var launcher = LauncherSeeingGame(() => false);
        var (started, process) = await StartGameAsync(launcher, _instance, isAdopted: true);
        Assert.Equal(kept, GameLocation());
        process.Exit(0);

        await launcher.WatchExitAsync(_instance, started);

        Assert.Equal(player, GameLocation());
    }

    [Fact]
    public async Task WatchExit_TwoInstancesOnAKeptBuildEndInAnAdoptedFolder_GiveTheGamePathOfThePlayerBack()
    {
        PlaceStarMap();
        var player = PlaceGameAssembly(Path.Combine(_tempRoot, "Other Game"));
        WriteConfig(new { GameLocation = player });
        KeepBuild();
        var other = new Instance("Other", InstanceSource.Custom.Value);
        other.SetGameBuild(KeptVersion);
        var gameRuns = false;
        using var launcher = LauncherSeeingGame(() => gameRuns);
        var (first, firstProcess) = await StartGameAsync(launcher, _instance, isAdopted: true);
        gameRuns = true;
        var (second, secondProcess) = await StartGameAsync(launcher, other, isAdopted: true);
        firstProcess.Exit(0);
        await launcher.WatchExitAsync(_instance, first);
        gameRuns = false;
        secondProcess.Exit(0);

        await launcher.WatchExitAsync(other, second);

        Assert.Equal(player, GameLocation());
    }

    [Fact]
    public async Task WatchExit_FileNamesAnotherFolderWhenTheGameEnds_LeavesItAlone()
    {
        PlaceStarMap();
        KeepBuild();
        using var launcher = LauncherSeeingGame(() => false);
        var (started, process) = await StartGameAsync(launcher, _instance);
        // written by hand or by another start while the game ran
        WriteConfig(new { GameLocation = Path.Combine(_tempRoot, "Other Game") });
        var before = File.ReadAllBytes(ConfigFile);
        process.Exit(0);

        await launcher.WatchExitAsync(_instance, started);

        Assert.Equal(before, File.ReadAllBytes(ConfigFile));
    }

    [Fact]
    public async Task WatchExit_LoaderRestartedTheGameOnAKeptBuild_KeepsTheKeptFolder()
    {
        PlaceStarMap();
        var kept = KeepBuild();
        var gameRuns = false;
        using var launcher = LauncherSeeingGame(() => gameRuns);
        var (started, process) = await StartGameAsync(launcher, _instance);
        gameRuns = true;
        process.HasEnded = false;
        process.Exit(0);

        var exit = await launcher.WatchExitAsync(_instance, started);

        Assert.Equal(GameExitKind.Closed, exit.Kind);
        Assert.Equal(kept, GameLocation());
    }

    [Fact]
    public async Task WatchExit_InstanceWithoutAKeptBuild_WritesNothing()
    {
        PlaceStarMap();
        using var launcher = LauncherSeeingGame(() => false);
        var (started, process) = await StartGameAsync(launcher, _instance);
        // a write would indent the compact text
        WriteConfig(new { GameLocation = SettingsGame });
        var before = File.ReadAllBytes(ConfigFile);
        process.Exit(0);

        await launcher.WatchExitAsync(_instance, started);

        Assert.Equal(before, File.ReadAllBytes(ConfigFile));
    }

    [Fact]
    public async Task WatchExit_FileUnreadableWhenTheKeptBuildEnds_LogsItAndTheGameIsClosed()
    {
        PlaceStarMap();
        KeepBuild();
        var log = new RecordingLog();
        using var launcher = LauncherSeeingGame(() => false, log: log);
        var (started, process) = await StartGameAsync(launcher, _instance);
        File.WriteAllText(ConfigFile, "{ \"GameLocation\": ");
        process.Exit(0);

        var exit = await launcher.WatchExitAsync(_instance, started);

        Assert.Equal(GameExitKind.Closed, exit.Kind);
        Assert.Equal("{ \"GameLocation\": ", File.ReadAllText(ConfigFile));
        Assert.Contains(SettingsGame, Assert.Single(log.Messages));
    }

    [Fact]
    public async Task WatchExit_KeptBuildEnds_ChecksTheFileWhileItHoldsTheGateOfTheLaunches()
    {
        PlaceStarMap();
        KeepBuild();
        var launches = new RunningLaunches();
        var gameEnded = false;
        bool? gateHeld = null;
        using var launcher = new LoaderLauncher(
            _paths,
            _starter,
            SharedProfileLauncher.CurrentPlatform(),
            () => DotnetHost.Find(SharedProfileLauncher.CurrentPlatform()),
            LoaderLauncher.DefaultStartupWindow,
            launches,
            isGameRunning: () =>
            {
                if (gameEnded)
                    gateHeld = Monitor.IsEntered(launches.Gate);
                return false;
            });
        var (started, process) = await StartGameAsync(launcher, _instance);
        gameEnded = true;
        process.Exit(0);

        await launcher.WatchExitAsync(_instance, started);

        // a start in the same Borea takes the gate, so it cannot write the file between the check and the write
        Assert.True(gateHeld);
        Assert.Equal(SettingsGame, GameLocation());
    }

    [Fact]
    public async Task WatchStart_KeptBuildStopsEarly_WritesTheSettingsFolderBack()
    {
        PlaceStarMap();
        var kept = KeepBuild();
        using var launcher = LauncherSeeingGame(() => false);
        var started = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));
        Assert.Equal(kept, GameLocation());
        Assert.Single(_starter.Processes).Exit(1);

        var result = await launcher.WatchStartAsync(_instance, started);

        Assert.Equal(LaunchOutcome.ExitedEarly, result.Outcome);
        Assert.Equal(SettingsGame, GameLocation());
    }

    [Fact]
    public async Task WatchStart_WrapperOnAKeptBuildStopsEarly_WritesTheSettingsFolderBack()
    {
        PlaceStarMap();
        var wine = PlaceWrapper();
        var kept = KeepBuild();
        File.WriteAllBytes(Path.Combine(kept, "KSA.exe"), Array.Empty<byte>());
        using var launcher = new LoaderLauncher(_paths, _starter, OsPlatform.MacOs, NoDotnetNeeded, TimeSpan.FromMilliseconds(100), isGameRunning: () => false, wine: wine);
        var started = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));
        Assert.Equal(OnDriveZ(kept), GameLocation());
        Assert.Single(_starter.Processes).Exit(0);

        var result = await launcher.WatchStartAsync(_instance, started);

        Assert.Equal(LaunchOutcome.ExitedEarly, result.Outcome);
        Assert.NotNull(result.Wine);
        Assert.Equal(OnDriveZ(SettingsGame), GameLocation());
    }

    public void Dispose()
    {
        _launcher.Dispose();

        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
