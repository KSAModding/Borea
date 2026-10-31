using Borea.App.ViewModels;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Mods;
using Borea.Storage.Launch;

namespace Borea.App.Tests.ViewModels;

public sealed class GameCrashToastTests
{
    private const int UnhandledException = -532462766;

    private const string NullReference = "System.NullReferenceException: Object reference not set to an instance of an object.";

    private static string[] CrashTail(params string[] innerFrames) =>
    [
        "[Brutal.Monitor] the most recent 37 log lines - full history in KittenSpaceAgency.260926-122536.4244.log",
        "registered language 'English (United States)'",
        "Unhandled exception System.Reflection.TargetInvocationException: Exception has been thrown by the target of an invocation.",
        " ---> " + NullReference,
        .. innerFrames,
        "   at KSA.ConfigOnStartPopup.SetVehicles()",
        "   --- End of inner exception stack trace ---",
        "   at StarMap.GameSurveyer.RunGame()",
        "   at StarMap.Program.Main(String[] args).",
    ];

    /// <summary>Writes a crash log the way Brutal.Monitor names it for a run of the process.</summary>
    private static string WriteCrashLog(ViewModelHarness harness, Guid instanceId, string name, DateTime? writtenAt = null, string[]? lines = null)
    {
        var path = Path.Combine(Path.GetDirectoryName(harness.Services.Paths.GetInstanceGameLogPath(instanceId))!, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines ?? CrashTail());
        if (writtenAt is { } at)
            File.SetLastWriteTimeUtc(path, at);
        return path;
    }

    private static string AbnormalExit(int processId) => $"KittenSpaceAgency.260926-122536.{processId}.20260926_122557.abnormal-exit.log";

    /// <summary>Starts the active instance, whose game writes its log and keeps running until <see cref="GameStarter.Exit"/>.</summary>
    private static async Task<(ViewModelHarness Harness, GameStarter Starter, Instance Instance)> PlayAsync(Func<ViewModelHarness, Task<Instance>>? addInstance = null)
    {
        var starter = new GameStarter();
        var harness = await LaunchFailureTests.CreateAsync(starter);
        Instance instance;
        if (addInstance is null)
        {
            instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
            await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        }
        else
        {
            instance = await addInstance(harness);
        }

        starter.GameLog = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.PlayActiveInstanceCommand.ExecuteAsync(null);
        return (harness, starter, instance);
    }

    [Fact]
    public async Task Play_GameCrashesAfterTheStart_ShowsTheExceptionAndOpensItsCrashLog()
    {
        var (harness, starter, instance) = await PlayAsync();
        using var _ = harness;
        var viewModel = harness.ViewModel;
        Assert.Empty(viewModel.Toasts.Items);

        var log = WriteCrashLog(harness, instance.InstanceId, AbnormalExit(GameStarter.ProcessId));
        starter.Exit(UnhandledException);
        await viewModel.WhenGameExitCheckedAsync().WaitAsync(TimeSpan.FromSeconds(60));

        var toast = Assert.Single(viewModel.Toasts.Items);
        Assert.True(toast.IsFailed);
        Assert.Equal(harness.Localization.LaunchGameCrashed, toast.Message);
        Assert.Equal(NullReference, toast.Detail);
        Assert.Equal(harness.Localization.GameLogOpen, toast.ActionText);
        Assert.True(toast.HasActions);

        string? opened = null;
        viewModel.OpenWithSystem = path => opened = path;
        toast.RunActionCommand.Execute(null);

        Assert.Equal(log, opened);
        Assert.Empty(viewModel.Toasts.Items);
        Assert.Null(await harness.Services.GameCrashes.GetUnreportedAsync(instance, DateTimeOffset.MinValue));
    }

    [Fact]
    public async Task Play_StackNamesAModAssembly_TheToastNamesTheMod()
    {
        var (harness, starter, instance) = await PlayAsync(harness => InstalledContent.AddAsync(harness, "KSArmory", activate: true, ownership: ModInstallOwnership.Borea));
        using var _ = harness;

        WriteCrashLog(harness, instance.InstanceId, AbnormalExit(GameStarter.ProcessId), lines: CrashTail("   at KSArmory.RoundFollowable.DrawAxes()"));
        starter.Exit(UnhandledException);
        await harness.ViewModel.WhenGameExitCheckedAsync().WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(harness.Localization.FormatLaunchGameCrashedBy("KSArmory"), Assert.Single(harness.ViewModel.Toasts.Items).Message);
    }

