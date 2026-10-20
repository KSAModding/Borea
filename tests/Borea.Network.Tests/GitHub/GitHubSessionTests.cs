using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Borea.Core.GitHub;
using Borea.Core.Logging;
using Borea.Core.Secrets;
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
    private const string RefreshedJson = """{"access_token":"ghu_refreshed","expires_in":28800,"refresh_token":"ghr_refreshed","refresh_token_expires_in":15897600,"token_type":"bearer","scope":""}""";
    private const string Kept = GitHubSession.RefreshTokenSecret;

    private readonly StepTimeProvider _time = new();
    private readonly ConcurrentQueue<SentRequest> _sent = new();
    private readonly Queue<Func<HttpResponseMessage>> _tokenAnswers = new();

    /// <summary>Holds back the answer to a token request, so that a test can keep a sign-in running.</summary>
    private Task _tokenGate = Task.CompletedTask;

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
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _tokenGate = release.Task;
        _tokenAnswers.Enqueue(() => Json(TokenJson));
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

    [Fact]
    public async Task SendAsync_AssetUpload_CarriesTheToken()
    {
        var session = await SignedInSessionAsync();
        _otherAnswer = _ => Json("{}");

        using var response = await session.SendAsync(new HttpRequestMessage(HttpMethod.Post, "https://uploads.github.com/repos/octocat/ksa-shares/releases/1/assets?name=a.zip"));

        Assert.Equal("Bearer " + Token, _sent.Last().Authorization);
    }

    [Theory]
    [InlineData("https://github.com/KSAModding/content-index")]
    [InlineData("https://uploads.github.com.example.com/repos")]
    [InlineData("http://uploads.github.com/repos")]
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

    [Fact]
    public async Task SignInAsync_KeepSignedIn_KeepsTheRefreshTokenAndTheNextStartSignsInWithoutTheCode()
    {
        var secrets = new FakeSecretStore();
        _tokenAnswers.Enqueue(() => Json(TokenJson));
        var first = Session(secrets, keep: true);

        Assert.True((await first.SignInAsync()).SignedIn);
        await first.WhenSecretsStoredAsync();

        Assert.Equal("ghr_secret", secrets[Kept]);
        _sent.Clear();
        _tokenAnswers.Enqueue(() => Json(RefreshedJson));
        var next = Session(secrets, keep: true);
        var states = new List<GitHubSessionStatus>();
        next.StateChanged += (_, _) => states.Add(next.State.Status);

        var outcome = await next.ResumeAsync();

        Assert.Equal(GitHubResumeOutcome.SignedIn, outcome);
        Assert.Equal(GitHubSessionState.SignedInAs("octocat"), next.State);
        Assert.Equal([GitHubSessionStatus.Resuming, GitHubSessionStatus.SignedIn], states);
        var sent = _sent.ToArray();
        Assert.Equal([GitHubSession.AccessTokenUrl, GitHubSession.UserUrl], sent.Select(request => request.Url));
        Assert.Equal("client_id=Iv23.testclient&grant_type=refresh_token&refresh_token=ghr_secret", sent[0].Body);
        Assert.Null(sent[0].Authorization);
        Assert.Equal("Bearer ghu_refreshed", sent[1].Authorization);

        _otherAnswer = _ => Json("{}");
        using var response = await next.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/repos"));
        Assert.Equal("Bearer ghu_refreshed", _sent.Last().Authorization);
    }

    [Fact]
    public async Task ResumeAsync_Refreshed_ReplacesTheKeptToken()
    {
        var secrets = new FakeSecretStore { [Kept] = "ghr_secret" };
        _tokenAnswers.Enqueue(() => Json(RefreshedJson));
        var session = Session(secrets, keep: true);

        await session.ResumeAsync();
        await session.WhenSecretsStoredAsync();

        Assert.Equal("ghr_refreshed", secrets[Kept]);
    }

    [Fact]
    public async Task ResumeAsync_AccountUnreachableAfterTheRefresh_KeepsTheNewTokenForTheNextStart()
    {
        var secrets = new FakeSecretStore { [Kept] = "ghr_secret" };
        _tokenAnswers.Enqueue(() => Json(RefreshedJson));
        _otherAnswer = _ => throw new HttpRequestException("No route to host.");
        var session = Session(secrets, keep: true);

        var outcome = await session.ResumeAsync();
        await session.WhenSecretsStoredAsync();

        Assert.Equal(GitHubResumeOutcome.Unreachable, outcome);
        Assert.Equal(GitHubSessionState.SignedOut, session.State);
        Assert.Equal("ghr_refreshed", secrets[Kept]);
    }

    [Theory]
    [InlineData("bad_refresh_token")]
    [InlineData("unauthorized")]
    public async Task ResumeAsync_Refused_DeletesTheKeptTokenAndEndsSignedOut(string error)
    {
        var secrets = new FakeSecretStore { [Kept] = "ghr_secret" };
        _tokenAnswers.Enqueue(() => Json($$"""{"error":"{{error}}","error_description":"The refresh token passed is incorrect or expired."}"""));
        var session = Session(secrets, keep: true);
        var states = new List<GitHubSessionStatus>();
        session.StateChanged += (_, _) => states.Add(session.State.Status);

        var outcome = await session.ResumeAsync();
        await session.WhenSecretsStoredAsync();

        Assert.Equal(GitHubResumeOutcome.Refused, outcome);
        Assert.Equal(GitHubSessionState.SignedOut, session.State);
        Assert.Equal([GitHubSessionStatus.Resuming, GitHubSessionStatus.SignedOut], states);
        Assert.Null(secrets[Kept]);
        Assert.DoesNotContain(_sent, request => request.Url == GitHubSession.UserUrl);
        Assert.Null(session.KeepSignedInProblem);
    }

    [Fact]
    public async Task ResumeAsync_AccountRefusesTheNewToken_DeletesTheKeptToken()
    {
        var secrets = new FakeSecretStore { [Kept] = "ghr_secret" };
        _tokenAnswers.Enqueue(() => Json(RefreshedJson));
        _otherAnswer = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        var session = Session(secrets, keep: true);

        var outcome = await session.ResumeAsync();
        await session.WhenSecretsStoredAsync();

        Assert.Equal(GitHubResumeOutcome.Refused, outcome);
        Assert.Equal(GitHubSessionState.SignedOut, session.State);
        Assert.Null(secrets[Kept]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResumeAsync_GitHubUnreachable_KeepsTheTokenAndEndsSignedOut(bool noRoute)
    {
        var secrets = new FakeSecretStore { [Kept] = "ghr_secret" };
        _tokenAnswers.Enqueue(() => noRoute
            ? throw new HttpRequestException("No route to host.")
            : new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>Bad Gateway</html>") });
        var session = Session(secrets, keep: true);

        var outcome = await session.ResumeAsync();
        await session.WhenSecretsStoredAsync();

        Assert.Equal(GitHubResumeOutcome.Unreachable, outcome);
        Assert.Equal(GitHubSessionState.SignedOut, session.State);
        Assert.Equal("ghr_secret", secrets[Kept]);
        Assert.Equal(["read"], secrets.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ResumeAsync_BusyGitHubAnswersWithAnError_KeepsTheToken(HttpStatusCode status)
    {
        var secrets = new FakeSecretStore { [Kept] = "ghr_secret" };
        _tokenAnswers.Enqueue(() => new HttpResponseMessage(status)
        {
            Content = new StringContent("""{"error":"temporarily_unavailable"}""", Encoding.UTF8, "application/json"),
        });
        var session = Session(secrets, keep: true);

        var outcome = await session.ResumeAsync();
        await session.WhenSecretsStoredAsync();

        Assert.Equal(GitHubResumeOutcome.Unreachable, outcome);
        Assert.Equal("ghr_secret", secrets[Kept]);
        Assert.Equal(["read"], secrets.Calls);
    }

    [Fact]
    public async Task ResumeAsync_NothingKept_StaysSignedOutWithoutARequest()
    {
        var session = Session(new FakeSecretStore(), keep: true);
        var changes = 0;
        session.StateChanged += (_, _) => changes++;

        Assert.Equal(GitHubResumeOutcome.NothingKept, await session.ResumeAsync());

        Assert.Empty(_sent);
        Assert.Equal(0, changes);
        Assert.Equal(GitHubSessionState.SignedOut, session.State);
    }

    [Fact]
    public async Task SignOut_DeletesTheKeptToken()
    {
        var secrets = new FakeSecretStore();
        var session = await SignedInSessionAsync(secrets);
        Assert.Equal("ghr_secret", secrets[Kept]);

        session.SignOut();
        await session.WhenSecretsStoredAsync();

        Assert.Null(secrets[Kept]);
        Assert.Equal(GitHubResumeOutcome.NothingKept, await Session(secrets, keep: true).ResumeAsync());
    }

    [Fact]
    public async Task KeepSignedIn_TurnedOff_DeletesTheKeptTokenAndKeepsTheSessionInMemory()
    {
        var secrets = new FakeSecretStore();
        var session = await SignedInSessionAsync(secrets);

        session.KeepSignedIn = false;
        await session.WhenSecretsStoredAsync();

        Assert.Null(secrets[Kept]);
        Assert.Equal(GitHubSessionState.SignedInAs("octocat"), session.State);

        session.KeepSignedIn = true;
        await session.WhenSecretsStoredAsync();

        Assert.Equal("ghr_secret", secrets[Kept]);
    }

    [Fact]
    public async Task KeepSignedInOff_NeverReadsOrWritesAndOnlyDeletes()
    {
        var secrets = new FakeSecretStore();
        _tokenAnswers.Enqueue(() => Json(TokenJson));
        var session = Session(secrets, keep: false);

        Assert.True((await session.SignInAsync()).SignedIn);
        session.SignOut();
        var next = Session(secrets, keep: false);
        var outcome = await next.ResumeAsync();
        await session.WhenSecretsStoredAsync();
        await next.WhenSecretsStoredAsync();

        Assert.Equal(GitHubResumeOutcome.NothingKept, outcome);
        Assert.Equal(["delete", "delete", "delete"], secrets.Calls);
    }

    [Fact]
    public async Task KeepSignedInTurnedOff_DeleteFailed_TheNextStartWithItOffDeletesTheToken()
    {
        var secrets = new FakeSecretStore();
        var session = await SignedInSessionAsync(secrets);
        secrets.Failure = SecretStoreProblem.Refused;

        session.KeepSignedIn = false;
        await session.WhenSecretsStoredAsync();

        Assert.Equal("ghr_secret", secrets[Kept]);
        Assert.Equal(SecretStoreProblem.Refused, session.KeepSignedInProblem);
        secrets.Failure = null;
        var next = Session(secrets, keep: false);
        await next.WhenSecretsStoredAsync();

        Assert.Null(secrets[Kept]);
        Assert.Null(next.KeepSignedInProblem);
    }

    [Fact]
    public async Task SignInAsync_NotKept_WritesNothingAndDeletesTheTokenOfAnEarlierSession()
    {
        var secrets = new FakeSecretStore { [Kept] = "ghr_earlier" };
        _tokenAnswers.Enqueue(() => Json(TokenJson));
        var session = Session(secrets, keep: true);

        Assert.True((await session.SignInAsync(keepSignedIn: false)).SignedIn);
        session.KeepSignedIn = false;
        session.KeepSignedIn = true;
        await session.WhenSecretsStoredAsync();

        Assert.Equal(GitHubSessionState.SignedInAs("octocat"), session.State);
        Assert.Null(secrets[Kept]);
        Assert.DoesNotContain("write", secrets.Calls);
    }

    [Fact]
    public async Task SignInAsync_TokenWithoutARefreshToken_DeletesTheTokenOfAnEarlierSession()
    {
        var secrets = new FakeSecretStore { [Kept] = "ghr_earlier" };
        _tokenAnswers.Enqueue(() => Json("""{"access_token":"ghu_secret","token_type":"bearer","scope":""}"""));
        var session = Session(secrets, keep: true);

        Assert.True((await session.SignInAsync()).SignedIn);
        await session.WhenSecretsStoredAsync();

        Assert.Null(secrets[Kept]);
        Assert.Equal(["delete"], secrets.Calls);
    }

    [Fact]
    public async Task WithoutASecretStore_KeepsTheSessionInMemoryAndSaysWhy()
    {
        _tokenAnswers.Enqueue(() => Json(TokenJson));
        var session = Session(secrets: null, keep: true);

        Assert.Equal(SecretStoreProblem.Unsupported, session.KeepSignedInProblem);
        Assert.Equal(GitHubResumeOutcome.NothingKept, await session.ResumeAsync());
        Assert.True((await session.SignInAsync()).SignedIn);
    }

    [Theory]
    [InlineData(SecretStoreProblem.Missing)]
    [InlineData(SecretStoreProblem.Refused)]
    public async Task FailingSecretStore_WritesNothingAndSaysWhy(SecretStoreProblem problem)
    {
        var secrets = new FakeSecretStore { Failure = problem };
        _tokenAnswers.Enqueue(() => Json(TokenJson));
        var session = Session(secrets, keep: true);
        var reports = 0;
        session.KeepSignedInProblemChanged += (_, _) => reports++;

        Assert.Equal(GitHubResumeOutcome.NothingKept, await session.ResumeAsync());
        Assert.Equal(problem, session.KeepSignedInProblem);
        secrets.Failure = null;
        Assert.True((await session.SignInAsync()).SignedIn);
        await session.WhenSecretsStoredAsync();

        Assert.Null(secrets[Kept]);
        Assert.Equal(["read"], secrets.Calls);
        Assert.Equal(1, reports);
        Assert.Equal(GitHubSessionState.SignedInAs("octocat"), session.State);

        session.SignOut();
        await session.WhenSecretsStoredAsync();

        Assert.Equal(["read", "delete"], secrets.Calls);
    }

    [Fact]
    public async Task SignInAsync_WhileResuming_Throws()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _tokenGate = release.Task;
        _tokenAnswers.Enqueue(() => Json(RefreshedJson));
        var session = Session(new FakeSecretStore { [Kept] = "ghr_secret" }, keep: true);
        var resume = session.ResumeAsync();
        await WaitUntilAsync(() => session.State.Status == GitHubSessionStatus.Resuming);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SignInAsync());

        release.SetResult();
        Assert.Equal(GitHubResumeOutcome.SignedIn, await resume);
    }

    [Fact]
    public async Task SignOut_WhileResuming_StaysSignedOutAndKeepsNothing()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _tokenGate = release.Task;
        _tokenAnswers.Enqueue(() => Json(RefreshedJson));
        var secrets = new FakeSecretStore { [Kept] = "ghr_secret" };
        var session = Session(secrets, keep: true);
        var resume = session.ResumeAsync();
        await WaitUntilAsync(() => session.State.Status == GitHubSessionStatus.Resuming);

        session.SignOut();
        release.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resume);
        await session.WhenSecretsStoredAsync();
        Assert.Equal(GitHubSessionState.SignedOut, session.State);
        Assert.Null(secrets[Kept]);
    }

    [Fact]
    public async Task SendAsync_Unauthorized_KeepsTheKeptTokenForTheNextStart()
    {
        var secrets = new FakeSecretStore();
        var session = await SignedInSessionAsync(secrets);
        _otherAnswer = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        using var response = await session.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/repos"));
        await session.WhenSecretsStoredAsync();

        Assert.Equal(GitHubSessionState.SignedOut, session.State);
        Assert.Equal("ghr_secret", secrets[Kept]);
    }

    [Fact]
    public async Task LogLinesAndErrors_HoldNoToken()
    {
        var log = new ListLog();
        var secrets = new FakeSecretStore();
        _tokenAnswers.Enqueue(() => Json(TokenJson));
        var first = new LoggingGitHubSession(Session(secrets, keep: true), log);
        await first.SignInAsync();
        await ((GitHubSession)first.Inner).WhenSecretsStoredAsync();

        _tokenAnswers.Enqueue(() => Json(RefreshedJson));
        var resumed = new LoggingGitHubSession(Session(secrets, keep: true), log);
        await resumed.ResumeAsync();
        await ((GitHubSession)resumed.Inner).WhenSecretsStoredAsync();

        _tokenAnswers.Enqueue(() => throw new HttpRequestException("No route to host."));
        await new LoggingGitHubSession(Session(secrets, keep: true), log).ResumeAsync();

        _tokenAnswers.Enqueue(() => Json("""{"error":"bad_refresh_token"}"""));
        await new LoggingGitHubSession(Session(secrets, keep: true), log).ResumeAsync();

        await new LoggingGitHubSession(Session(new FakeSecretStore { Failure = SecretStoreProblem.Refused }, keep: true), log).ResumeAsync();
        var refusal = await Assert.ThrowsAsync<ArgumentException>(() => resumed.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://example.com/")));
        resumed.SignOut();

        Assert.Equal(
        [
            "Signed in to GitHub as octocat.",
            "Signed in to GitHub as octocat with the sign-in kept on this computer.",
            "Cannot reach GitHub to resume the sign-in kept on this computer. It stays for the next start.",
            "GitHub refused the sign-in kept on this computer, so Borea deleted it.",
            "Cannot keep the GitHub sign-in on this computer, Refused.",
            "Signed out of GitHub.",
        ],
            log.Lines);
        Assert.All(log.Lines.Append(refusal.Message), line => Assert.DoesNotMatch("gh[ur]_", line));
    }

    private async Task<GitHubSession> SignedInSessionAsync()
    {
        _tokenAnswers.Enqueue(() => Json(TokenJson));
        var session = Session();
        Assert.True((await session.SignInAsync()).SignedIn);
        return session;
    }

    /// <summary>A session that keeps its sign-in in <paramref name="secrets"/>, already signed in once.</summary>
    private async Task<GitHubSession> SignedInSessionAsync(FakeSecretStore secrets)
    {
        _tokenAnswers.Enqueue(() => Json(TokenJson));
        var session = Session(secrets, keep: true);
        Assert.True((await session.SignInAsync()).SignedIn);
        await session.WhenSecretsStoredAsync();
        return session;
    }

    private GitHubSession Session() => new(new HttpClient(new FakeHttpMessageHandler(Respond)), ClientId, Slug, _time);

    private GitHubSession Session(ISecretStore? secrets, bool keep) =>
        new(new HttpClient(new FakeHttpMessageHandler(Respond)), ClientId, Slug, _time, secrets) { KeepSignedIn = keep };

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
                await _tokenGate;
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

    /// <summary>A secret store in memory. It records the kind of every call, never the secret, and fails every call with <see cref="Failure"/> while that is set.</summary>
    private sealed class FakeSecretStore : ISecretStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, string> _secrets = [];
        private readonly List<string> _calls = [];

        public SecretStoreProblem? Failure { get; set; }

        public IReadOnlyList<string> Calls
        {
            get
            {
                lock (_gate)
                    return _calls.ToArray();
            }
        }

        public string? this[string name]
        {
            get
            {
                lock (_gate)
                    return _secrets.GetValueOrDefault(name);
            }

            init => _secrets[name] = value!;
        }

        public string? Read(string name)
        {
            Record("read");
            lock (_gate)
                return _secrets.GetValueOrDefault(name);
        }

        public void Write(string name, string secret)
        {
            Record("write");
            lock (_gate)
                _secrets[name] = secret;
        }

        public void Delete(string name)
        {
            Record("delete");
            lock (_gate)
                _secrets.Remove(name);
        }

        private void Record(string call)
        {
            lock (_gate)
                _calls.Add(call);

            if (Failure is { } problem)
                throw new SecretStoreException(problem, "The fake store fails.");
        }
    }

    private sealed class ListLog : IBoreaLog
    {
        private readonly List<string> _lines = [];

        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (_lines)
                    return _lines.ToArray();
            }
        }

        public string CurrentFilePath => "borea.log";

        public void Write(string message)
        {
            lock (_lines)
                _lines.Add(message);
        }

        public void Write(string message, Exception exception) => Write(message + " " + exception);

        public IReadOnlyList<string> ReadRecentLines(int maxLines) => Lines;
    }

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
