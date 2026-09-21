using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Borea.Core.GitHub;
using Borea.Network.GitHub;

namespace Borea.Network.Tests;

public sealed class GitHubSessionTests
{
    private const string ClientId = "Iv23.testclient";
    private const string Slug = "borea-test";
    private const string Token = "ghu_secret";
    private const string DeviceCodeJson = """{"device_code":"3584d83530557fdd1f46af8289938c8ef79f9dc5","user_code":"WDJB-MJHT","verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}""";
    private const string TokenJson = """{"access_token":"ghu_secret","expires_in":28800,"refresh_token":"ghr_secret","refresh_token_expires_in":15897600,"token_type":"bearer","scope":""}""";
    private const string PendingJson = """{"error":"authorization_pending","error_description":"The authorization request is still pending.","error_uri":"https://docs.github.com"}""";
    private const string UserJson = """{"login":"octocat","id":1}""";

    private readonly StepTimeProvider _time = new();
    private readonly ConcurrentQueue<SentRequest> _sent = new();
    private readonly Queue<Func<HttpResponseMessage>> _tokenAnswers = new();

    private string _deviceCodeJson = DeviceCodeJson;
    private Func<HttpRequestMessage, HttpResponseMessage>? _otherAnswer;

    [Fact]
    public async Task SignInAsync_Confirmed_SignsInAndReportsTheCode()
    {
        _tokenAnswers.Enqueue(() => Json(TokenJson));
        var session = Session();
        var codes = new List<GitHubDeviceCode>();
        var states = new List<GitHubSessionStatus>();
        session.StateChanged += (_, _) => states.Add(session.State.Status);

        var result = await session.SignInAsync(new ListProgress<GitHubDeviceCode>(codes));

        Assert.Equal(new GitHubSignInResult(GitHubSignInOutcome.SignedIn, "octocat"), result);
        Assert.Equal(GitHubSessionStatus.SignedIn, session.State.Status);
        Assert.Equal("octocat", session.State.Login);
        Assert.Equal([new GitHubDeviceCode("WDJB-MJHT", "https://github.com/login/device")], codes);
        Assert.Equal([GitHubSessionStatus.WaitingForCode, GitHubSessionStatus.SignedIn], states);
        Assert.Equal([TimeSpan.FromSeconds(5)], _time.Delays);

        var sent = _sent.ToArray();
        Assert.Equal(["https://github.com/login/device/code", "https://github.com/login/oauth/access_token", "https://api.github.com/user"], sent.Select(request => request.Url));
        Assert.Equal("client_id=Iv23.testclient", sent[0].Body);
        Assert.Equal("client_id=Iv23.testclient&device_code=3584d83530557fdd1f46af8289938c8ef79f9dc5&grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code", sent[1].Body);
        Assert.All(sent.Take(2), request => Assert.Equal("application/json", request.Accept));
        Assert.All(sent.Take(2), request => Assert.Null(request.Authorization));
        Assert.Equal("Bearer " + Token, sent[2].Authorization);
    }

