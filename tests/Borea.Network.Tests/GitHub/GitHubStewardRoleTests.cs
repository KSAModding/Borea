using System.Net;
using System.Text;
using System.Text.Json;
using Borea.Core.GitHub;
using Borea.Core.Secrets;
using Borea.Core.Stewardship;
using Borea.Network.GitHub;

namespace Borea.Network.Tests;

public sealed class GitHubStewardRoleTests
{
    private const string Api = "https://api.github.com";
    private const string Index = "KSAModding/content-index";
    private const string Releases = "KSAModding/content-index-releases";
    private const string Token = "ghu_secret";

    private readonly InstantTimeProvider _time = new();
    private readonly List<Sent> _sent = [];
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.Ordinal);
    private string _login = "octocat";
    private Task? _holdFirst;
    private readonly TaskCompletionSource _firstArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public GitHubStewardRoleTests()
    {
        On("POST", "https://github.com/login/device/code", () => Json("""{"device_code":"d","user_code":"WDJB-MJHT","verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}"""));
        On("POST", "https://github.com/login/oauth/access_token", () => Json($$"""{"access_token":"{{Token}}","token_type":"bearer"}"""));
        On("GET", Api + "/user", () => Json($$"""{"login":"{{_login}}","id":1}"""));
    }

    [Theory]
    [InlineData("always", true)]
    [InlineData("pull_requests_only", true)]
    [InlineData("never", false)]
    [InlineData(null, false)]
    public async Task CheckAsync_ReadsTheBypassOfTheMainRuleset(string? bypass, bool steward)
    {
        OnMainRulesets(bypass);
        var (role, _) = await SignedInAsync();

        var access = await role.CheckAsync();

        Assert.Equal(new StewardAccess("octocat", steward, steward), access);
        Assert.Equal(access, role.Current);
        Assert.Equal(steward, access!.IsSteward);
        Assert.Equal(
            [
                $"GET {Api}/repos/{Index}/rulesets?per_page=100&page=1",
                $"GET {Api}/repos/{Index}/rulesets/1",
                $"GET {Api}/repos/{Releases}/rulesets?per_page=100&page=1",
                $"GET {Api}/repos/{Releases}/rulesets/2",
            ],
            _sent.Select(sent => sent.Line));
        Assert.All(_sent, sent => Assert.Equal("Bearer " + Token, sent.Authorization));
    }

    [Fact]
    public async Task CheckAsync_StewardInOneRepositoryOnly()
    {
        OnRulesets(Index, Ruleset(1, "~DEFAULT_BRANCH", "never"));
        OnRulesets(Releases, Ruleset(2, "~DEFAULT_BRANCH", "always"));
        var (role, _) = await SignedInAsync();

        var access = await role.CheckAsync();

        Assert.Equal(new StewardAccess("octocat", ContentIndex: false, ContentIndexReleases: true), access);
        Assert.True(access!.IsSteward);
    }

    [Fact]
    public async Task CheckAsync_RulesetWithoutTheDefaultBranch_IsNoSteward()
    {
        OnRulesets(Index, Ruleset(1, "refs/heads/automation-test", "always"));
        OnRulesets(Releases, Ruleset(2, "refs/heads/main", "always"));
        var (role, _) = await SignedInAsync();

        Assert.Equal(new StewardAccess("octocat", false, false), await role.CheckAsync());
    }

    [Fact]
    public async Task CheckAsync_TakesOnlyTheActiveBranchRulesetsOfTheDefaultBranch_AndNeedsTheBypassOfEach()
    {
        OnRulesets(
            Index,
            Ruleset(1, "refs/heads/automation-test", "never"),
            Ruleset(2, "~DEFAULT_BRANCH", "always"),
            Ruleset(3, "~DEFAULT_BRANCH", "never", enforcement: "disabled"),
            Ruleset(4, "~DEFAULT_BRANCH", "never", enforcement: "evaluate"),
            Ruleset(5, "~DEFAULT_BRANCH", "never", target: "tag"));
        OnRulesets(Releases, Ruleset(6, "~DEFAULT_BRANCH", "always"), Ruleset(7, "~DEFAULT_BRANCH", "never"));
        var (role, _) = await SignedInAsync();

        Assert.Equal(new StewardAccess("octocat", ContentIndex: true, ContentIndexReleases: false), await role.CheckAsync());
        Assert.DoesNotContain(_sent, sent => sent.Url == $"{Api}/repos/{Index}/rulesets/5");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.Forbidden, true)]
    [InlineData(HttpStatusCode.NotFound, true)]
    public async Task CheckAsync_RefusedRead_IsNoSteward(HttpStatusCode status, bool onTheRuleset)
    {
        OnMainRulesets("always");
        var refused = onTheRuleset ? $"{Api}/repos/{Index}/rulesets/1" : $"{Api}/repos/{Index}/rulesets?per_page=100&page=1";
        On("GET", refused, () => Json("""{"message":"Resource not accessible by integration"}""", status));
        var (role, session) = await SignedInAsync();

        var access = await role.CheckAsync();

        Assert.Equal(new StewardAccess("octocat", ContentIndex: false, ContentIndexReleases: true), access);
        Assert.Equal(GitHubSessionStatus.SignedIn, session.State.Status);
    }

    [Fact]
    public async Task CheckAsync_NetworkError_IsNoSteward()
    {
        OnMainRulesets("always");
        On("GET", $"{Api}/repos/{Releases}/rulesets/2", () => throw new HttpRequestException("offline"));
        var (role, _) = await SignedInAsync();

        Assert.Equal(new StewardAccess("octocat", ContentIndex: true, ContentIndexReleases: false), await role.CheckAsync());
    }

    [Fact]
    public async Task CheckAsync_Unauthorized_SignsOutAndLeavesNoRole()
    {
        OnRulesets(Index, Ruleset(1, "~DEFAULT_BRANCH", "always"));
        On("GET", $"{Api}/repos/{Index}/rulesets/1", () => Json("""{"message":"Bad credentials"}""", HttpStatusCode.Unauthorized));
        var (role, session) = await SignedInAsync();

        Assert.Null(await role.CheckAsync());

        Assert.Equal(GitHubSessionStatus.SignedOut, session.State.Status);
        Assert.Null(role.Current);
        Assert.DoesNotContain(_sent, sent => sent.Url.Contains(Releases, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CheckAsync_SignedOut_SendsNothing()
    {
        var session = new GitHubSession(Http(), "Iv1.testclient", "borea-test", _time);
        using var role = new GitHubStewardRole(session, Http(), _time);

        Assert.Null(await role.CheckAsync());
        Assert.Null(role.Current);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task CheckAsync_CallsDuringACheck_ShareIt()
    {
        var held = new TaskCompletionSource();
        _holdFirst = held.Task;
        OnMainRulesets("always");
        var (role, _) = await SignedInAsync();

        var first = role.CheckAsync();
        await _firstArrived.Task;
        var second = role.CheckAsync();
        held.SetResult();

        Assert.Equal(await first, await second);
        Assert.Equal(4, _sent.Count);
    }

    [Fact]
    public async Task SignOut_DropsTheRoleAndSaysSo()
    {
        OnMainRulesets("always");
        var (role, session) = await SignedInAsync();
        await role.CheckAsync();
        var changes = 0;
        role.Changed += (_, _) => changes++;

        session.SignOut();

        Assert.Null(role.Current);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task SignOutDuringACheck_DropsItsResult()
    {
        var held = new TaskCompletionSource();
        _holdFirst = held.Task;
        OnMainRulesets("always");
        var (role, session) = await SignedInAsync();

        var check = role.CheckAsync();
        await _firstArrived.Task;
        session.SignOut();
        held.SetResult();

        Assert.Null(await check);
        Assert.Null(role.Current);
    }

    [Fact]
    public async Task CheckOfTheAccountBefore_EndingLate_KeepsTheResultOfTheNewAccount()
    {
        var held = new TaskCompletionSource();
        _holdFirst = held.Task;
        OnMainRulesets("always");
        var (role, session) = await SignedInAsync();
        var stale = role.CheckAsync();
        await _firstArrived.Task;

        session.SignOut();
        _login = "alice";
        Assert.True((await session.SignInAsync()).SignedIn);
        Assert.Equal(new StewardAccess("alice", true, true), await role.CheckAsync());
        held.SetResult();

        Assert.Null(await stale);
        Assert.Equal(new StewardAccess("alice", true, true), role.Current);
    }

    [Fact]
    public async Task SecondSignInAsAnotherAccount_ChecksAgain()
    {
        OnMainRulesets("always");
        var (role, session) = await SignedInAsync();
        Assert.Equal(new StewardAccess("octocat", true, true), await role.CheckAsync());

        session.SignOut();
        _login = "alice";
        OnMainRulesets("never");
        Assert.True((await session.SignInAsync()).SignedIn);
        _sent.Clear();

        Assert.Null(role.Current);
        Assert.Equal(new StewardAccess("alice", false, false), await role.CheckAsync());
        Assert.Equal(4, _sent.Count);
        Assert.Equal(new StewardAccess("alice", false, false), role.Current);
    }

    [Fact]
    public async Task Dispose_StopsFollowingTheSession()
    {
        OnMainRulesets("always");
        var session = new CountingSession(new GitHubSession(Http(), "Iv1.testclient", "borea-test", _time));
        Assert.True((await session.SignInAsync()).SignedIn);
        var role = new GitHubStewardRole(session, Http(), _time);
        await role.CheckAsync();
        var changes = 0;
        role.Changed += (_, _) => changes++;
        Assert.Equal(1, session.Subscribers);

        role.Dispose();
        session.SignOut();

        Assert.Equal(0, session.Subscribers);
        Assert.Null(role.Current);
        Assert.Equal(0, changes);
    }

    private async Task<(GitHubStewardRole Role, GitHubSession Session)> SignedInAsync()
    {
        var session = new GitHubSession(Http(), "Iv1.testclient", "borea-test", _time);
        Assert.True((await session.SignInAsync()).SignedIn);
        _sent.Clear();
        return (new GitHubStewardRole(session, Http(), _time), session);
    }

    /// <summary>Holds the first ruleset read until <see cref="_holdFirst"/> ends. The role sends its reads through the session, so the session's client is the one that holds them.</summary>
    private HttpClient Http() => new(new FakeHttpMessageHandler(async request =>
    {
        if (request.RequestUri!.AbsolutePath.Contains("/rulesets", StringComparison.Ordinal) && Interlocked.Exchange(ref _holdFirst, null) is { } first)
        {
            _firstArrived.SetResult();
            await first;
        }

        return Respond(request);
    }));

    private void On(string method, string url, Func<HttpResponseMessage> answer) => _routes[method + " " + url] = answer;

    private void OnRulesets(string repository, params Dictionary<string, object?>[] rulesets)
    {
        var listed = rulesets.Select(ruleset => ruleset.Where(pair => pair.Key is "id" or "target" or "enforcement").ToDictionary());
        On("GET", $"{Api}/repos/{repository}/rulesets?per_page=100&page=1", () => Json(JsonSerializer.Serialize(listed)));
        foreach (var ruleset in rulesets)
        {
            var body = JsonSerializer.Serialize(ruleset.Where(pair => pair.Value is not null).ToDictionary());
            On("GET", $"{Api}/repos/{repository}/rulesets/{ruleset["id"]}", () => Json(body));
        }
    }

    private void OnMainRulesets(string? bypass)
    {
        OnRulesets(Index, Ruleset(1, "~DEFAULT_BRANCH", bypass));
        OnRulesets(Releases, Ruleset(2, "~DEFAULT_BRANCH", bypass));
    }

    private static Dictionary<string, object?> Ruleset(long id, string include, string? bypass, string enforcement = "active", string target = "branch") => new()
    {
        ["id"] = id,
        ["name"] = "main branch protection",
        ["target"] = target,
        ["enforcement"] = enforcement,
        ["conditions"] = new { ref_name = new { exclude = Array.Empty<string>(), include = new[] { include } } },
        ["current_user_can_bypass"] = bypass,
    };

    private HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var url = request.RequestUri!.AbsoluteUri;
        lock (_sent)
            _sent.Add(new Sent(request.Method.Method, url, request.Headers.Authorization?.ToString()));

        return _routes.TryGetValue(request.Method.Method + " " + url, out var answer)
            ? answer()
            : Json("""{"message":"Not Found"}""", HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed record Sent(string Method, string Url, string? Authorization)
    {
        public string Line => Method + " " + Url;
    }

    /// <summary>Counts who follows <see cref="IGitHubSession.StateChanged"/>.</summary>
    private sealed class CountingSession(IGitHubSession inner) : IGitHubSession
    {
        public int Subscribers { get; private set; }

        public bool IsAvailable => inner.IsAvailable;

        public string ManageAccessUrl => inner.ManageAccessUrl;

        public string InstallUrl => inner.InstallUrl;

        public GitHubSessionState State => inner.State;

        public event EventHandler? StateChanged
        {
            add
            {
                Subscribers++;
                inner.StateChanged += value;
            }

            remove
            {
                Subscribers--;
                inner.StateChanged -= value;
            }
        }

        public string InstallUrlFor(long repositoryId) => inner.InstallUrlFor(repositoryId);

        public bool KeepSignedIn
        {
            get => inner.KeepSignedIn;
            set => inner.KeepSignedIn = value;
        }

        public SecretStoreProblem? KeepSignedInProblem => inner.KeepSignedInProblem;

        public event EventHandler? KeepSignedInProblemChanged
        {
            add => inner.KeepSignedInProblemChanged += value;
            remove => inner.KeepSignedInProblemChanged -= value;
        }

        public Task<GitHubResumeOutcome> ResumeAsync(CancellationToken cancellationToken = default) => inner.ResumeAsync(cancellationToken);

        public Task<GitHubSignInResult> SignInAsync(IProgress<GitHubDeviceCode>? progress = null, bool keepSignedIn = true, CancellationToken cancellationToken = default) =>
            inner.SignInAsync(progress, keepSignedIn, cancellationToken);

        public void SignOut() => inner.SignOut();

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default) =>
            inner.SendAsync(request, cancellationToken);
    }

    /// <summary>Fires every delay at once, so the sign-in does not wait for its poll interval.</summary>
    private sealed class InstantTimeProvider : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ThreadPool.QueueUserWorkItem(_ => callback(state));
            return TimeProvider.System.CreateTimer(_ => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }
}