    [Fact]
    public async Task Play_GameQuitsOrRestarts_ShowsNothing()
    {
        var (harness, starter, instance) = await PlayAsync();
        using var _ = harness;

        WriteCrashLog(harness, instance.InstanceId, AbnormalExit(GameStarter.ProcessId));
        starter.Exit(0);
        await harness.ViewModel.WhenGameExitCheckedAsync().WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Empty(harness.ViewModel.Toasts.Items);
    }

    /// <summary>A harness whose active instance was last launched an hour ago and holds the crash log <paramref name="name"/>.</summary>
    private static Task<ViewModelHarness> StartAfterACrashAsync(string name, DateTime writtenAt) =>
        ViewModelHarness.CreateAsync(async services =>
        {
            var instance = (await services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
            await services.Instances.SetActiveInstanceAsync(instance.InstanceId);
            await services.Instances.UpdateAsync(instance.InstanceId, saved =>
            {
                saved.RecordPlayed(DateTimeOffset.UtcNow.AddHours(-1));
                return true;
            });
            var path = Path.Combine(Path.GetDirectoryName(services.Paths.GetInstanceGameLogPath(instance.InstanceId))!, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllLinesAsync(path, CrashTail());
            File.SetLastWriteTimeUtc(path, writtenAt);
        });

    [Fact]
    public async Task Start_CrashLogNewerThanTheLastLaunch_ShowsTheToastOnce()
    {
        using var harness = await StartAfterACrashAsync("KittenSpaceAgency.260926-122536.43512.previous-crash.log", DateTime.UtcNow.AddMinutes(-30));
        var viewModel = harness.ViewModel;
        await viewModel.WhenCrashCheckedAsync();

        var toast = Assert.Single(viewModel.Toasts.Items);
        Assert.Equal(harness.Localization.LaunchGameCrashed, toast.Message);
        Assert.Equal(NullReference, toast.Detail);

        string? opened = null;
        viewModel.OpenWithSystem = path => opened = path;
        toast.RunActionCommand.Execute(null);
        Assert.EndsWith("KittenSpaceAgency.260926-122536.43512.previous-crash.log", opened);

        await viewModel.LoadAsync();
        await viewModel.WhenCrashCheckedAsync();

        Assert.Empty(viewModel.Toasts.Items);
        var instance = (await harness.Services.Instances.GetAllAsync()).Single();
        Assert.Null(await harness.Services.GameCrashes.GetUnreportedAsync(instance, instance.LastPlayedAt!.Value));
    }

    [Fact]
    public async Task Start_CrashLogOlderThanTheLastLaunch_ShowsNothing()
    {
        using var harness = await StartAfterACrashAsync(AbnormalExit(43512), DateTime.UtcNow.AddHours(-2));
        await harness.ViewModel.WhenCrashCheckedAsync();

        Assert.Empty(harness.ViewModel.Toasts.Items);
    }

    /// <summary>
    /// Hands out a game process that keeps running, writes <see cref="GameLog"/>
    /// when its start is watched, and ends when <see cref="Exit"/> is called.
    /// </summary>
    internal sealed class GameStarter : IProcessStarter
    {
        public const int ProcessId = 4244;

        private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int? _exitCode;

        public string? GameLog { get; set; }

        public void Exit(int exitCode)
        {
            _exitCode = exitCode;
            _exited.TrySetResult();
        }

        public IStartedProcess Start(LaunchPlan plan) => new GameProcess(this);

        private sealed class GameProcess(GameStarter owner) : IStartedProcess
        {
            public int Id => ProcessId;

            public bool HasExited => owner._exitCode is not null;

            public int? ExitCode => owner._exitCode;

            public IReadOnlyList<string> RecentOutput => [];

            public async Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
            {
                if (timeout == Timeout.InfiniteTimeSpan)
                {
                    await owner._exited.Task.WaitAsync(cancellationToken);
                    return true;
                }

                if (owner.GameLog is { } log)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                    File.WriteAllText(log, "KSA started.");
                }

                return HasExited;
            }

            public void Dispose()
            {
            }
        }
    }
}
