using System.ComponentModel;
using System.Net;
using System.Text;
using Borea.App.ViewModels;
using Borea.Core.GitHub;
using Borea.Core.Preferences;
using Borea.Core.Secrets;
using Borea.Network.GitHub;

namespace Borea.App.Tests.ViewModels;

public sealed class GitHubAccountViewModelTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private const string KeptName = "github-refresh-token";

    private readonly FakeGitHubSession _session = new();

    [Theory]
    [InlineData("", "")]
    [InlineData("Iv23.testclient", "")]
    [InlineData("", "borea-test")]
    public async Task WithoutClientIdOrSlug_HidesTheAccountAndDoesNotSignIn(string clientId, string slug)
    {
        var session = new GitHubSession(new HttpClient(), clientId, slug);
        using var harness = await ViewModelHarness.CreateAsync(gitHub: session);
        var viewModel = harness.ViewModel;

        viewModel.SignInToGitHubCommand.Execute(null);

        Assert.False(viewModel.IsGitHubAccountAvailable);
        Assert.False(viewModel.IsGitHubSignInOpen);
        Assert.False(await viewModel.SignInToGitHubAsync());
        Assert.Equal(GitHubSessionStatus.SignedOut, session.State.Status);
    }

    [Fact]
    public async Task BuiltInApp_ShowsTheAccountOnlyWithClientIdAndSlug()
    {
        using var harness = await ViewModelHarness.CreateAsync();

        Assert.Equal(BoreaGitHubApp.ClientId.Length > 0 && BoreaGitHubApp.Slug.Length > 0, harness.ViewModel.IsGitHubAccountAvailable);
    }

    [Fact]
    public async Task SignedOut_ShowsTheSignIn()
    {
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;

        Assert.True(viewModel.IsGitHubAccountAvailable);
        Assert.False(viewModel.IsGitHubSignedIn);
        Assert.Null(viewModel.GitHubLogin);
        Assert.False(viewModel.IsGitHubSignInOpen);
    }

    [Fact]
    public async Task SignIn_ShowsTheCodeOpensGitHubAndEndsSignedIn()
    {
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;
        var window = new FakeWindowServices();
        var opened = new List<string>();
        viewModel.WindowServices = window;
        viewModel.OpenWithSystem = opened.Add;

        viewModel.SignInToGitHubCommand.Execute(null);

        Assert.True(viewModel.IsGitHubSignInOpen);
        Assert.True(viewModel.IsGettingGitHubCode);
        _session.ReportCode();
        await WaitUntilAsync(() => viewModel.HasGitHubCode);
        Assert.Equal("WDJB-MJHT", viewModel.GitHubUserCode);
        Assert.False(viewModel.IsGettingGitHubCode);

        await viewModel.CopyGitHubCodeAndOpenCommand.ExecuteAsync(null);

        Assert.Equal(["WDJB-MJHT"], window.Copied);
        Assert.Equal(["https://github.com/login/device"], opened);

        _session.Finish(GitHubSignInOutcome.SignedIn);
        await viewModel.WhenGitHubSignInDoneAsync();

        Assert.False(viewModel.IsGitHubSignInOpen);
        Assert.False(viewModel.IsGitHubSignInRunning);
        Assert.Null(viewModel.GitHubUserCode);
        Assert.True(viewModel.IsGitHubSignedIn);
        Assert.Equal("octocat", viewModel.GitHubLogin);
        Assert.Equal("Signed in as octocat", viewModel.GitHubSignedInText);
    }

    [Fact]
    public async Task SignInToGitHubAsync_CompletesWhenTheModalCloses()
    {
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;

        var signIn = viewModel.SignInToGitHubAsync();
        Assert.True(viewModel.IsGitHubSignInOpen);
        Assert.Same(signIn, viewModel.SignInToGitHubAsync());

        _session.Finish(GitHubSignInOutcome.SignedIn);

        Assert.True(await signIn.WaitAsync(Timeout));
        Assert.False(viewModel.IsGitHubSignInOpen);
        Assert.True(await viewModel.SignInToGitHubAsync());
    }

    [Fact]
    public async Task SignInToGitHubAsync_RetryAfterAFailure_KeepsWaitingUntilTheModalCloses()
    {
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;
        var signIn = viewModel.SignInToGitHubAsync();
        _session.Finish(GitHubSignInOutcome.Expired);
        await viewModel.WhenGitHubSignInDoneAsync();
        Assert.False(signIn.IsCompleted);

        viewModel.SignInToGitHubCommand.Execute(null);
        viewModel.CancelGitHubSignInCommand.Execute(null);
        await viewModel.WhenGitHubSignInDoneAsync();

        Assert.False(await signIn.WaitAsync(Timeout));
        Assert.False(viewModel.IsGitHubSignInOpen);
    }

    [Fact]
    public async Task SignInToGitHubAsync_RightAfterACancel_OpensTheModalWhenTheCancelledRunEnds()
    {
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;
        await viewModel.WhenGitHubResumedAsync();
        _session.HoldCancel = true;
        viewModel.SignInToGitHubCommand.Execute(null);
        viewModel.CancelGitHubSignInCommand.Execute(null);
        Assert.True(viewModel.IsGitHubSignInRunning);

        var signIn = viewModel.SignInToGitHubAsync();
        Assert.False(viewModel.IsGitHubSignInOpen);
        _session.HoldCancel = false;
        _session.EndCancel();
        await WaitUntilAsync(() => _session.SignIns == 2);

        Assert.True(viewModel.IsGitHubSignInOpen);
        _session.Finish(GitHubSignInOutcome.SignedIn);
        Assert.True(await signIn.WaitAsync(Timeout));
    }

    [Theory]
    [InlineData(GitHubSignInOutcome.Expired, "The code expired. Try again for a new one.")]
    [InlineData(GitHubSignInOutcome.AccessDenied, "The sign-in was refused on GitHub.")]
    [InlineData(GitHubSignInOutcome.DeviceFlowDisabled, "Sign-in with a code is turned off for Borea on GitHub. Report the problem.")]
    [InlineData(GitHubSignInOutcome.NetworkError, "Cannot reach GitHub. Check your connection and try again.")]
    public async Task FailedSignIn_KeepsTheModalWithTheReason(GitHubSignInOutcome outcome, string message)
    {
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;

        viewModel.SignInToGitHubCommand.Execute(null);
        _session.Finish(outcome);
        await viewModel.WhenGitHubSignInDoneAsync();

        Assert.True(viewModel.IsGitHubSignInOpen);
        Assert.Equal(message, viewModel.GitHubSignInError);
        Assert.True(viewModel.CanRetryGitHubSignIn);
        Assert.False(viewModel.IsGitHubSignedIn);
    }

    [Fact]
    public async Task Cancel_ClosesTheModalAndStopsTheSignIn()
    {
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;
        viewModel.SignInToGitHubCommand.Execute(null);

        viewModel.CancelGitHubSignInCommand.Execute(null);
        await viewModel.WhenGitHubSignInDoneAsync();

        Assert.True(_session.WasCancelled);
        Assert.False(viewModel.IsGitHubSignInOpen);
        Assert.False(viewModel.IsGitHubSignInRunning);
        Assert.Null(viewModel.GitHubSignInError);
        Assert.False(viewModel.IsGitHubSignedIn);
    }

    [Fact]
    public async Task SignIn_FromSettingsIsKeptAndFromTheListingPageIsNot()
    {
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;

        var listing = viewModel.SignInToGitHubAsync(forListing: true);
        _session.Finish(GitHubSignInOutcome.SignedIn);
        Assert.True(await listing.WaitAsync(Timeout));
        viewModel.SignOutOfGitHubForListing();
        Assert.False(viewModel.IsGitHubSignedIn);

        viewModel.SignInToGitHubCommand.Execute(null);
        _session.Finish(GitHubSignInOutcome.SignedIn);
        await viewModel.WhenGitHubSignInDoneAsync();

        Assert.Equal([false, true], _session.KeptSignIns);
    }

    [Fact]
    public async Task SignedIn_SignsOutAndOpensTheAccessPage()
    {
        _session.SignInDirectly();
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;
        Assert.True(viewModel.IsGitHubSignedIn);

        viewModel.OpenGitHubAccessCommand.Execute(null);
        viewModel.SignOutOfGitHubCommand.Execute(null);

        Assert.Equal([FakeGitHubSession.AccessUrl], opened);
        Assert.False(viewModel.IsGitHubSignedIn);
        Assert.Null(viewModel.GitHubLogin);
    }

    [Fact]
    public async Task SignOutByTheSession_AfterARebuild_UpdatesTheAccount()
    {
        _session.SignInDirectly();
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;
        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);
        viewModel.GameDirectoryInput = Directory.CreateDirectory(Path.Combine(harness.Root, "game")).FullName;
        await viewModel.SaveGameDirectoryCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsGitHubSignedIn);
        var changes = new List<string?>();
        ((INotifyPropertyChanged)viewModel).PropertyChanged += (_, e) =>
        {
            lock (changes)
                changes.Add(e.PropertyName);
        };

        _session.SignOut();

        await WaitUntilAsync(() =>
        {
            lock (changes)
                return changes.Contains(nameof(MainViewModel.IsGitHubSignedIn));
        });
        Assert.False(viewModel.IsGitHubSignedIn);
        lock (changes)
            Assert.Single(changes, name => name == nameof(MainViewModel.IsGitHubSignedIn));
    }

    [Theory]
    [InlineData("always", "always", true)]
    [InlineData("never", "pull_requests_only", true)]
    [InlineData("never", "never", false)]
    [InlineData(null, null, false)]
    public async Task SignedIn_ShowsTheStewardRoleOfEitherIndexRepository(string? index, string? releases, bool steward)
    {
        _session.Bypass["KSAModding/content-index"] = index;
        _session.Bypass["KSAModding/content-index-releases"] = releases;
        _session.SignInDirectly();
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;

        await viewModel.WhenStewardRoleCheckedAsync();

        Assert.Equal(steward, viewModel.IsGitHubSteward);
        Assert.Equal("Steward of the content index", harness.Localization.SettingsGitHubSteward);
        Assert.Contains("https://api.github.com/repos/KSAModding/content-index/rulesets/1", _session.Sent);
        Assert.Contains("https://api.github.com/repos/KSAModding/content-index-releases/rulesets/1", _session.Sent);
    }

    [Fact]
    public async Task SignIn_ShowsTheStewardRoleWhenItsCheckEnds()
    {
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;
        var shown = new List<bool>();
        ((INotifyPropertyChanged)viewModel).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsGitHubSteward))
            {
                lock (shown)
                    shown.Add(viewModel.IsGitHubSteward);
            }
        };
        var hold = _session.Hold = new TaskCompletionSource();

        viewModel.SignInToGitHubCommand.Execute(null);
        _session.Finish(GitHubSignInOutcome.SignedIn);
        await viewModel.WhenGitHubSignInDoneAsync();
        Assert.False(viewModel.IsGitHubSteward);
        hold.SetResult();
        await viewModel.WhenStewardRoleCheckedAsync();

        Assert.True(viewModel.IsGitHubSteward);
        await WaitUntilAsync(() =>
        {
            lock (shown)
                return shown[^1];
        });
    }

    [Fact]
    public async Task SignOut_HidesTheStewardRoleAndTheNextSignInChecksAgain()
    {
        _session.SignInDirectly();
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;
        await viewModel.WhenStewardRoleCheckedAsync();
        Assert.True(viewModel.IsGitHubSteward);

        viewModel.SignOutOfGitHubCommand.Execute(null);

        Assert.False(viewModel.IsGitHubSteward);
        Assert.Null(harness.Services.StewardRole.Current);

        _session.Bypass["KSAModding/content-index"] = "never";
        _session.Bypass["KSAModding/content-index-releases"] = "never";
        _session.Login = "alice";
        viewModel.SignInToGitHubCommand.Execute(null);
        _session.Finish(GitHubSignInOutcome.SignedIn);
        await viewModel.WhenGitHubSignInDoneAsync();
        await viewModel.WhenStewardRoleCheckedAsync();

        Assert.Equal("alice", viewModel.GitHubLogin);
        Assert.Equal("alice", harness.Services.StewardRole.Current?.Login);
        Assert.False(viewModel.IsGitHubSteward);
    }

    [Fact]
    public async Task Rebuild_ChecksTheStewardRoleAgain()
    {
        _session.SignInDirectly();
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;
        await viewModel.WhenStewardRoleCheckedAsync();
        var before = harness.Services;

        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);
        viewModel.GameDirectoryInput = Directory.CreateDirectory(Path.Combine(harness.Root, "game")).FullName;
        await viewModel.SaveGameDirectoryCommand.ExecuteAsync(null);
        await viewModel.WhenStewardRoleCheckedAsync();

        Assert.NotSame(before, harness.Services);
        Assert.True(viewModel.IsGitHubSteward);
        Assert.Null(before.StewardRole.Current);
    }

    [Fact]
    public async Task Start_WithAKeptSignIn_SignsInWithoutTheCodeAndChecksTheStewardRole()
    {
        var resume = _session.Resume = new TaskCompletionSource<GitHubResumeOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;
        await WaitUntilAsync(() => viewModel.IsGitHubResuming);
        Assert.False(viewModel.CanStartGitHubSignIn);
        Assert.Equal("Signing you in to GitHub again...", harness.Localization.SettingsGitHubResuming);

        resume.SetResult(GitHubResumeOutcome.SignedIn);
        await viewModel.WhenGitHubResumedAsync();
        await WaitUntilAsync(() => viewModel.IsGitHubSignedIn);
        await viewModel.WhenStewardRoleCheckedAsync();

        Assert.Equal(1, _session.Resumes);
        Assert.Equal(0, _session.SignIns);
        Assert.False(viewModel.IsGitHubResuming);
        Assert.Equal("octocat", viewModel.GitHubLogin);
        Assert.True(viewModel.IsGitHubSteward);
        Assert.Contains("https://api.github.com/repos/KSAModding/content-index/rulesets/1", _session.Sent);
    }

    [Theory]
    [InlineData(GitHubResumeOutcome.SignedIn, false)]
    [InlineData(GitHubResumeOutcome.Refused, true)]
    [InlineData(GitHubResumeOutcome.Unreachable, true)]
    public async Task SignIn_WhileResuming_WaitsAndAsksForTheCodeOnlyWhenTheResumeFailed(GitHubResumeOutcome outcome, bool asksForTheCode)
    {
        var resume = _session.Resume = new TaskCompletionSource<GitHubResumeOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;
        await WaitUntilAsync(() => viewModel.IsGitHubResuming);

        var signIn = viewModel.SignInToGitHubAsync();
        Assert.False(viewModel.IsGitHubSignInOpen);
        resume.SetResult(outcome);
        await viewModel.WhenGitHubResumedAsync();
        await WaitUntilAsync(() => signIn.IsCompleted || _session.SignIns > 0);

        Assert.Equal(asksForTheCode, viewModel.IsGitHubSignInOpen);
        Assert.Equal(asksForTheCode ? 1 : 0, _session.SignIns);
        viewModel.CancelGitHubSignInCommand.Execute(null);
        Assert.Equal(!asksForTheCode, await signIn.WaitAsync(Timeout));
    }

    [Fact]
    public async Task StaySignedIn_IsOnByDefaultAndReachesTheSession()
    {
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);

        Assert.True(harness.ViewModel.StaySignedInToGitHub);
        Assert.True(_session.KeepSignedIn);
        Assert.Equal(1, _session.Resumes);
        Assert.Equal("Stay signed in on this computer", harness.Localization.SettingsGitHubStaySignedIn);
    }

    [Fact]
    public async Task StaySignedIn_TurnedOff_StopsKeepingAndIsSaved()
    {
        _session.SignInDirectly();
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;

        viewModel.StaySignedInToGitHub = false;
        await viewModel.WhenPreferencesSavedAsync();

        Assert.False(_session.KeepSignedIn);
        Assert.True(viewModel.IsGitHubSignedIn);
        var saved = await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames);
        Assert.False(saved.Preferences.StaySignedInToGitHub);
    }

    [Fact]
    public async Task StaySignedInOff_AtStart_DoesNotKeepTheSignIn()
    {
        using var harness = await ViewModelHarness.CreateAsync(
            seed: services => services.AppPreferences.SaveAsync(AppPreferences.Empty.WithStaySignedInToGitHub(false), MainViewModel.BundledThemeNames),
            gitHub: _session);

        Assert.False(harness.ViewModel.StaySignedInToGitHub);
        Assert.False(_session.KeepSignedIn);
    }

    [Theory]
    [InlineData(SecretStoreProblem.Unsupported, "Borea has no secret store for this system, so the sign-in ends when Borea closes.")]
    [InlineData(SecretStoreProblem.Missing, "No secret store was found, such as a keyring with the Secret Service on Linux, so the sign-in ends when Borea closes.")]
    [InlineData(SecretStoreProblem.Refused, "The secret store of your system refused the sign-in, for example because it is locked, so the sign-in ends when Borea closes.")]
    public async Task WithoutASecretStore_SettingsSaysWhy(SecretStoreProblem problem, string text)
    {
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;
        Assert.Null(viewModel.GitHubKeepSignedInProblemText);
        var shown = new List<string?>();
        ((INotifyPropertyChanged)viewModel).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.GitHubKeepSignedInProblemText))
            {
                lock (shown)
                    shown.Add(viewModel.GitHubKeepSignedInProblemText);
            }
        };

        _session.ReportProblem(problem);

        await WaitUntilAsync(() =>
        {
            lock (shown)
                return shown.Contains(text);
        });
    }

    [Fact]
    public async Task Restart_WithTheRealSession_IsSignedInWithoutTheCode()
    {
        var secrets = new MemorySecretStore();
        var sent = new System.Collections.Concurrent.ConcurrentQueue<string>();
        GitHubSession Session() => new(new HttpClient(new AnswerHandler(request =>
        {
            sent.Enqueue(request.RequestUri!.AbsoluteUri);
            return request.RequestUri.AbsoluteUri switch
            {
                "https://github.com/login/oauth/access_token" => """{"access_token":"ghu_refreshed","expires_in":28800,"refresh_token":"ghr_refreshed","refresh_token_expires_in":15897600,"token_type":"bearer"}""",
                "https://api.github.com/user" => """{"login":"octocat","id":1}""",
                _ => """{"message":"Not Found"}""",
            };
        })), "Iv23.testclient", "borea-test", secrets: secrets);
        secrets.Write(KeptName, "ghr_kept");
        var session = Session();

        using var harness = await ViewModelHarness.CreateAsync(gitHub: session);
        await harness.ViewModel.WhenGitHubResumedAsync();
        await WaitUntilAsync(() => secrets.Read(KeptName) == "ghr_refreshed");

        Assert.True(harness.ViewModel.IsGitHubSignedIn);
        Assert.Equal("ghr_refreshed", secrets.Read(KeptName));
        Assert.DoesNotContain("https://github.com/login/device/code", sent);

        harness.ViewModel.SignOutOfGitHubCommand.Execute(null);

        await WaitUntilAsync(() => secrets.Read(KeptName) is null);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class FakeWindowServices : IWindowServices
    {
        public List<string> Copied { get; } = [];

        public Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string fileTypeName, string text) => Task.FromResult<string?>(null);

        public Task<PickedTextFile?> OpenTextFileAsync(string title, string fileTypeName) => Task.FromResult<PickedTextFile?>(null);

        public Task<PickedBinaryFile?> OpenImageFileAsync(string title, string fileTypeName, long maxBytes) => Task.FromResult<PickedBinaryFile?>(null);

        public Task CopyTextAsync(string text)
        {
            Copied.Add(text);
            return Task.CompletedTask;
        }
    }

    private sealed class MemorySecretStore : ISecretStore
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _secrets = new();

        public string? Read(string name) => _secrets.GetValueOrDefault(name);

        public void Write(string name, string secret) => _secrets[name] = secret;

        public void Delete(string name) => _secrets.TryRemove(name, out _);
    }

    private sealed class AnswerHandler(Func<HttpRequestMessage, string> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer(request), Encoding.UTF8, "application/json") });
    }

    /// <summary>
    /// A session whose sign-in waits until the test reports the code and finishes it, and whose resume waits for <see cref="Resume"/> when that is set.
    /// It answers the rulesets of the index repositories.
    /// </summary>
    private sealed class FakeGitHubSession : IGitHubSession
    {
        public const string AccessUrl = "https://github.com/settings/apps/authorizations";

        private readonly GitHubDeviceCode _code = new("WDJB-MJHT", "https://github.com/login/device");
        private TaskCompletionSource<GitHubSignInResult>? _signIn;
        private IProgress<GitHubDeviceCode>? _progress;
        private TaskCompletionSource<GitHubSignInResult>? _heldCancel;
        private int _resumes;
        private int _signIns;

        public bool IsAvailable => true;

        public string ManageAccessUrl => AccessUrl;

        public string InstallUrl => "https://github.com/apps/borea-test/installations/new";

        public string InstallUrlFor(long repositoryId) => InstallUrl + "?repository=" + repositoryId;

        public GitHubSessionState State { get; private set; } = GitHubSessionState.SignedOut;

        public bool WasCancelled { get; private set; }

        /// <summary>
        /// How many sign-ins started.
        /// A sign-in can start on another thread, so the count goes up last and with a fence, and a test that sees the new count also sees that sign-in and the view model state before it, also on arm64.
        /// </summary>
        public int SignIns => Volatile.Read(ref _signIns);

        /// <summary>The keepSignedIn argument of every sign-in, in order.</summary>
        public List<bool> KeptSignIns { get; } = [];

        /// <summary>True keeps a cancelled sign-in running until <see cref="EndCancel"/>.</summary>
        public bool HoldCancel { get; set; }

        public string Login { get; set; } = "octocat";

        /// <summary>current_user_can_bypass of the main ruleset per repository, which the ruleset leaves out for null.</summary>
        public Dictionary<string, string?> Bypass { get; } = new()
        {
            ["KSAModding/content-index"] = "always",
            ["KSAModding/content-index-releases"] = "always",
        };

        public System.Collections.Concurrent.ConcurrentQueue<string> Sent { get; } = new();

        /// <summary>Holds every answer until it is set.</summary>
        public TaskCompletionSource? Hold { get; set; }

        public event EventHandler? StateChanged;

        public event EventHandler? KeepSignedInProblemChanged;

        public bool KeepSignedIn { get; set; }

        public SecretStoreProblem? KeepSignedInProblem { get; private set; }

        public TaskCompletionSource<GitHubResumeOutcome>? Resume { get; set; }

        public int Resumes => Volatile.Read(ref _resumes);

        public async Task<GitHubResumeOutcome> ResumeAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _resumes);
            if (Resume is not { } resume)
                return GitHubResumeOutcome.NothingKept;

            State = GitHubSessionState.Resuming;
            StateChanged?.Invoke(this, EventArgs.Empty);
            var outcome = await resume.Task;
            if (outcome == GitHubResumeOutcome.SignedIn)
                SignInDirectly();
            else
                SignOut();

            return outcome;
        }

        public void ReportProblem(SecretStoreProblem problem)
        {
            KeepSignedInProblem = problem;
            KeepSignedInProblemChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task<GitHubSignInResult> SignInAsync(IProgress<GitHubDeviceCode>? progress = null, bool keepSignedIn = true, CancellationToken cancellationToken = default)
        {
            _progress = progress;
            var signIn = _signIn = new TaskCompletionSource<GitHubSignInResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            KeptSignIns.Add(keepSignedIn);
            using var registration = cancellationToken.Register(() =>
            {
                WasCancelled = true;
                if (HoldCancel)
                    _heldCancel = signIn;
                else
                    signIn.TrySetCanceled(cancellationToken);
            });
            Interlocked.Increment(ref _signIns);

            var result = await signIn.Task;
            if (result.SignedIn)
                SignInDirectly();

            return result;
        }

        public void ReportCode()
        {
            State = GitHubSessionState.WaitingFor(_code);
            _progress?.Report(_code);
        }

        public void EndCancel() => _heldCancel?.TrySetCanceled();

        public void Finish(GitHubSignInOutcome outcome) =>
            _signIn!.SetResult(new GitHubSignInResult(outcome, outcome == GitHubSignInOutcome.SignedIn ? Login : null));

        public void SignInDirectly()
        {
            State = GitHubSessionState.SignedInAs(Login);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SignOut()
        {
            State = GitHubSessionState.SignedOut;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            if (Hold is { } hold)
                await hold.Task.WaitAsync(cancellationToken);

            var url = request.RequestUri!.AbsoluteUri;
            Sent.Enqueue(url);
            var repository = Bypass.Keys.FirstOrDefault(name => url.StartsWith($"https://api.github.com/repos/{name}/rulesets", StringComparison.Ordinal));
            var body = repository is null ? null
                : url.EndsWith("/rulesets?per_page=100&page=1", StringComparison.Ordinal) ? """[{"id":1,"target":"branch","enforcement":"active"}]"""
                : url.EndsWith("/rulesets/1", StringComparison.Ordinal) ? """{"id":1,"target":"branch","enforcement":"active","conditions":{"ref_name":{"include":["~DEFAULT_BRANCH"],"exclude":[]}}"""
                    + (Bypass[repository] is { } bypass ? $$""","current_user_can_bypass":"{{bypass}}"}""" : "}")
                : null;
            return new HttpResponseMessage(body is null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            {
                Content = new StringContent(body ?? """{"message":"Not Found"}""", Encoding.UTF8, "application/json"),
            };
        }
    }
}
