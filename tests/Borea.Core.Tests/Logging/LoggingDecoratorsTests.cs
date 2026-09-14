using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Logging;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.Tests.Mods;

namespace Borea.Core.Tests.Logging;

public sealed class LoggingDecoratorsTests
{
    private readonly RecordingLog _log = new();
    private readonly Guid _instanceId = Guid.NewGuid();

    [Fact]
    public async Task Install_Success_WritesStartAndFinishWithTheDownload()
    {
        var release = TestFixtures.SampleVersionMetadata("Example", "1.2.0");
        var installer = new LoggingModInstaller(new FakeInstaller(), _log);
        var reports = new List<InstallProgress>();

        await installer.InstallAsync(_instanceId, release, InstallReason.Manual, enable: true, new SynchronousProgress<InstallProgress>(reports.Add));

        Assert.Equal(
            [
                $"Install of Example 1.2.0 into instance {_instanceId} started, reason Manual.",
                "Install of Example 1.2.0 finished, 1024 bytes from https://example.com/mod.zip.",
            ],
            _log.Messages);
        Assert.Equal([InstallPhase.Downloading, InstallPhase.Extracting], reports.Select(report => report.Phase));
    }

    [Fact]
    public async Task Install_Failure_NamesThePhaseAndRethrows()
    {
        var release = TestFixtures.SampleVersionMetadata("Example", "1.2.0");
        var failure = new InvalidOperationException("The archive does not hold a mod.");
        var installer = new LoggingModInstaller(new FakeInstaller { Failure = failure }, _log);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(_instanceId, release, InstallReason.Dependency, enable: true));

