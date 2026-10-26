using System.Net;
using System.Text;
using System.Text.Json;
using Borea.Core.Stewardship;
using Borea.Network.GitHub;

namespace Borea.Network.Tests.GitHub;

/// <summary>content-index answers its open issues from a list this test keeps in memory, and the requests of the other tests from their routes.</summary>
public sealed partial class GitHubIndexReportsTests
{
    private const string Api = "https://api.github.com";
    private const string Index = "KSAModding/content-index";
    private const string Token = "ghu_secret";
    private const string IssuesUrl = $"{Api}/repos/{Index}/issues?state=open&sort=created&direction=asc&per_page=100&page=1";
    private const string TakedownBody = "### Listing id\n\ntools-pack 1.1.0\n\n### Ground\n\nThe archive carries something harmful\n\n### What is wrong\n\nThe installer runs a script.\n\n### Who you are\n\nA player.";
    private const string DisputeBody = "### Listing id\n\nMeasureTools\n\n### What is disputed\n\nSomething else, explained below\n\n### Your forums thread\n\nhttps://forums.ahwoo.com/threads/measure-tools.123/\n\n### Your claim\n\nI announced it first.\n\n### The other party\n\n@bob";

    private static readonly DateTimeOffset Day = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly List<Sent> _sent = [];
    private readonly List<object> _issues = [];
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = [];
    private HttpStatusCode? _failing;

    [Fact]
    public async Task ListAsync_ReadsTakedownsAndDisputes_AndLeavesOutOtherIssuesAndPullRequests()
    {
        Issue(10, "[Takedown] Harmful installer", TakedownBody, "alice");
        Issue(11, "Compendium: the watcher found a problem", "<!-- watcher:listing=Compendium -->", "ksamodding-indexer-bot[bot]");
        Issue(12, "[Dispute] MeasureTools is mine", DisputeBody, "bob");
        _issues.Add(new { number = 13, title = "[Takedown] a pull request", html_url = $"https://github.com/{Index}/pull/13", body = TakedownBody, created_at = Day, updated_at = Day, user = new { login = "carol", type = "User" }, pull_request = new { url = $"{Api}/repos/{Index}/pulls/13" } });
        var reports = await SignedInAsync();

        var listed = await reports.ListAsync();

        Assert.Equal([(10, IndexReportKind.Takedown), (12, IndexReportKind.Dispute)], listed.Select(report => (report.Number, report.Kind)));
        var takedown = listed[0];
        Assert.Equal((new Uri($"https://github.com/{Index}/issues/10"), "[Takedown] Harmful installer", "alice", Day.AddHours(10)), (takedown.Url, takedown.Title, takedown.Author, takedown.Created));
        Assert.Equal(("tools-pack", "1.1.0", "The archive carries something harmful", "A player."), (takedown.Id, takedown.Version, takedown.Ground, takedown.Reporter));
        var dispute = listed[1];
        Assert.Equal(("MeasureTools", "I announced it first.", "@bob"), (dispute.Id, dispute.Claim, dispute.OtherParty));
    }

    [Fact]
    public async Task ListAsync_ReadsWithoutTheToken()
    {
        var reports = await SignedInAsync();

        await reports.ListAsync();

        var sent = Assert.Single(_sent);
        Assert.Equal(("GET", IssuesUrl, (string?)null), (sent.Method, sent.Url, sent.Authorization));
    }