    [Fact]
    public async Task SignInAsync_PendingThenConfirmed_PollsOncePerInterval()
    {
        _tokenAnswers.Enqueue(() => Json(PendingJson));
        _tokenAnswers.Enqueue(() => Json(PendingJson));
        _tokenAnswers.Enqueue(() => Json(TokenJson));

        var result = await Session().SignInAsync();

        Assert.True(result.SignedIn);
        Assert.Equal(3, _sent.Count(request => request.Url == GitHubSession.AccessTokenUrl));
        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)], _time.Delays);
    }

    [Theory]
    [InlineData("""{"error":"slow_down","interval":10}""", 10)]
    [InlineData("""{"error":"slow_down"}""", 10)]
    [InlineData("""{"error":"slow_down","interval":15}""", 15)]
    public async Task SignInAsync_SlowDown_RaisesTheInterval(string slowDown, int seconds)
    {
        _tokenAnswers.Enqueue(() => Json(slowDown));
        _tokenAnswers.Enqueue(() => Json(PendingJson));
        _tokenAnswers.Enqueue(() => Json(TokenJson));

        var result = await Session().SignInAsync();

        Assert.True(result.SignedIn);
        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(seconds)], _time.Delays);
    }

    [Theory]
    [InlineData("expired_token", GitHubSignInOutcome.Expired)]
    [InlineData("access_denied", GitHubSignInOutcome.AccessDenied)]
    [InlineData("device_flow_disabled", GitHubSignInOutcome.DeviceFlowDisabled)]
    [InlineData("incorrect_client_credentials", GitHubSignInOutcome.IncorrectClientCredentials)]
    [InlineData("incorrect_device_code", GitHubSignInOutcome.IncorrectDeviceCode)]
    [InlineData("unsupported_grant_type", GitHubSignInOutcome.UnsupportedGrantType)]
    [InlineData("something_new", GitHubSignInOutcome.UnexpectedResponse)]
    public async Task SignInAsync_RefusedPoll_EndsSignedOut(string error, GitHubSignInOutcome outcome)
    {
        _tokenAnswers.Enqueue(() => Json($$"""{"error":"{{error}}","error_description":"Refused."}"""));
        var session = Session();

        var result = await session.SignInAsync();

        Assert.Equal(new GitHubSignInResult(outcome), result);
        Assert.Equal(GitHubSessionState.SignedOut, session.State);
        Assert.DoesNotContain(_sent, request => request.Url == GitHubSession.UserUrl);
    }

    [Theory]
    [InlineData("device_flow_disabled", GitHubSignInOutcome.DeviceFlowDisabled)]
    [InlineData("incorrect_client_credentials", GitHubSignInOutcome.IncorrectClientCredentials)]
    public async Task SignInAsync_RefusedDeviceCode_NeverPolls(string error, GitHubSignInOutcome outcome)
    {
        _deviceCodeJson = $$"""{"error":"{{error}}","error_description":"Refused."}""";
        var session = Session();

        var result = await session.SignInAsync();

        Assert.Equal(outcome, result.Outcome);
        Assert.Single(_sent);
        Assert.Equal(GitHubSessionStatus.SignedOut, session.State.Status);
    }

    [Theory]
    [InlineData("""{"device_code":"abc","user_code":"WDJB-MJHT","verification_uri":"https://example.com/login/device","expires_in":900,"interval":5}""")]
    [InlineData("""{"device_code":"abc","user_code":"WDJB-MJHT","verification_uri":"http://github.com/login/device","expires_in":900,"interval":5}""")]
    [InlineData("""{"device_code":"","user_code":"WDJB-MJHT","verification_uri":"https://github.com/login/device"}""")]
    [InlineData("<html>Not Found</html>")]
    public async Task SignInAsync_UnusableDeviceCode_IsUnexpected(string json)
    {
        _deviceCodeJson = json;

        var result = await Session().SignInAsync();

        Assert.Equal(GitHubSignInOutcome.UnexpectedResponse, result.Outcome);
        Assert.Single(_sent);
    }

    [Fact]
    public async Task SignInAsync_CodeOutlivesItsLifetime_Expires()
    {
        _deviceCodeJson = """{"device_code":"abc","user_code":"WDJB-MJHT","verification_uri":"https://github.com/login/device","expires_in":10,"interval":5}""";
        _tokenAnswers.Enqueue(() => Json(PendingJson));

        var result = await Session().SignInAsync();

        Assert.Equal(GitHubSignInOutcome.Expired, result.Outcome);
        Assert.Equal(1, _sent.Count(request => request.Url == GitHubSession.AccessTokenUrl));
    }

    [Fact]
    public async Task SignInAsync_OneFailedPoll_KeepsPolling()
    {
        _tokenAnswers.Enqueue(() => throw new HttpRequestException("No route to host."));
        _tokenAnswers.Enqueue(() => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>Bad Gateway</html>") });
        _tokenAnswers.Enqueue(() => Json(TokenJson));

        var result = await Session().SignInAsync();

        Assert.True(result.SignedIn);
        Assert.Equal(3, _sent.Count(request => request.Url == GitHubSession.AccessTokenUrl));
    }

    [Fact]
    public async Task SignInAsync_FailedPollsInSequence_IsANetworkError()
    {
        for (var i = 0; i < 3; i++)
            _tokenAnswers.Enqueue(() => throw new HttpRequestException("No route to host."));
        var session = Session();

        var result = await session.SignInAsync();

        Assert.Equal(GitHubSignInOutcome.NetworkError, result.Outcome);
        Assert.Equal(GitHubSessionStatus.SignedOut, session.State.Status);
    }

    [Fact]
    public async Task SignInAsync_GarbledPollsInSequence_AreUnexpected()
    {
        for (var i = 0; i < 3; i++)
            _tokenAnswers.Enqueue(() => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("Too many requests") });

        var result = await Session().SignInAsync();

        Assert.Equal(GitHubSignInOutcome.UnexpectedResponse, result.Outcome);
    }

    [Fact]
    public async Task SignInAsync_Cancelled_StopsPolling()
    {
        using var cancel = new CancellationTokenSource();
        _tokenAnswers.Enqueue(() => Json(PendingJson));
        _tokenAnswers.Enqueue(() =>
        {
            cancel.Cancel();
            return Json(PendingJson);
        });
        var session = Session();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.SignInAsync(cancellationToken: cancel.Token));

        Assert.Equal(2, _sent.Count(request => request.Url == GitHubSession.AccessTokenUrl));
        Assert.Equal(GitHubSessionStatus.SignedOut, session.State.Status);
    }

    [Fact]
    public async Task SignOut_WhileWaiting_StopsTheSignIn()
    {
        GitHubSession session = null!;
        _tokenAnswers.Enqueue(() =>
        {
            session.SignOut();
            return Json(PendingJson);
        });
        session = Session();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.SignInAsync());

        Assert.Equal(1, _sent.Count(request => request.Url == GitHubSession.AccessTokenUrl));
        Assert.Equal(GitHubSessionStatus.SignedOut, session.State.Status);
    }

    [Fact]
    public async Task SignOut_WhileTheUserIsFetched_StaysSignedOut()
    {
        GitHubSession session = null!;
        _tokenAnswers.Enqueue(() => Json(TokenJson));
        _otherAnswer = _ =>
        {
            session.SignOut();
            return Json(UserJson);
        };
        session = Session();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.SignInAsync());

        Assert.Equal(GitHubSessionState.SignedOut, session.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user")));
    }

    [Fact]
    public async Task SignInAsync_WhenSignedIn_ThrowsAndKeepsTheSession()
    {
        var session = await SignedInSessionAsync();
        var sentBefore = _sent.Count;

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SignInAsync());

        Assert.Equal(sentBefore, _sent.Count);
        Assert.Equal(GitHubSessionState.SignedInAs("octocat"), session.State);
    }

    [Fact]
    public async Task SignInAsync_WhileAnotherRuns_Throws()
    {
        var release = new TaskCompletionSource();
        _tokenAnswers.Enqueue(() =>
        {
            release.Task.Wait();
            return Json(TokenJson);
        });
        var session = Session();
        var first = session.SignInAsync();
        await WaitUntilAsync(() => session.State.Status == GitHubSessionStatus.WaitingForCode);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SignInAsync());

        release.SetResult();
        Assert.True((await first).SignedIn);
    }

    [Fact]
    public async Task SignInAsync_UserRequestFails_IsUnexpected()
    {
        _tokenAnswers.Enqueue(() => Json(TokenJson));
        _otherAnswer = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var session = Session();

        var result = await session.SignInAsync();

        Assert.Equal(GitHubSignInOutcome.UnexpectedResponse, result.Outcome);
        Assert.Equal(GitHubSessionStatus.SignedOut, session.State.Status);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(ClientId, "")]
    [InlineData("", Slug)]
    public async Task SignInAsync_WithoutClientIdOrSlug_Throws(string clientId, string slug)
    {
        var session = new GitHubSession(new HttpClient(new FakeHttpMessageHandler(_ => throw new InvalidOperationException("No request expected."))), clientId, slug);

        Assert.False(session.IsAvailable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SignInAsync());
    }

    [Fact]
    public void IsAvailable_WithClientIdAndSlug_IsTrue()
    {
        Assert.True(Session().IsAvailable);
    }

    [Fact]
    public void InstallUrl_NamesTheApp()
    {
        Assert.Equal("https://github.com/apps/borea-test/installations/new", Session().InstallUrl);
    }

    [Fact]
    public async Task InstallUrl_SignedIn_SuggestsTheUsersOwnAccount()
    {
        _tokenAnswers.Enqueue(() => Json(TokenJson));
        var session = Session();

        await session.SignInAsync();

        Assert.Equal("https://github.com/apps/borea-test/installations/new/permissions?suggested_target_id=1", session.InstallUrl);

        session.SignOut();

        Assert.Equal("https://github.com/apps/borea-test/installations/new", session.InstallUrl);
    }

    [Fact]
    public async Task InstallUrlFor_SignedIn_SelectsTheRepository()
    {
        _tokenAnswers.Enqueue(() => Json(TokenJson));
        var session = Session();

        Assert.Equal("https://github.com/apps/borea-test/installations/new", session.InstallUrlFor(42));

        await session.SignInAsync();

        Assert.Equal("https://github.com/apps/borea-test/installations/new/permissions?suggested_target_id=1&repository_ids[]=42", session.InstallUrlFor(42));
    }

    [Fact]
    public void ManageAccessUrl_IsTheAuthorizedAppsPage()
    {
        Assert.Equal("https://github.com/settings/apps/authorizations", Session().ManageAccessUrl);
    }

    [Fact]
    public async Task SendAsync_ApiRequest_CarriesTheToken()
    {
        var session = await SignedInSessionAsync();
        _otherAnswer = _ => Json("{}");

        using var response = await session.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/KSAModding/content-index"));

        var sent = _sent.Last();
        Assert.Equal("https://api.github.com/repos/KSAModding/content-index", sent.Url);
        Assert.Equal("Bearer " + Token, sent.Authorization);
        Assert.Equal("application/vnd.github+json", sent.Accept);
        Assert.Equal(BoreaReleaseCheck.ApiVersion, sent.ApiVersion);
    }

    [Theory]
    [InlineData("https://github.com/KSAModding/content-index")]
    [InlineData("https://raw.githubusercontent.com/KSAModding/content-index/main/README.md")]
    [InlineData("http://api.github.com/user")]
    [InlineData("https://api.github.com.example.com/user")]
    [InlineData("https://api.github.com:8443/user")]
    [InlineData("https://user@api.github.com/user")]
    public async Task SendAsync_OtherHost_IsRefusedWithoutSending(string url)
    {
        var session = await SignedInSessionAsync();
        var sentBefore = _sent.Count;

        await Assert.ThrowsAsync<ArgumentException>(() => session.SendAsync(new HttpRequestMessage(HttpMethod.Get, url)));

        Assert.Equal(sentBefore, _sent.Count);
        Assert.All(_sent.Where(request => !request.Url.StartsWith("https://api.github.com/", StringComparison.Ordinal)), request => Assert.Null(request.Authorization));
    }

    [Fact]
    public async Task SendAsync_Unauthorized_SignsOut()
    {
        var session = await SignedInSessionAsync();
        var changes = 0;
        session.StateChanged += (_, _) => changes++;
        _otherAnswer = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        using var response = await session.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/repos"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(GitHubSessionState.SignedOut, session.State);
        Assert.Equal(1, changes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user")));
    }

    [Fact]
    public async Task SendAsync_UnauthorizedAfterARedirectDroppedTheToken_StaysSignedIn()
    {
        var session = await SignedInSessionAsync();
        _otherAnswer = request =>
        {
            request.Headers.Authorization = null;
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        };

        using var response = await session.SendAsync(new HttpRequestMessage(HttpMethod.Put, "https://api.github.com/repos/old-name/content-index/contents/listings/a.toml"));

        Assert.Equal(GitHubSessionStatus.SignedIn, session.State.Status);
    }

    [Fact]
    public async Task SendAsync_Forbidden_StaysSignedIn()
    {
        var session = await SignedInSessionAsync();
        _otherAnswer = _ => new HttpResponseMessage(HttpStatusCode.Forbidden);

        using var response = await session.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/repos"));

        Assert.Equal(GitHubSessionStatus.SignedIn, session.State.Status);
    }

    [Fact]
    public async Task SignOut_ForgetsTheToken()
    {
        var session = await SignedInSessionAsync();

        session.SignOut();

        Assert.Equal(GitHubSessionState.SignedOut, session.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user")));
    }

    private async Task<GitHubSession> SignedInSessionAsync()
    {
        _tokenAnswers.Enqueue(() => Json(TokenJson));
        var session = Session();
        Assert.True((await session.SignInAsync()).SignedIn);
        return session;
    }

    private GitHubSession Session() => new(new HttpClient(new FakeHttpMessageHandler(Respond)), ClientId, Slug, _time);

    private async Task<HttpResponseMessage> Respond(HttpRequestMessage request)
    {
        _sent.Enqueue(new SentRequest(
            request.RequestUri!.AbsoluteUri,
            request.Content is null ? null : await request.Content.ReadAsStringAsync(),
            request.Headers.Authorization?.ToString(),
            request.Headers.Accept.SingleOrDefault()?.MediaType,
            request.Headers.TryGetValues("X-GitHub-Api-Version", out var versions) ? versions.Single() : null));

        switch (request.RequestUri.AbsoluteUri)
        {
            case GitHubSession.DeviceCodeUrl:
                return Json(_deviceCodeJson);
            case GitHubSession.AccessTokenUrl:
                return _tokenAnswers.Count > 0 ? _tokenAnswers.Dequeue()() : Json(PendingJson);
            case GitHubSession.UserUrl when _otherAnswer is null:
                return Json(UserJson);
            default:
                return _otherAnswer?.Invoke(request) ?? throw new InvalidOperationException($"No answer for {request.RequestUri}.");
        }
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not hold in time.");
            await Task.Delay(10);
        }
    }

    private sealed record SentRequest(string Url, string? Body, string? Authorization, string? Accept, string? ApiVersion);

    private sealed class ListProgress<T>(List<T> reports) : IProgress<T>
    {
        public void Report(T value) => reports.Add(value);
    }

    /// <summary>
    /// A clock whose timers fire at once and move the clock by their due time,
    /// so a polling loop runs without waiting and the test sees each delay.
    /// </summary>
    private sealed class StepTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<TimeSpan> _delays = [];
        private DateTimeOffset _now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

        public IReadOnlyList<TimeSpan> Delays
        {
            get
            {
                lock (_gate)
                    return _delays.ToArray();
            }
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
                return _now;
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (_gate)
            {
                _delays.Add(dueTime);
                _now += dueTime;
            }

            ThreadPool.QueueUserWorkItem(_ => callback(state));
            return new FiredTimer();
        }

        private sealed class FiredTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => false;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