        Assert.Same(failure, thrown);
        Assert.Equal("Install of Example 1.2.0 failed while extracting.", _log.Messages[^1]);
        Assert.Same(failure, _log.Exceptions[^1]);
    }

    [Fact]
    public async Task Install_Cancelled_WritesTheCancellationWithoutTheException()
    {
        var release = TestFixtures.SampleVersionMetadata("Example", "1.2.0");
        var installer = new LoggingModInstaller(new FakeInstaller { Failure = new OperationCanceledException() }, _log);

        await Assert.ThrowsAsync<OperationCanceledException>(() => installer.InstallAsync(_instanceId, release, InstallReason.Manual, enable: true));

        Assert.Equal("Install of Example 1.2.0 cancelled while extracting.", _log.Messages[^1]);
        Assert.Null(_log.Exceptions[^1]);
    }

    [Fact]
    public async Task Replace_Failure_BeforeAnyReport_SaysSo()
    {
        var current = TestFixtures.SampleInstalledMod("Example", "1.0.0");
        var replacement = TestFixtures.SampleVersionMetadata("Example", "2.0.0");
        var replacer = new LoggingModReplacer(new FailingReplacer(), _log);

        await Assert.ThrowsAsync<IOException>(() => replacer.ReplaceAsync(_instanceId, current, replacement));

        Assert.Equal(
            [
                $"Replacement of Example 1.0.0 with 2.0.0 in instance {_instanceId} started.",
                "Replacement of Example 1.0.0 with 2.0.0 failed before the download.",
            ],
            _log.Messages);
    }

    [Fact]
    public async Task Plan_WritesTheRequestTheOutcomeAndEachWarning()
    {
        var instance = new Instance("Main", InstanceSource.Custom.Value);
        var release = TestFixtures.SampleVersionMetadata("Example", "1.2.0");
        var request = new InstallPlanningRequest(instance, [new RequestedMod(release, InstallReason.Manual)], new EmptyRepository());
        var warning = new PlanningMessage("Example", "game-untested", "Example is not tested with this game version.");
        var planner = new LoggingInstallPlanner(new FixedPlanner(new InstallPlan(instance.InstanceId, InstallPlanningState.Capture(instance), [], [], [warning], [], [], [])), _log);

        await planner.PlanAsync(request);

        Assert.Equal(
            [
                $"Plan for instance {instance.InstanceId}: ready. Requested: Example 1.2.0 (Manual). Operations: none.",
                "Plan warning Example (game-untested): Example is not tested with this game version.",
            ],
            _log.Messages);
    }

    [Fact]
    public async Task IndexFetch_WritesTheResultAndTheFailure()
    {
        var fetcher = new LoggingContentIndexFetcher(new SequenceFetcher(ContentIndexFetchResult.NotModified), _log);
        await fetcher.FetchAsync("index.json");

        var failing = new LoggingContentIndexFetcher(new SequenceFetcher(null), _log);
        await Assert.ThrowsAsync<HttpRequestException>(() => failing.FetchAsync("index.json"));

        Assert.Equal(
            [
                "Index fetch: not modified, the ETag matched.",
                "Index fetch failed: HttpRequestException: Response status code does not indicate success: 503.",
            ],
            _log.Messages);
    }

    [Fact]
    public async Task PlanIndexFetchAndRemoval_Cancelled_WriteTheCancellationWithoutTheException()
    {
        var instance = new Instance("Main", InstanceSource.Custom.Value);
        var release = TestFixtures.SampleVersionMetadata("Example", "1.2.0");
        var request = new InstallPlanningRequest(instance, [new RequestedMod(release, InstallReason.Manual)], new EmptyRepository());
        var cancelled = new CancelledServices();

        await Assert.ThrowsAsync<OperationCanceledException>(() => new LoggingInstallPlanner(cancelled, _log).PlanAsync(request));
        await Assert.ThrowsAsync<OperationCanceledException>(() => new LoggingContentIndexFetcher(cancelled, _log).FetchAsync("index.json"));
        await Assert.ThrowsAsync<OperationCanceledException>(() => new LoggingModUninstaller(cancelled, _log).UninstallAsync(_instanceId, "Example"));

        Assert.Equal(
            [
                $"Plan for instance {instance.InstanceId} cancelled. Requested: Example 1.2.0 (Manual).",
                "Index fetch cancelled.",
                $"Removal of Example from instance {_instanceId} started.",
                "Removal of Example cancelled.",
            ],
            _log.Messages);
        Assert.All(_log.Exceptions, exception => Assert.Null(exception));
    }

    [Fact]
    public void Launch_WritesTheOutcomeAndThePlan()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Instances", "one"));
        var loader = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "StarMap"));
        var plan = new LaunchPlan(Path.Combine(loader, "StarMap.exe"), ["-InstancePath", root], loader, new Dictionary<string, string> { ["STARMAP_INSTANCE_PATH"] = root });
        var instance = new Instance("Main", InstanceSource.Custom.Value);
        var started = new LoggingLauncher(new FixedLauncher(LaunchResult.Success(plan, 42, "Started.")), _log);
        var failed = new LoggingLauncher(new FixedLauncher(LaunchResult.Failed(LaunchOutcome.LaunchTargetMissing, "StarMap.exe is not there.", plan)), _log);

        started.Launch(instance, loader: null);
        failed.Launch(instance, loader: null);

        var details = $"Executable: \"{plan.Executable}\". Arguments: \"-InstancePath\" \"{root}\". Environment: STARMAP_INSTANCE_PATH=\"{root}\". Working directory: \"{loader}\".";
        Assert.Equal(
            [
                $"Launch of instance {instance.InstanceId} with no loader started process 42. {details}",
                $"Launch of instance {instance.InstanceId} with no loader did not start, LaunchTargetMissing: StarMap.exe is not there. {details}",
            ],
            _log.Messages);
    }

    private sealed class RecordingLog : IBoreaLog
    {
        public List<string> Messages { get; } = [];

        public List<Exception?> Exceptions { get; } = [];

        public string CurrentFilePath => "borea.log";

        public void Write(string message)
        {
            Messages.Add(message);
            Exceptions.Add(null);
        }

        public void Write(string message, Exception exception)
        {
            Messages.Add(message);
            Exceptions.Add(exception);
        }

        public IReadOnlyList<string> ReadRecentLines(int maxLines) => [];
    }

    private sealed class FakeInstaller : IModInstaller
    {
        public Exception? Failure { get; init; }

        public Task<InstallResult> InstallAsync(Guid instanceId, ModVersionMetadata release, InstallReason reason, bool enable, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            progress.Report(release, InstallPhase.Downloading);
            progress.Report(release, InstallPhase.Extracting);
            if (Failure is not null)
                throw Failure;

            var mod = TestFixtures.SampleInstalledMod(release.ModId, release.Version.ToString());
            return Task.FromResult(new InstallResult(mod, new DownloadResult("https://example.com/mod.zip", 1024, new string('A', 64)), default!));
        }

        public Task<GuardedInstallResult> InstallGuardedAsync(Guid instanceId, ModVersionMetadata release, InstallReason reason, bool enable, InstallPlanningState expectedState, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FailingReplacer : IModReplacer
    {
        public Task<ModReplacementResult> ReplaceAsync(Guid instanceId, InstalledMod expectedCurrent, ModVersionMetadata replacement, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
            => throw new IOException("The mods folder is locked.");

        public Task<GuardedModReplacementResult> ReplaceGuardedAsync(Guid instanceId, InstalledMod expectedCurrent, ModVersionMetadata replacement, InstallPlanningState expectedState, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FixedPlanner(InstallPlan plan) : IInstallPlanner
    {
        public Task<InstallPlan> PlanAsync(InstallPlanningRequest request, CancellationToken cancellationToken = default) => Task.FromResult(plan);
    }

    private sealed class SequenceFetcher(ContentIndexFetchResult? result) : IContentIndexFetcher
    {
        public Task<ContentIndexFetchResult> FetchAsync(string destinationPath, CancellationToken ct = default)
            => result is { } value
                ? Task.FromResult(value)
                : throw new HttpRequestException("Response status code does not indicate success: 503.");
    }

    private sealed class CancelledServices : IInstallPlanner, IContentIndexFetcher, IModUninstaller
    {
        public Task<InstallPlan> PlanAsync(InstallPlanningRequest request, CancellationToken cancellationToken = default) => throw new OperationCanceledException();

        public Task<ContentIndexFetchResult> FetchAsync(string destinationPath, CancellationToken ct = default) => throw new OperationCanceledException();

        public Task UninstallAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default) => throw new OperationCanceledException();
    }

    private sealed class FixedLauncher(LaunchResult result) : ILauncher
    {
        public LaunchResult Launch(Instance instance, ModMetadata? loader) => result;

        public Task<LaunchResult> WatchStartAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default) => Task.FromResult(started);

        public bool IsRunning(Guid instanceId) => false;
    }

    private sealed class EmptyRepository : IModRepository
    {
        public Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ModMetadata>>([]);

        public Task<ModMetadata?> GetListingAsync(string modId, CancellationToken cancellationToken = default) => Task.FromResult<ModMetadata?>(null);

        public Task<ModVersionMetadata?> GetLatestReleaseAsync(string modId, CancellationToken cancellationToken = default) => Task.FromResult<ModVersionMetadata?>(null);

        public Task<ModVersionMetadata?> GetReleaseAsync(string modId, ModVersion version, CancellationToken cancellationToken = default) => Task.FromResult<ModVersionMetadata?>(null);

        public Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ModVersion>>([]);

        public Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ModMetadata>>([]);
    }
}