    [Fact]
    public async Task ListAsync_SignedOut_StillReadsThePublicIssues()
    {
        Issue(10, "[Takedown] Harmful installer", TakedownBody, "alice");
        var reports = new GitHubIndexReports(new GitHubSession(Http(), "Iv1.testclient", "borea-test"), Http());

        var listed = await reports.ListAsync();

        Assert.Equal([10], listed.Select(report => report.Number));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, StewardFailure.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, StewardFailure.NotFound)]
    [InlineData(HttpStatusCode.BadGateway, StewardFailure.UnexpectedResponse)]
    public async Task ListAsync_Failure_ThrowsAsAStewardFailure(HttpStatusCode status, StewardFailure failure)
    {
        _failing = status;
        var reports = await SignedInAsync();

        var exception = await Assert.ThrowsAsync<StewardException>(() => reports.ListAsync());

        Assert.Equal(failure, exception.Failure);
    }

    [Fact]
    public async Task ListAsync_IssueWithoutAnHttpsLink_IsAnUnexpectedResponse()
    {
        _issues.Add(new { number = 10, title = "[Takedown] Harmful installer", html_url = "javascript:alert(1)", body = TakedownBody, created_at = Day, updated_at = Day, user = new { login = "alice", type = "User" } });
        var reports = await SignedInAsync();

        var exception = await Assert.ThrowsAsync<StewardException>(() => reports.ListAsync());

        Assert.Equal(StewardFailure.UnexpectedResponse, exception.Failure);
    }

    [Fact]
    public async Task ListAsync_RateLimitWithoutTheToken_SaysWhenToTryAgain()
    {
        var reset = DateTimeOffset.FromUnixTimeSeconds(1790000000);
        var reports = new GitHubIndexReports(await SignInAsync(), Http(request => request.RequestUri!.AbsoluteUri == IssuesUrl ? RateLimited(reset) : null));

        var exception = await Assert.ThrowsAsync<StewardException>(() => reports.ListAsync());

        Assert.Equal((StewardFailure.RateLimited, reset), (exception.Failure, exception.RetryAt));
    }

    private void Issue(int number, string title, string body, string author) =>
        _issues.Add(new
        {
            number,
            title,
            html_url = $"https://github.com/{Index}/issues/{number}",
            body,
            created_at = Day.AddHours(number),
            updated_at = Day.AddHours(number + 1),
            user = new { login = author, type = author.EndsWith("[bot]", StringComparison.Ordinal) ? "Bot" : "User" },
        });

    private async Task<GitHubIndexReports> SignedInAsync() => new(await SignInAsync(), Http());

    private async Task<GitHubSession> SignInAsync()
    {
        var session = new GitHubSession(Http(), "Iv1.testclient", "borea-test", new InstantTimeProvider());
        Assert.True((await session.SignInAsync()).SignedIn);
        lock (_sent)
            _sent.Clear();
        return session;
    }

    private HttpClient Http(Func<HttpRequestMessage, HttpResponseMessage?>? respond = null) =>
        new(new FakeHttpMessageHandler(async request => respond?.Invoke(request) ?? Respond(request, request.Content is null ? null : await request.Content.ReadAsStringAsync())));

    private HttpResponseMessage Respond(HttpRequestMessage request, string? body)
    {
        var url = request.RequestUri!.AbsoluteUri;
        lock (_sent)
            _sent.Add(new Sent(request.Method.Method, url, request.Headers.Authorization?.ToString(), body));

        if (url == "https://github.com/login/device/code")
            return Json("""{"device_code":"d","user_code":"WDJB-MJHT","verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}""");
        if (url == "https://github.com/login/oauth/access_token")
            return Json($$"""{"access_token":"{{Token}}","token_type":"bearer"}""");
        if (url == Api + "/user")
            return Json("""{"login":"octocat","id":1}""");
        // a route that ends with " anonymous" answers only the request without the token
        if ((request.Headers.Authorization is null && _routes.TryGetValue($"{request.Method.Method} {url} anonymous", out var route))
            || _routes.TryGetValue($"{request.Method.Method} {url}", out route))
            return route();
        if (url != IssuesUrl)
            return Json("""{"message":"Not Found"}""", HttpStatusCode.NotFound);

        return _failing is { } status ? Json("""{"message":"Failed"}""", status) : Json(JsonSerializer.Serialize(_issues));
    }

    private static HttpResponseMessage RateLimited(DateTimeOffset reset)
    {
        var response = Json("""{"message":"API rate limit exceeded for 127.0.0.1."}""", HttpStatusCode.Forbidden);
        response.Headers.Add("x-ratelimit-remaining", "0");
        response.Headers.Add("x-ratelimit-reset", reset.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        return response;
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed record Sent(string Method, string Url, string? Authorization, string? Body = null);

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
