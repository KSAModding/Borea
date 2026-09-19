using System.Net;
using Borea.Core.GitHub;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Logging;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.Settings;
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
        var warning = new PlanningMessage("Example", PlanningMessageKind.Compatibility) { Compatibility = Borea.Core.Game.GameCompatibility.Untested };
        var planner = new LoggingInstallPlanner(new FixedPlanner(new InstallPlan(instance.InstanceId, InstallPlanningState.Capture(instance), [], [], [warning], [], [], [])), _log);

        await planner.PlanAsync(request);

        Assert.Equal(
            [
                $"Plan for instance {instance.InstanceId}: ready. Requested: Example 1.2.0 (Manual). Operations: none.",
                "Plan warning Example (compatibility): Game compatibility is Untested.",
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
        var plan = new LaunchPlan(Path.Combine(loader, "StarMap.exe"), ["-InstancePath", root, "-name", "say \"hi\""], loader, new Dictionary<string, string> { ["STARMAP_INSTANCE_PATH"] = root });
        var instance = new Instance("Main", InstanceSource.Custom.Value);
        var inner = new FixedLauncher(LaunchResult.Success(plan, 42, "Started."));
        var started = new LoggingLauncher(inner, _log);
        var failed = new LoggingLauncher(new FixedLauncher(LaunchResult.Failed(LaunchOutcome.LaunchTargetMissing, "StarMap.exe is not there.", plan)), _log);

        started.Launch(instance, loader: null, ["-name", "say \"hi\""]);
        failed.Launch(instance, loader: null);

        Assert.Equal(["-name", "say \"hi\""], inner.Arguments);
        var details = $"Executable: \"{plan.Executable}\". Arguments: -InstancePath {ArgumentLine.Join([root])} -name \"say \\\"hi\\\"\". Environment: STARMAP_INSTANCE_PATH=\"{root}\". Working directory: \"{loader}\".";
        Assert.Equal(
            [
                $"Launch of instance {instance.InstanceId} with no loader started process 42. {details}",
                $"Launch of instance {instance.InstanceId} with no loader did not start, LaunchTargetMissing: StarMap.exe is not there. {details}",
            ],
            _log.Messages);
    }

    [Fact]
    public void Launch_RefusedWithoutAPlan_WritesTheSavedAndGivenArguments()
    {
        var instance = new Instance("Main", InstanceSource.Custom.Value);
        instance.SetLaunchArguments(["-saved"]);
        var refused = new LoggingLauncher(new FixedLauncher(LaunchResult.Failed(LaunchOutcome.HandoverFlagInArguments, "Refused.")), _log);

        refused.Launch(instance, loader: null, ["-instancepath", "D:/Other dir"]);

        Assert.Equal(
            [$"Launch of instance {instance.InstanceId} with no loader did not start, HandoverFlagInArguments: Refused. Arguments: -saved -instancepath \"D:/Other dir\"."],
            _log.Messages);
    }

    [Fact]
    public async Task WatchStart_StoppedWhileLoadingMods_WritesTheLikelyModAndTheExitCode()
    {
        var loader = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "StarMap"));
        var plan = new LaunchPlan(Path.Combine(loader, "StarMap.exe"), [], loader, new Dictionary<string, string>());
        var instance = new Instance("Main", InstanceSource.Custom.Value);
        var launcher = new LoggingLauncher(new FixedLauncher(LaunchResult.Success(plan, 42, "Started.")), _log);

        await launcher.WatchStartAsync(instance, LaunchResult.ExitedEarly(plan, -1073741819, ["Fatal error."], "KSArmory", "Stopped.", LoaderCrashCause.ModLoading));
        await launcher.WatchStartAsync(instance, LaunchResult.ExitedEarly(plan, -1073741819, ["Fatal error."], null, "Stopped.", LoaderCrashCause.ModLoading));

        var exitCode = LoaderExitCode.Describe(-1073741819, OperatingSystem.IsWindows());
        Assert.Equal(
            [
                $"Launch of instance {instance.InstanceId} stopped early with exit code {exitCode}, stopped while loading mods, likely KSArmory. Last output:{Environment.NewLine}Fatal error.",
                $"Launch of instance {instance.InstanceId} stopped early with exit code {exitCode}, stopped while loading mods, no mod found. Last output:{Environment.NewLine}Fatal error.",
            ],
            _log.Messages);
    }

    [Fact]
    public void SharedProfileLaunch_WritesTheOutcomeAndThePlanWithTheArguments()
    {
        var game = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Game"));
        var plan = GameExecutable.Plan(game, "KSA.exe", ["-windowed"]);
        var inner = new FixedSharedProfileLauncher(SharedProfileLaunchResult.Success(plan, 7, "Started."));
        var started = new LoggingSharedProfileLauncher(inner, _log);
        var failed = new LoggingSharedProfileLauncher(new FixedSharedProfileLauncher(SharedProfileLaunchResult.Failed(SharedProfileLaunchOutcome.NoGameDirectory, "No game directory.")), _log);

        started.Launch(["-windowed"]);
        failed.Launch();

        Assert.Equal(["-windowed"], inner.Arguments);
        Assert.Equal(
            [
                $"Launch without a mod loader started process 7. Executable: \"{plan.Executable}\". Arguments: -windowed. Environment: none. Working directory: \"{game}\".",
                "Launch without a mod loader did not start, NoGameDirectory: No game directory.",
            ],
            _log.Messages);
    }

    [Fact]
    public async Task LibraryFolderChange_WritesTheStartAndTheOutcome()
    {
        var moved = new LoggingLibraryFolderChanger(new FixedLibraryFolderChanger(
            new LibraryFolderChangeResult(LibraryFolderChangeOutcome.Moved, "D:\\Library", "C:\\Borea", "Moved.") { OldFilesRemain = true }), _log);
        var refused = new LoggingLibraryFolderChanger(new FixedLibraryFolderChanger(
            new LibraryFolderChangeResult(LibraryFolderChangeOutcome.GameRunning, "C:\\Borea", "D:\\Library", "Close the game first.")), _log);

        await moved.ChangeAsync("D:\\Library");
        await refused.ChangeAsync(null);

        Assert.Equal(
            [
                "Library folder change to D:\\Library started.",
                "Library folder change to D:\\Library finished, Moved, old files remain in C:\\Borea.",
                "Library folder change to the default folder started.",
                "Library folder change to the default folder refused, GameRunning: Close the game first.",
            ],
            _log.Messages);
    }

    [Fact]
    public async Task LibraryFolderChange_CancelledOrFailed_WritesItAndRethrows()
    {
        var cancelled = new LoggingLibraryFolderChanger(new FixedLibraryFolderChanger(null, new OperationCanceledException()), _log);
        var failed = new LoggingLibraryFolderChanger(new FixedLibraryFolderChanger(null, new IOException("The disk is full.")), _log);

        await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled.ChangeAsync("D:\\Library"));
        await Assert.ThrowsAsync<IOException>(() => failed.ChangeAsync("D:\\Library"));

        Assert.Equal(
            [
                "Library folder change to D:\\Library started.",
                "Library folder change to D:\\Library was cancelled.",
                "Library folder change to D:\\Library started.",
                "Library folder change to D:\\Library failed.",
            ],
            _log.Messages);
        Assert.Null(_log.Exceptions[1]);
        Assert.IsType<IOException>(_log.Exceptions[3]);
    }

    [Fact]
    public async Task GitHub_SignInAndSignOut_WriteTheLogin()
    {
        var session = new LoggingGitHubSession(new FakeGitHubSession(), _log);

        await session.SignInAsync();
        session.SignOut();
        session.SignOut();

        Assert.Equal(["Signed in to GitHub as octocat.", "Signed out of GitHub."], _log.Messages);
    }

    [Fact]
    public async Task GitHub_FailedSignIn_WritesTheOutcome()
    {
        var session = new LoggingGitHubSession(new FakeGitHubSession { Outcome = GitHubSignInOutcome.Expired }, _log);

        await session.SignInAsync();

        Assert.Equal(["GitHub sign-in failed, Expired."], _log.Messages);
    }

    [Fact]
    public async Task GitHub_RefusedToken_WritesTheSignOut()
    {
        var inner = new FakeGitHubSession();
        var session = new LoggingGitHubSession(inner, _log);
        await session.SignInAsync();
        inner.Answer = HttpStatusCode.Unauthorized;

        using var response = await session.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user"));

        Assert.Equal("Signed out of GitHub, because GitHub refused the token.", _log.Messages[^1]);
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
        public IReadOnlyList<string>? Arguments { get; private set; }

        public LaunchResult Launch(Instance instance, ModMetadata? loader, IReadOnlyList<string>? arguments = null)
        {
            Arguments = arguments;
            return result;
        }

        public Task<LaunchResult> WatchStartAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default) => Task.FromResult(started);

        public bool IsRunning(Guid instanceId) => false;
    }

    private sealed class FixedLibraryFolderChanger(LibraryFolderChangeResult? result, Exception? failure = null) : ILibraryFolderChanger
    {
        public Task<LibraryFolderChangeResult> ChangeAsync(string? folder, IProgress<LibraryMoveProgress>? progress = null, CancellationToken cancellationToken = default)
            => failure is null ? Task.FromResult(result!) : Task.FromException<LibraryFolderChangeResult>(failure);
    }

    private sealed class FixedSharedProfileLauncher(SharedProfileLaunchResult result) : ISharedProfileLauncher
    {
        public IReadOnlyList<string>? Arguments { get; private set; }

        public SharedProfileLaunchResult Launch(IReadOnlyList<string>? arguments = null)
        {
            Arguments = arguments;
            return result;
        }
    }

    private sealed class FakeGitHubSession : IGitHubSession
    {
        public GitHubSignInOutcome Outcome { get; init; } = GitHubSignInOutcome.SignedIn;

        public HttpStatusCode Answer { get; set; } = HttpStatusCode.OK;

        public bool IsAvailable => true;

        public string ManageAccessUrl => "https://github.com/settings/apps/authorizations";

        public string InstallUrl => "https://github.com/apps/borea/installations/new";

        public GitHubSessionState State { get; private set; } = GitHubSessionState.SignedOut;

        public event EventHandler? StateChanged;

        public Task<GitHubSignInResult> SignInAsync(IProgress<GitHubDeviceCode>? progress = null, CancellationToken cancellationToken = default)
        {
            if (Outcome != GitHubSignInOutcome.SignedIn)
                return Task.FromResult(new GitHubSignInResult(Outcome));

            State = GitHubSessionState.SignedInAs("octocat");
            StateChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(new GitHubSignInResult(Outcome, "octocat"));
        }

        public void SignOut() => State = GitHubSessionState.SignedOut;

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            if (Answer == HttpStatusCode.Unauthorized)
                SignOut();

            return Task.FromResult(new HttpResponseMessage(Answer));
        }
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
