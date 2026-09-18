using System.Text.Json.Nodes;
using Borea.Composition;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
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
    private static Task<ViewModelHarness> CreateAsync(int exitCode = UnhandledException) => CreateAsync(new CrashingStarter(exitCode, KsArmoryCrash));

    /// <summary>A harness with StarMap recorded that starts its processes through <paramref name="starter"/>.</summary>
    private static Task<ViewModelHarness> CreateAsync(IProcessStarter starter) =>
        ViewModelHarness.CreateAsync(
            services => services.SettingsRepository.SaveAsync(services.Settings.WithLoaderInstallation("StarMap", CreateLoader(services, "StarMap"))),
            processStarter: starter);

    /// <summary>A loader directory with both launch targets, so the plan is found on Windows and through dotnet elsewhere.</summary>
    private static LoaderInstallation CreateLoader(BoreaServices services, string loaderId)
    {
        var loader = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Loaders", loaderId)).FullName;
        File.WriteAllBytes(Path.Combine(loader, "StarMap.exe"), []);
        File.WriteAllBytes(Path.Combine(loader, "StarMap.dll"), []);
        return new LoaderInstallation(loader, ModVersion.Parse("0.4.6"), rawVersion: null, isAdopted: false);
    }

    [Fact]
    public async Task Play_LoaderCrashesOnAMod_NamesItAndOffersToDisableIt()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "KSArmory", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        await viewModel.PlayCommand.ExecuteAsync(null);

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
        Assert.Equal(harness.Localization.FormatLaunchModDisabled("KSArmory"), viewModel.LaunchMessage);
        Assert.False(viewModel.HasLaunchOutput);
        Assert.False(viewModel.CanDisableBlamedMod);
        Assert.False(viewModel.ContentGroups.SelectMany(group => group.Items).Single().IsEnabled);
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

        Assert.Equal(harness.Localization.FormatLaunchModBroke("KSArmory", "0.8.44", "StarMap"), viewModel.LaunchMessage);
        Assert.True(viewModel.HasLaunchOutput);
        Assert.True(viewModel.CanDisableBlamedMod);
        Assert.Equal(harness.Localization.FormatLaunchDisableMod("KSArmory"), viewModel.DisableBlamedModText);

        await viewModel.DisableBlamedModCommand.ExecuteAsync(null);

        Assert.False(await harness.Services.ModState.IsActiveAsync(instance.InstanceId, "KSArmory"));
        Assert.Equal(harness.Localization.FormatLaunchModDisabled("KSArmory"), viewModel.LaunchMessage);
        Assert.False(viewModel.HasLaunchOutput);
        Assert.Null(viewModel.ContentError);
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
