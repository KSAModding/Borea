using System.Text.Json.Nodes;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Storage.Game;
using Borea.Storage.Launch;

namespace Borea.App.Tests.ViewModels;

public sealed class LaunchFailureTests
{
    private const int UnhandledException = -532462766;

    private static readonly string[] KsArmoryCrash =
    [
        "Unhandled exception. System.Reflection.ReflectionTypeLoadException: Unable to load one or more of the requested types.",
        "Method 'DrawAxes' in type 'KSArmory.RoundFollowable' from assembly 'KSArmory, Version=0.8.44.0, Culture=neutral, PublicKeyToken=null' does not have an implementation.",
    ];

    /// <summary>A harness with StarMap recorded and a loader process that has already crashed with <see cref="KsArmoryCrash"/>.</summary>
    internal static Task<ViewModelHarness> CreateAsync(int exitCode = UnhandledException) => CreateAsync(new CrashingStarter(exitCode, KsArmoryCrash));

    /// <summary>A harness with StarMap recorded that starts its processes through <paramref name="starter"/>.</summary>
    internal static Task<ViewModelHarness> CreateAsync(IProcessStarter starter) =>
        ViewModelHarness.CreateAsync(
            services => services.SettingsRepository.SaveAsync(services.Settings.WithLoaderInstallation("StarMap", CreateLoader(services, "StarMap"))),
            processStarter: starter);

    /// <summary>A loader directory with the launch target of the listing.</summary>
    private static LoaderInstallation CreateLoader(BoreaServices services, string loaderId)
    {
        var loader = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Loaders", loaderId)).FullName;
        File.WriteAllBytes(Path.Combine(loader, "StarMap.exe"), []);
        return new LoaderInstallation(loader, ModVersion.Parse("0.4.6"), rawVersion: null, isAdopted: false);
    }

    [Fact]
    public async Task Play_LoaderCrashesOnAMod_OpensTheModalThatNamesItAndOffersToDisableIt()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "KSArmory", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        await viewModel.PlayCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLaunchFailureOpen);
        Assert.Equal(harness.Localization.FormatLaunchStoppedTitle("StarMap"), viewModel.LaunchFailureTitle);
        Assert.Equal(harness.Localization.FormatLaunchModBroke("KSArmory", "0.8.44", "StarMap"), viewModel.LaunchMessage);
        Assert.True(viewModel.HasLaunchOutput);
        Assert.Contains("DrawAxes", viewModel.LaunchOutputText);
        Assert.True(viewModel.CanDisableBlamedMod);
        Assert.Equal(harness.Localization.FormatLaunchDisableMod("KSArmory"), viewModel.DisableBlamedModText);
        Assert.False(viewModel.IsLaunching);

        viewModel.ToggleLaunchOutputCommand.Execute(null);
        Assert.True(viewModel.IsLaunchOutputShown);

        await viewModel.DisableBlamedModCommand.ExecuteAsync(null);

        Assert.False(await harness.Services.ModState.IsActiveAsync(instance.InstanceId, "KSArmory"));
        Assert.False(viewModel.IsLaunchFailureOpen);
        Assert.Equal(harness.Localization.FormatLaunchModDisabled("KSArmory"), viewModel.LaunchMessage);
        Assert.False(viewModel.HasLaunchOutput);
        Assert.False(viewModel.CanDisableBlamedMod);
        Assert.False(viewModel.ContentGroups.SelectMany(group => group.Items).Single().IsEnabled);
    }

    [Fact]
    public async Task LaunchModal_Closed_KeepsTheStatusLineThatOpensItAgain()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.PlayCommand.ExecuteAsync(null);

        viewModel.CloseLaunchFailureCommand.Execute(null);

        Assert.False(viewModel.IsLaunchFailureOpen);
        Assert.Equal(harness.Localization.FormatLaunchModBroke("KSArmory", "0.8.44", "StarMap"), viewModel.LaunchMessage);
        Assert.True(viewModel.CanDisableBlamedMod);

        viewModel.ShowLaunchFailureCommand.Execute(null);

        Assert.True(viewModel.IsLaunchFailureOpen);
    }

    [Fact]
    public async Task LaunchModal_OpenLaunchLogFails_ShowsTheErrorInTheModal()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.PlayCommand.ExecuteAsync(null);
        viewModel.OpenWithSystem = _ => throw new InvalidOperationException("No application is associated with the file.");

        viewModel.OpenLaunchLogCommand.Execute(null);

        Assert.True(viewModel.IsLaunchFailureOpen);
        Assert.Equal("No application is associated with the file.", viewModel.LaunchFailureError);
        Assert.Empty(viewModel.Toasts.Items);
    }

    [Fact]
    public async Task Play_LoaderStopsWithoutNamingAMod_ShowsTheDetailsWithoutADisableButton()
    {
        using var harness = await CreateAsync(exitCode: 3);
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        await viewModel.PlayCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatLaunchExitedEarly("StarMap", 3), viewModel.LaunchMessage);
        Assert.True(viewModel.HasLaunchOutput);
        Assert.False(viewModel.CanDisableBlamedMod);
    }

    [Fact]
    public async Task Play_LoaderCannotLoadAGameAssembly_SaysTheLoaderNeedsAnUpdate()
    {
        string[] starMap5638Crash =
        [
            "Unhandled exception. System.Reflection.ReflectionTypeLoadException: Unable to load one or more of the requested types.",
            "Could not load file or assembly 'Brutal.Core.Numerics, Version=2026.9.0.0, Culture=neutral, PublicKeyToken=null'. The system cannot find the file specified.",
            "   at StarMap.GameSurveyer.TryLoadCoreAndGame()",
        ];
        using var harness = await ViewModelHarness.CreateAsync(
            services =>
            {
                var game = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Game")).FullName;
                File.Copy(Path.Combine(AppContext.BaseDirectory, "GameVersionFixture.dll"), Path.Combine(game, "KSA.dll"));
                return services.SettingsRepository.SaveAsync(services.Settings.WithGameDirectory(game).WithLoaderInstallation("StarMap", CreateLoader(services, "StarMap")));
            },
            processStarter: new CrashingStarter(UnhandledException, starMap5638Crash));
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();

        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLaunchFailureOpen);
        Assert.Equal(harness.Localization.FormatLaunchLoaderTooOld("StarMap", "2026.8.3.5117"), viewModel.LaunchMessage);
        Assert.Equal("https://github.com/StarMapLoader/StarMap/releases", viewModel.LaunchLoaderReleases?.Url);
        Assert.False(viewModel.CanDisableBlamedMod);
    }

    [Fact]
    public async Task PlayActiveInstance_OtherModsNeedMissingGameParts_NamesThemAndDisablesThemWithTheBlamedMod()
    {
        var started = LaunchResult.Success(WrapperPlan, 4244, "Started.");
        var stopped = LaunchResult.ExitedEarly(WrapperPlan, UnhandledException, ["File name: 'Brutal.Core.Numerics, Version=2026.9.0.0'"], "KSArmory", "English text.", LoaderCrashCause.ModAssembly, otherBlamedModIds: ["MeasureTools"]);
        using var harness = await CreateWithLauncherAsync(new FixedLauncher(started, stopped));
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "KSArmory", activate: true, ownership: ModInstallOwnership.Borea);
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, ownership: ModInstallOwnership.Borea, into: instance);
        await viewModel.LoadAsync();

        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        var localization = harness.Localization;
        Assert.Equal(localization.FormatLaunchModBroke("KSArmory", "0.8.44", "StarMap") + " " + localization.FormatLaunchOtherModsNeedMissing("MeasureTools"), viewModel.LaunchMessage);
        Assert.Equal(localization.FormatLaunchDisableMods(2), viewModel.DisableBlamedModText);

        await viewModel.DisableBlamedModCommand.ExecuteAsync(null);

        Assert.False(await harness.Services.ModState.IsActiveAsync(instance.InstanceId, "KSArmory"));
        Assert.False(await harness.Services.ModState.IsActiveAsync(instance.InstanceId, "MeasureTools"));
        Assert.Equal(localization.FormatLaunchModsDisabled(2), viewModel.LaunchMessage);
        Assert.Null(viewModel.DisableBlamedModText);
    }

    private const int AccessViolation = -1073741819;

    private static readonly string[] ModLoadingCrash =
    [
        "StarMap - Using Instance Path: Main",
        "Fatal error.",
        "Internal CLR error. (0x80131506)",
        "   at System.Reflection.RuntimeModule.GetTypes()",
        "   at StarMap.Core.ModRepository.RuntimeMod.InitializeMod(StarMap.Core.ModRepository.ModRegistry)",
        "   at StarMap.Core.ModRepository.ModLoader.PrepareMods()",
    ];

    [Fact]
    public async Task Play_LoaderStopsWhileLoadingAMod_NamesItAsTheLikelyCauseAndOffersToDisableIt()
    {
        using var harness = await CreateAsync(new CrashingStarter(AccessViolation, ModLoadingCrash));
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "KSArmory", activate: true, ownership: ModInstallOwnership.Borea);
        File.WriteAllBytes(Path.Combine(harness.Services.Paths.GetInstanceModsFolder(instance.InstanceId), "KSArmory", "KSArmory.dll"), []);
        await viewModel.LoadAsync();

        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLaunchFailureOpen);
        Assert.Equal(harness.Localization.FormatLaunchModLikelyBroke("KSArmory", "0.8.44", "StarMap"), viewModel.LaunchMessage);
        Assert.True(viewModel.CanDisableBlamedMod);
        Assert.Equal(harness.Localization.FormatLaunchDisableMod("KSArmory"), viewModel.DisableBlamedModText);
        Assert.StartsWith(harness.Localization.FormatLaunchExitCode(LoaderExitCode.Describe(AccessViolation, OperatingSystem.IsWindows())), viewModel.LaunchOutputText);
        Assert.Contains("RuntimeMod.InitializeMod", viewModel.LaunchOutputText);
    }

    [Fact]
    public async Task Play_LoaderStopsWhileLoadingModsWithoutAModFound_SaysItStoppedWhileLoadingMods()
    {
        using var harness = await CreateAsync(new CrashingStarter(AccessViolation, ModLoadingCrash));
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();

        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLaunchFailureOpen);
        Assert.Equal(harness.Localization.FormatLaunchStoppedLoadingMods("StarMap", AccessViolation), viewModel.LaunchMessage);
        Assert.False(viewModel.CanDisableBlamedMod);
    }

    [Fact]
    public async Task Play_TwoLoadersRecorded_StartsTheOneTheModsNeed()
    {
        using var harness = await ViewModelHarness.CreateAsync(
            services => services.SettingsRepository.SaveAsync(services.Settings
                .WithLoaderInstallation("AlphaLoader", CreateLoader(services, "AlphaLoader"))
                .WithLoaderInstallation("StarMap", CreateLoader(services, "StarMap"))),
            editSnapshot: json =>
            {
                var root = JsonNode.Parse(json)!;
                var listings = root["listings"]!.AsArray();
                var copy = listings.Single(node => (string?)node!["id"] == "StarMap")!.DeepClone();
                copy["id"] = "AlphaLoader";
                copy["authored"]!["id"] = "AlphaLoader";
                copy["authored"]!["name"] = "Alpha Loader";
                foreach (var release in copy["releases"]!.AsArray())
                {
                    release!["id"] = "AlphaLoader";
                    release["listing"]!["name"] = "Alpha Loader";
                }

                listings.Add(copy);
                return root.ToJsonString();
            },
            processStarter: new CrashingStarter(UnhandledException, KsArmoryCrash));
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        await viewModel.PlayCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatLaunchModBroke("KSArmory", "0.8.44", "StarMap"), viewModel.LaunchMessage);
    }

    [Fact]
    public async Task PlayActiveInstance_WithoutAnOpenedPage_StartsTheActiveInstance()
    {
        var starter = new RunningStarter();
        using var harness = await CreateAsync(starter);
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        starter.GameLog = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        await viewModel.LoadAsync();

        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        Assert.True(harness.Services.Launcher.IsRunning(instance.InstanceId));
        Assert.True(File.Exists(harness.Services.Paths.GetInstanceLaunchLogPath(instance.InstanceId)));
        Assert.NotNull(viewModel.LaunchMessage);
        Assert.False(viewModel.HasLaunchOutput);
        Assert.False(viewModel.IsLaunching);
        Assert.Null(viewModel.SelectedInstance);
        Assert.True(viewModel.CurrentWindowHome);
    }

    [Fact]
    public async Task PlayActiveInstance_PassesTheSavedLaunchArgumentsAfterTheHandover()
    {
        var starter = new RunningStarter();
        using var harness = await CreateAsync(starter);
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await harness.Services.Instances.UpdateAsync(instance.InstanceId, saved =>
        {
            saved.SetLaunchArguments(["-windowed", "a b"]);
            return true;
        });
        starter.GameLog = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        await viewModel.LoadAsync();

        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        var root = Path.GetFullPath(harness.Services.Paths.GetInstanceRoot(instance.InstanceId));
        Assert.Equal(["-InstancePath", root, "-windowed", "a b"], Assert.Single(starter.Plans).Arguments.TakeLast(4));
    }

    /// <summary>An active instance without mods, whose start <paramref name="starter"/> answers like a game that started.</summary>
    private static async Task<Instance> CreateActiveInstanceAsync(ViewModelHarness harness, RunningStarter starter)
    {
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        starter.GameLog = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        await harness.ViewModel.LoadAsync();
        await harness.WhenIdleAsync();
        return instance;
    }

    [Fact]
    public async Task PlayActiveInstance_NoCachedIndexAndSpaceDockNeverAnswers_StartsFromTheFetchedIndex()
    {
        var starter = new RunningStarter();
        using var harness = await ViewModelHarness.CreateAsync(
            services => services.SettingsRepository.SaveAsync(services.Settings.WithLoaderInstallation("StarMap", CreateLoader(services, "StarMap"))),
            indexOffline: true,
            processStarter: starter);
        var instance = await CreateActiveInstanceAsync(harness, starter);
        harness.IndexOffline = false;
        harness.SpaceDock.Browse = () => new TaskCompletionSource().Task;

        await harness.ViewModel.PlayActiveInstanceCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(harness.Services.Launcher.IsRunning(instance.InstanceId));
        Assert.Equal(0, harness.SpaceDock.Browses);
    }

    [Fact]
    public async Task PlayActiveInstance_CachedIndexListsTheLoader_StartsWithoutARequest()
    {
        var starter = new RunningStarter();
        using var harness = await CreateAsync(starter);
        var instance = await CreateActiveInstanceAsync(harness, starter);
        var requests = harness.Requests.Count;

        await harness.ViewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        Assert.True(harness.Services.Launcher.IsRunning(instance.InstanceId));
        Assert.Equal(0, harness.SpaceDock.Browses);
        Assert.Equal(requests, harness.Requests.Count);
    }

    [Fact]
    public async Task PlayActiveInstance_SourceNeverAnswers_StartsFromTheCachedIndex()
    {
        var starter = new RunningStarter();
        using var harness = await CreateAsync(starter);
        var instance = await CreateActiveInstanceAsync(harness, starter);
        harness.SpaceDock.Browse = () => new TaskCompletionSource().Task;

        await harness.ViewModel.PlayActiveInstanceCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(harness.Services.Launcher.IsRunning(instance.InstanceId));
        Assert.False(harness.ViewModel.IsLaunching);
    }

    [Fact]
    public async Task PlayActiveInstance_IndexTimesOutWithoutACachedIndex_SaysTheListingsDidNotLoad()
    {
        var starter = new RunningStarter();
        using var harness = await ViewModelHarness.CreateAsync(
            services => services.SettingsRepository.SaveAsync(services.Settings.WithLoaderInstallation("StarMap", CreateLoader(services, "StarMap"))),
            indexOffline: true,
            processStarter: starter);
        await CreateActiveInstanceAsync(harness, starter);
        var timeout = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.", new TimeoutException());
        harness.IndexRequest = () => Task.FromException(timeout);

        await harness.ViewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatLaunchListingsFailed(timeout.Message), harness.ViewModel.LaunchMessage);
        Assert.Null(harness.ViewModel.UnexpectedError);
        Assert.False(harness.ViewModel.IsLaunching);
        Assert.True(harness.ViewModel.EnableHomeLaunch);
        Assert.Empty(starter.Plans);
    }

    /// <summary>A harness whose StarMap listing has <paramref name="entry"/> for every platform.</summary>
    private static Task<ViewModelHarness> CreateWithPlatformEntryAsync(IProcessStarter starter, JsonObject entry) =>
        ViewModelHarness.CreateAsync(
            services => services.SettingsRepository.SaveAsync(services.Settings.WithLoaderInstallation("StarMap", CreateLoader(services, "StarMap"))),
            editSnapshot: json =>
            {
                var root = JsonNode.Parse(json)!;
                var platforms = new JsonObject();
                foreach (var platform in new[] { "windows", "linux", "macos" })
                    platforms[platform] = entry.DeepClone();
                root["listings"]!.AsArray().Single(node => (string?)node!["id"] == "StarMap")!["authored"]!["provides"]!["platform"] = platforms;
                return root.ToJsonString();
            },
            processStarter: starter);

    private static async Task PlayActiveInstanceAsync(ViewModelHarness harness)
    {
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await harness.ViewModel.LoadAsync();

        await harness.ViewModel.PlayActiveInstanceCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task PlayActiveInstance_UnknownRuntimeForThisSystem_StartsNothingAndSaysWhy()
    {
        var starter = new RunningStarter();
        using var harness = await CreateWithPlatformEntryAsync(starter, new JsonObject { ["launch"] = "StarMap.exe", ["runtime"] = "mono" });

        await PlayActiveInstanceAsync(harness);

        Assert.Equal(harness.Localization.FormatLaunchUnknownRuntime("StarMap", "mono"), harness.ViewModel.LaunchMessage);
        Assert.Empty(starter.Plans);
    }

    [Fact]
    public async Task PlayActiveInstance_UnknownKeyForThisSystem_StartsNothingAndSaysWhy()
    {
        var starter = new RunningStarter();
        using var harness = await CreateWithPlatformEntryAsync(starter, new JsonObject { ["launch"] = "StarMap.exe", ["arch"] = "x64" });

        await PlayActiveInstanceAsync(harness);

        Assert.Equal(harness.Localization.FormatLaunchUnknownPlatformKey("StarMap", "arch"), harness.ViewModel.LaunchMessage);
        Assert.Empty(starter.Plans);
    }

    [Fact]
    public async Task PlayActiveInstance_EntryFileForThisSystemMissing_StartsNothingAndNamesTheFile()
    {
        var starter = new RunningStarter();
        using var harness = await CreateWithPlatformEntryAsync(starter, new JsonObject { ["launch"] = "StarMap.dll", ["runtime"] = "dotnet" });

        await PlayActiveInstanceAsync(harness);

        var entryFile = Path.Combine(Path.GetDirectoryName(harness.Services.Paths.GetBoreaSettingsPath())!, "Loaders", "StarMap", "StarMap.dll");
        Assert.Equal(harness.Localization.FormatLaunchTargetMissing(entryFile, "StarMap"), harness.ViewModel.LaunchMessage);
        Assert.Empty(starter.Plans);
    }

    /// <summary>A game folder that holds only KSA.exe, inside a Wine prefix when asked.</summary>
    private static Task<ViewModelHarness> CreateWithWindowsBuildAsync(IProcessStarter starter, bool inPrefix) =>
        ViewModelHarness.CreateAsync(
            services =>
            {
                var root = Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!;
                if (inPrefix)
                {
                    Directory.CreateDirectory(Path.Combine(root, "dosdevices"));
                    File.WriteAllText(Path.Combine(root, "system.reg"), "WINE REGISTRY Version 2\n");
                }

                var game = Directory.CreateDirectory(Path.Combine(root, "Game")).FullName;
                File.WriteAllBytes(Path.Combine(game, "KSA.exe"), []);
                var settings = services.Settings.WithGameDirectory(game).WithLoaderInstallation("StarMap", CreateLoader(services, "StarMap"));
                return services.SettingsRepository.SaveAsync(settings);
            },
            processStarter: starter);

    [UnixFact("Windows runs the Windows build.")]
    public async Task PlayActiveInstance_WindowsBuildWithoutWine_StartsNothingAndSaysWhy()
    {
        var starter = new RunningStarter();
        using var harness = await CreateWithWindowsBuildAsync(starter, inPrefix: false);
        harness.Localization.TrySetCulture("de");

        await PlayActiveInstanceAsync(harness);

        Assert.Equal(harness.Localization.LaunchWindowsBuildWithoutWine, harness.ViewModel.LaunchMessage);
        Assert.Empty(starter.Plans);
    }

    [UnixFact("Windows runs the Windows build.")]
    public async Task PlayWithoutModLoader_WindowsBuildInAWinePrefix_StartsNothingAndNamesThePrefix()
    {
        var starter = new RunningStarter();
        using var harness = await CreateWithWindowsBuildAsync(starter, inPrefix: true);
        harness.Localization.TrySetCulture("de");
        await harness.ViewModel.LoadAsync();

        await harness.ViewModel.PlayWithoutModLoaderCommand.ExecuteAsync(null);

        var prefix = new WinePrefixProbe().Find(harness.Services.Settings.GameDirectoryPath!)!.PrefixRoot;
        Assert.Equal(harness.Localization.FormatLaunchWindowsBuildInPrefix(prefix), harness.ViewModel.LaunchMessage);
        Assert.Empty(starter.Plans);
    }

    private const string WrapperBundle = "/Applications/Kitten Space Agency.app";

    private static readonly WineInstall WrapperInstall = new(
        WrapperBundle + "/Contents/SharedSupport/prefix",
        new WineDriveMap(new Dictionary<char, string>(), StringComparison.OrdinalIgnoreCase),
        new WineWrapper(WrapperBundle, WrapperBundle + "/Contents/MacOS/launcher"));

    private static LaunchPlan WrapperPlan => new(Path.Combine(Path.GetTempPath(), "launcher"), [], Path.GetTempPath(), new Dictionary<string, string>());

    /// <summary>A harness with StarMap recorded whose loader launches all end in <paramref name="launcher"/>.</summary>
    private static Task<ViewModelHarness> CreateWithLauncherAsync(ILauncher launcher) =>
        ViewModelHarness.CreateAsync(
            services => services.SettingsRepository.SaveAsync(services.Settings.WithLoaderInstallation("StarMap", CreateLoader(services, "StarMap"))),
            loaderLauncher: launcher);

    [Theory]
    [InlineData(LaunchOutcome.WrapperBusy)]
    [InlineData(LaunchOutcome.WrapperNeedsVariable)]
    [InlineData(LaunchOutcome.WrapperArguments)]
    [InlineData(LaunchOutcome.WrapperRuntime)]
    [InlineData(LaunchOutcome.PathOutsidePrefix)]
    public async Task PlayActiveInstance_WrapperRefusesTheLaunch_SaysWhyInTheDisplayLanguage(LaunchOutcome outcome)
    {
        var unmapped = Path.Combine(Path.GetTempPath(), "Instances", "Main");
        var launcher = new FixedLauncher(LaunchResult.Failed(outcome, "English text.", wine: WrapperInstall, unmappedPath: unmapped));
        using var harness = await CreateWithLauncherAsync(launcher);
        harness.Localization.TrySetCulture("de");

        await PlayActiveInstanceAsync(harness);

        var localization = harness.Localization;
        var expected = outcome switch
        {
            LaunchOutcome.WrapperBusy => localization.FormatLaunchWrapperBusy(WrapperBundle),
            LaunchOutcome.WrapperNeedsVariable => localization.FormatLaunchWrapperNeedsVariable("StarMap", "-InstancePath", WrapperBundle),
            LaunchOutcome.WrapperArguments => localization.FormatLaunchWrapperArguments(WrapperBundle, "StarMap"),
            LaunchOutcome.WrapperRuntime => localization.FormatLaunchWrapperRuntime("StarMap", WrapperBundle),
            _ => localization.FormatLaunchPathOutsidePrefix(unmapped, WrapperInstall.PrefixRoot),
        };
        Assert.Equal(expected, harness.ViewModel.LaunchMessage);
        Assert.NotEqual("English text.", harness.ViewModel.LaunchMessage);
    }

    [Fact]
    public async Task PlayActiveInstance_WrapperStopsBeforeTheGame_OpensTheModalThatNamesTheWrapper()
    {
        var started = LaunchResult.Success(WrapperPlan, 4244, "Started.");
        var stopped = LaunchResult.ExitedEarly(WrapperPlan, 0, ["Sikarugir: launching"], blamedModId: null, "English text.", wine: WrapperInstall);
        using var harness = await CreateWithLauncherAsync(new FixedLauncher(started, stopped));

        await PlayActiveInstanceAsync(harness);

        var viewModel = harness.ViewModel;
        Assert.True(viewModel.IsLaunchFailureOpen);
        Assert.Equal(harness.Localization.FormatLaunchWrapperStopped(WrapperBundle, "StarMap"), viewModel.LaunchMessage);
        Assert.Contains("Sikarugir: launching", viewModel.LaunchOutputText);
        Assert.False(viewModel.CanDisableBlamedMod);
    }

    [Theory]
    [InlineData(SharedProfileLaunchOutcome.WrapperBusy)]
    [InlineData(SharedProfileLaunchOutcome.PathOutsidePrefix)]
    public async Task PlayWithoutModLoader_WrapperRefusesTheLaunch_SaysWhyInTheDisplayLanguage(SharedProfileLaunchOutcome outcome)
    {
        var executable = Path.Combine(Path.GetTempPath(), "KSA.exe");
        var launcher = new FixedSharedProfileLauncher(SharedProfileLaunchResult.Failed(outcome, "English text.", wine: WrapperInstall, unmappedPath: outcome == SharedProfileLaunchOutcome.PathOutsidePrefix ? executable : null));
        using var harness = await ViewModelHarness.CreateAsync(sharedProfileLauncher: launcher);
        harness.Localization.TrySetCulture("de");

        await harness.ViewModel.PlayWithoutModLoaderCommand.ExecuteAsync(null);

        var expected = outcome switch
        {
            SharedProfileLaunchOutcome.WrapperBusy => harness.Localization.FormatLaunchWrapperBusy(WrapperBundle),
            _ => harness.Localization.FormatLaunchPathOutsidePrefix(executable, WrapperInstall.PrefixRoot),
        };
        Assert.Equal(expected, harness.ViewModel.LaunchMessage);
    }

    private static readonly DotnetRuntimeNeed Dotnet10 = DotnetRuntimeNeed.Parse("""{ "runtimeOptions": { "framework": { "name": "Microsoft.NETCore.App", "version": "10.0.0" } } }""")!;

    private static LaunchResult RuntimeMissing => LaunchResult.Failed(LaunchOutcome.WrapperRuntimeMissing, "English text.", WrapperPlan, wine: WrapperInstall, missingRuntime: Dotnet10);

    [Fact]
    public async Task PlayActiveInstance_WrapperLacksTheRuntime_OpensThePromptInTheDisplayLanguage()
    {
        var launcher = new SequenceLauncher(RuntimeMissing);
        using var harness = await CreateWithLauncherAsync(launcher);
        harness.Localization.TrySetCulture("de");

        await PlayActiveInstanceAsync(harness);

        var viewModel = harness.ViewModel;
        var localization = harness.Localization;
        Assert.True(viewModel.IsRuntimePromptOpen);
        Assert.Equal(localization.FormatLaunchRuntimeTitle("10.0"), viewModel.RuntimePromptTitle);
        Assert.Equal(localization.FormatLaunchRuntimeMissing("StarMap", "10.0", WrapperBundle), viewModel.RuntimePromptText);
        Assert.Contains(WrapperBundle, viewModel.RuntimePromptText);
        Assert.Equal(localization.FormatLaunchRuntimeDownload("10.0"), viewModel.RuntimeDownloadText);
        Assert.Contains("Install Software", localization.LaunchRuntimeInstallStep);
        Assert.Null(viewModel.RuntimePromptError);
        Assert.Null(viewModel.LaunchMessage);
        Assert.False(viewModel.IsLaunchFailureOpen);
    }

    [Fact]
    public async Task CheckRuntimeAgain_RuntimeNowThere_StartsTheLaunchAndClosesThePrompt()
    {
        var launcher = new SequenceLauncher(RuntimeMissing, LaunchResult.Success(WrapperPlan, 4244, "Started StarMap."));
        using var harness = await CreateWithLauncherAsync(launcher);
        await PlayActiveInstanceAsync(harness);
        var viewModel = harness.ViewModel;
        Assert.True(viewModel.IsRuntimePromptOpen);
        bool? openWhileWatched = null;
        launcher.OnWatch = () => openWhileWatched = viewModel.IsRuntimePromptOpen;

        await viewModel.CheckRuntimeAgainCommand.ExecuteAsync(null);

        Assert.Equal(2, launcher.Launches);
        Assert.Equal(1, launcher.Watches);
        Assert.False(openWhileWatched);
        Assert.False(viewModel.IsRuntimePromptOpen);
        Assert.Equal("Started StarMap.", viewModel.LaunchMessage);
    }

    [Fact]
    public async Task CheckRuntimeAgain_RuntimeStillMissing_KeepsThePromptAndSaysSo()
    {
        var launcher = new SequenceLauncher(RuntimeMissing, RuntimeMissing);
        using var harness = await CreateWithLauncherAsync(launcher);
        harness.Localization.TrySetCulture("de");
        await PlayActiveInstanceAsync(harness);
        var viewModel = harness.ViewModel;

        await viewModel.CheckRuntimeAgainCommand.ExecuteAsync(null);

        Assert.Equal(2, launcher.Launches);
        Assert.True(viewModel.IsRuntimePromptOpen);
        Assert.Equal(harness.Localization.FormatLaunchRuntimeStillMissing("10.0.0"), viewModel.RuntimePromptError);
        Assert.Equal(0, launcher.Watches);
    }

    [Fact]
    public async Task CheckRuntimeAgain_LaunchStopsForAnotherReason_ClosesThePromptAndSaysWhy()
    {
        var launcher = new SequenceLauncher(RuntimeMissing, LaunchResult.Failed(LaunchOutcome.WrapperBusy, "English text.", wine: WrapperInstall));
        using var harness = await CreateWithLauncherAsync(launcher);
        await PlayActiveInstanceAsync(harness);
        var viewModel = harness.ViewModel;

        await viewModel.CheckRuntimeAgainCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsRuntimePromptOpen);
        Assert.Equal(harness.Localization.FormatLaunchWrapperBusy(WrapperBundle), viewModel.LaunchMessage);
    }

    [Fact]
    public async Task CheckRuntimeAgain_InstanceGoneBeforeTheLaunch_ClosesThePromptAndSaysWhy()
    {
        var launcher = new SequenceLauncher(RuntimeMissing);
        using var harness = await CreateWithLauncherAsync(launcher);
        await PlayActiveInstanceAsync(harness);
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.DeleteAsync(viewModel.ActiveInstance!.InstanceId);

        await viewModel.CheckRuntimeAgainCommand.ExecuteAsync(null);

        Assert.Equal(1, launcher.Launches);
        Assert.False(viewModel.IsRuntimePromptOpen);
        Assert.Equal(harness.Localization.LaunchInstanceMissing, viewModel.LaunchMessage);
    }

    [Fact]
    public async Task OpenRuntimeDownload_OpensTheDownloadPageOfTheMajorVersion()
    {
        using var harness = await CreateWithLauncherAsync(new SequenceLauncher(RuntimeMissing));
        await PlayActiveInstanceAsync(harness);
        var viewModel = harness.ViewModel;
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;

        viewModel.OpenRuntimeDownloadCommand.Execute(null);

        Assert.Equal(["https://dotnet.microsoft.com/download/dotnet/10.0"], opened);
        Assert.Null(viewModel.RuntimePromptError);

        viewModel.CloseRuntimePromptCommand.Execute(null);

        Assert.False(viewModel.IsRuntimePromptOpen);
    }

    /// <summary>A harness with a game directory and StarMap recorded, whose launches go to <paramref name="launcher"/> and whose refreshes to <paramref name="configurator"/>.</summary>
    private static Task<ViewModelHarness> CreateWithConfiguratorAsync(ILauncher launcher, RefreshRecorder configurator) =>
        ViewModelHarness.CreateAsync(
            services => services.SettingsRepository.SaveAsync(services.Settings
                .WithGameDirectory(Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Game")).FullName)
                .WithLoaderInstallation("StarMap", CreateLoader(services, "StarMap"))),
            loaderLauncher: launcher,
            loaderConfigurator: configurator);

    [Fact]
    public async Task PlayActiveInstance_RefreshesTheGamePathOfTheLoaderBeforeItStarts()
    {
        var launcher = new FixedLauncher(LaunchResult.Failed(LaunchOutcome.NoLaunchTarget, "Not started."));
        var configurator = new RefreshRecorder(launcher);
        using var harness = await CreateWithConfiguratorAsync(launcher, configurator);

        await PlayActiveInstanceAsync(harness);

        var refresh = Assert.Single(configurator.Refreshes);
        Assert.Equal("StarMap", refresh.LoaderId);
        Assert.Equal(harness.Services.Paths.GetLoaderDirectoryPath("StarMap"), refresh.LoaderDirectory);
        Assert.Equal(harness.Services.Paths.GetGameDirectoryPath(), refresh.GameDirectory);
        Assert.Equal(0, refresh.LaunchesBefore);
        Assert.Equal(1, launcher.Launches);
    }

    [Fact]
    public async Task PlayActiveInstance_GamePathCannotBeRefreshed_StartsNothingAndSaysWhy()
    {
        var launcher = new FixedLauncher(LaunchResult.Failed(LaunchOutcome.NoLaunchTarget, "Not started."));
        using var harness = await CreateWithConfiguratorAsync(launcher, new RefreshRecorder(launcher, new InvalidOperationException("No drive of the Wine prefix holds the game.")));

        await PlayActiveInstanceAsync(harness);

        Assert.Equal("No drive of the Wine prefix holds the game.", harness.ViewModel.LaunchMessage);
        Assert.Equal(0, launcher.Launches);
    }

    [Fact]
    public async Task PlayWithoutModLoader_PassesNoSavedLaunchArguments()
    {
        var starter = new RunningStarter();
        using var harness = await ViewModelHarness.CreateAsync(
            services =>
            {
                var game = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Game")).FullName;
                File.WriteAllBytes(Path.Combine(game, "KSA.exe"), []);
                File.WriteAllBytes(Path.Combine(game, "KSA"), []);
                return services.SettingsRepository.SaveAsync(services.Settings.WithGameDirectory(game));
            },
            processStarter: starter);
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await harness.Services.Instances.UpdateAsync(instance.InstanceId, saved =>
        {
            saved.SetLaunchArguments(["-windowed"]);
            return true;
        });
        await viewModel.LoadAsync();

        await viewModel.PlayWithoutModLoaderCommand.ExecuteAsync(null);

        // Borea knows no game executable on macOS, so nothing starts there
        Assert.Equal(OperatingSystem.IsMacOS() ? 0 : 1, starter.Plans.Count);
        Assert.All(starter.Plans, plan => Assert.Empty(plan.Arguments));
    }

    [Fact]
    public async Task PlayActiveInstance_AfterAnotherInstancePage_StartsTheActiveInstance()
    {
        var starter = new RunningStarter();
        using var harness = await CreateAsync(starter);
        var viewModel = harness.ViewModel;
        var active = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var other = (await harness.Services.Instances.CreateAsync("Other", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(active.InstanceId);
        starter.GameLog = harness.Services.Paths.GetInstanceGameLogPath(active.InstanceId);
        await viewModel.LoadAsync();
        await viewModel.Instances.Single(instance => instance.InstanceId == other.InstanceId).OpenCommand.ExecuteAsync(null);
        viewModel.SetMainWindowHome();

        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        Assert.True(harness.Services.Launcher.IsRunning(active.InstanceId));
        Assert.False(harness.Services.Launcher.IsRunning(other.InstanceId));
        Assert.True(viewModel.CurrentWindowHome);
    }

    [Fact]
    public async Task PlayActiveInstance_LoaderCrashesOnAMod_OffersTheWayOutOnHome()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        await viewModel.LoadAsync();

        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLaunchFailureOpen);
        Assert.Equal(harness.Localization.FormatLaunchModBroke("KSArmory", "0.8.44", "StarMap"), viewModel.LaunchMessage);
        Assert.True(viewModel.HasLaunchOutput);
        Assert.True(viewModel.CanDisableBlamedMod);
        Assert.Equal(harness.Localization.FormatLaunchDisableMod("KSArmory"), viewModel.DisableBlamedModText);

        await viewModel.DisableBlamedModCommand.ExecuteAsync(null);

        Assert.False(await harness.Services.ModState.IsActiveAsync(instance.InstanceId, "KSArmory"));
        Assert.Equal(harness.Localization.FormatLaunchModDisabled("KSArmory"), viewModel.LaunchMessage);
        Assert.False(viewModel.HasLaunchOutput);
        Assert.False(viewModel.IsLaunchFailureOpen);
        Assert.True(viewModel.CurrentWindowHome);
    }

    [Fact]
    public async Task PlayOnTheActiveLibraryRow_LoaderCrashesOnAMod_OffersTheWayOutInTheLibrary()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        await viewModel.LoadAsync();
        viewModel.SetMainWindowLibraryCommand.Execute(null);

        await viewModel.ActiveInstance!.PlayCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatLaunchModBroke("KSArmory", "0.8.44", "StarMap"), viewModel.LaunchMessage);
        Assert.True(viewModel.HasLaunchOutput);
        Assert.True(viewModel.CanDisableBlamedMod);
        Assert.True(viewModel.CurrentWindowLibrary);
    }

    [Fact]
    public async Task PlayOnALibraryRow_StartsOnlyTheActiveInstance()
    {
        var starter = new RunningStarter();
        using var harness = await CreateAsync(starter);
        var viewModel = harness.ViewModel;
        var active = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var other = (await harness.Services.Instances.CreateAsync("Other", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(active.InstanceId);
        starter.GameLog = harness.Services.Paths.GetInstanceGameLogPath(active.InstanceId);
        await viewModel.LoadAsync();
        viewModel.SetMainWindowLibraryCommand.Execute(null);

        await viewModel.OtherInstances.Single().PlayCommand.ExecuteAsync(null);

        Assert.Null(viewModel.LaunchMessage);
        Assert.False(harness.Services.Launcher.IsRunning(other.InstanceId));

        await viewModel.ActiveInstance!.PlayCommand.ExecuteAsync(null);

        Assert.True(harness.Services.Launcher.IsRunning(active.InstanceId));
        Assert.False(harness.Services.Launcher.IsRunning(other.InstanceId));
        Assert.NotNull(viewModel.LaunchMessage);
        Assert.False(viewModel.IsLaunching);
    }

    [Fact]
    public async Task PlayActiveInstance_InstanceDeletedAfterLoad_SaysItIsGone()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await viewModel.LoadAsync();
        await harness.Services.Instances.DeleteAsync(instance.InstanceId);

        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.LaunchInstanceMissing, viewModel.LaunchMessage);
        Assert.False(viewModel.IsLaunching);
    }

    [Fact]
    public async Task PlayActiveInstance_WhileTheStartIsWatched_KeepsTheMessageOnAnotherPage()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var starter = new RunningStarter { Gate = gate };
        using var harness = await CreateAsync(starter);
        var viewModel = harness.ViewModel;
        var active = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var other = (await harness.Services.Instances.CreateAsync("Other", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(active.InstanceId);
        starter.GameLog = harness.Services.Paths.GetInstanceGameLogPath(active.InstanceId);
        await viewModel.LoadAsync();
        var launch = viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);
        await starter.Watched.Task.WaitAsync(TimeSpan.FromSeconds(30));

        await viewModel.Instances.Single(instance => instance.InstanceId == other.InstanceId).OpenCommand.ExecuteAsync(null);
        var watchedMessage = viewModel.LaunchMessage;
        gate.SetResult();
        await launch;

        Assert.Equal(harness.Localization.FormatLaunchStarting("StarMap"), watchedMessage);
        Assert.Equal(other.InstanceId, viewModel.SelectedInstance?.InstanceId);
    }

    [Fact]
    public async Task PlayWithoutModLoader_AfterACrash_ClearsTheWayOut()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        await viewModel.LoadAsync();
        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);
        Assert.True(viewModel.CanDisableBlamedMod);

        await viewModel.PlayWithoutModLoaderCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.LaunchMessage);
        Assert.False(viewModel.HasLaunchOutput);
        Assert.False(viewModel.CanDisableBlamedMod);
        Assert.Null(viewModel.DisableBlamedModText);
    }

    [Fact]
    public async Task OpenInstance_AfterACrashOnHome_KeepsTheWayOutOnlyOnTheCrashedInstance()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        var other = (await harness.Services.Instances.CreateAsync("Other", InstanceSource.Custom.Value)).Instance;
        await viewModel.LoadAsync();
        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowInstance);
        Assert.Equal(harness.Localization.FormatLaunchModBroke("KSArmory", "0.8.44", "StarMap"), viewModel.LaunchMessage);
        Assert.True(viewModel.CanDisableBlamedMod);

        await viewModel.Instances.Single(instance => instance.InstanceId == other.InstanceId).OpenCommand.ExecuteAsync(null);

        Assert.Null(viewModel.LaunchMessage);
        Assert.False(viewModel.HasLaunchOutput);
    }

    /// <summary>Hands out a loader process that keeps running, and writes <see cref="GameLog"/> once the launch is watched and <see cref="Gate"/> is open, like a game that started.</summary>
    private sealed class RunningStarter : IProcessStarter
    {
        public string? GameLog { get; set; }

        public TaskCompletionSource? Gate { get; init; }

        public TaskCompletionSource Watched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<LaunchPlan> Plans { get; } = [];

        public IStartedProcess Start(LaunchPlan plan)
        {
            Plans.Add(plan);
            return new RunningProcess(this);
        }

        private sealed class RunningProcess(RunningStarter owner) : IStartedProcess
        {
            public int Id => 4243;

            public bool HasExited => false;

            public int? ExitCode => null;

            public IReadOnlyList<string> RecentOutput => [];

            public async Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
            {
                owner.Watched.TrySetResult();
                if (owner.Gate is { } gate)
                    await gate.Task;

                if (owner.GameLog is { } log)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                    File.WriteAllText(log, "KSA started.");
                }

                return false;
            }

            public void Dispose()
            {
            }
        }
    }

    /// <summary>A loader launcher that answers every launch with <paramref name="launched"/> and its watch with <paramref name="watched"/>.</summary>
    private sealed class FixedLauncher(LaunchResult launched, LaunchResult? watched = null) : ILauncher
    {
        public int Launches { get; private set; }

        public LaunchResult Launch(Instance instance, ModMetadata? loader, IReadOnlyList<string>? arguments = null)
        {
            Launches++;
            return launched;
        }

        public Task<LaunchResult> WatchStartAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default) => Task.FromResult(watched ?? started);

        public Task<GameExit> WatchExitAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default) => Task.FromResult(GameExit.Unknown);

        public bool IsRunning(Guid instanceId) => false;
    }

    /// <summary>A loader launcher that answers the launches with <paramref name="results"/> in turn, and each watch with the started result.</summary>
    private sealed class SequenceLauncher(params LaunchResult[] results) : ILauncher
    {
        public int Launches { get; private set; }

        public int Watches { get; private set; }

        /// <summary>Runs when a watch starts.</summary>
        public Action? OnWatch { get; set; }

        public LaunchResult Launch(Instance instance, ModMetadata? loader, IReadOnlyList<string>? arguments = null) => results[Launches++];

        public Task<LaunchResult> WatchStartAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default)
        {
            Watches++;
            OnWatch?.Invoke();
            return Task.FromResult(started);
        }

        public Task<GameExit> WatchExitAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default) => Task.FromResult(GameExit.Unknown);

        public bool IsRunning(Guid instanceId) => false;
    }

    private sealed class FixedSharedProfileLauncher(SharedProfileLaunchResult result) : ISharedProfileLauncher
    {
        public SharedProfileLaunchResult Launch(IReadOnlyList<string>? arguments = null) => result;
    }

    /// <summary>Records each refresh with the launches before it, and throws <paramref name="failure"/> when set.</summary>
    private sealed class RefreshRecorder(FixedLauncher launcher, Exception? failure = null) : ILoaderConfigurator
    {
        public List<(string LoaderId, string LoaderDirectory, string GameDirectory, int LaunchesBefore)> Refreshes { get; } = [];

        public Task<string?> ConfigureAsync(ModMetadata loader, string loaderDirectory, string gameDirectory, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<string?> RefreshForWineAsync(ModMetadata loader, string loaderDirectory, string gameDirectory, CancellationToken cancellationToken = default)
        {
            Refreshes.Add((loader.ModId, loaderDirectory, gameDirectory, launcher.Launches));
            return failure is null ? Task.FromResult<string?>(null) : Task.FromException<string?>(failure);
        }

        public Task<string?> PrepareForLaunchAsync(ModMetadata loader, string loaderDirectory, string gameDirectory, bool isAdopted, CancellationToken cancellationToken = default) =>
            RefreshForWineAsync(loader, loaderDirectory, gameDirectory, cancellationToken);

        public string GamePathValue(string gameDirectory) => gameDirectory;
    }

    /// <summary>Hands out a loader process that has already exited with the given code and output.</summary>
    private sealed class CrashingStarter(int exitCode, IReadOnlyList<string> output) : IProcessStarter
    {
        public IStartedProcess Start(LaunchPlan plan) => new CrashedProcess(exitCode, output);

        private sealed class CrashedProcess(int exitCode, IReadOnlyList<string> output) : IStartedProcess
        {
            public int Id => 4242;

            public bool HasExited => true;

            public int? ExitCode => exitCode;

            public IReadOnlyList<string> RecentOutput => output;

            public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => Task.FromResult(true);

            public void Dispose()
            {
            }
        }
    }
}
