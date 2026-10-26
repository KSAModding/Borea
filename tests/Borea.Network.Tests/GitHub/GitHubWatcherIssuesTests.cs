using System.Net;
using System.Text;
using System.Text.Json;
using Borea.Core.Stewardship;
using Borea.Network.GitHub;

namespace Borea.Network.Tests.GitHub;

/// <summary>Both index repositories answer their open issues from lists this test keeps in memory.</summary>
public sealed class GitHubWatcherIssuesTests
{
    private const string Api = "https://api.github.com";
    private const string Index = "KSAModding/content-index";
    private const string Releases = "KSAModding/content-index-releases";
    private const string Token = "ghu_secret";
    private const string ListingsUrl = $"{Api}/repos/{Index}/issues?state=open&sort=created&direction=asc&labels=watcher&per_page=100&page=1";
    private const string WatchdogUrl = $"{Api}/repos/{Releases}/issues?state=open&sort=created&direction=asc&per_page=100&page=1";
    private const string WatchdogBody = "<!-- watchdog:workflow=watcher.yml -->\n`watcher.yml` last started a successful scheduled run 2026-09-25T20:04:00Z.";

    private static readonly DateTimeOffset Day = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly List<Sent> _sent = [];
    private readonly Dictionary<string, List<object>> _issues = new(StringComparer.Ordinal) { [ListingsUrl] = [], [WatchdogUrl] = [] };
    private readonly Dictionary<string, HttpStatusCode> _failing = new(StringComparer.Ordinal);

    [Fact]
    public async Task ListAsync_ListingIssues_NameTheListingOfTheirMarker()
    {
        Issue(ListingsUrl, Index, 102, "Compendium: the watcher found a problem", "<!-- watcher:listing=Compendium -->\n<!-- watcher:signature=28a9f08f3c40a6e8 -->\nThe watcher found a problem.");
        Issue(ListingsUrl, Index, 103, "A report by hand", "Somebody put the watcher label on this issue.", author: "alice");
        PullRequest(ListingsUrl, Index, 104, "<!-- watcher:listing=Other -->");
        var issues = await SignedInAsync();

        var listed = await issues.ListAsync();

        Assert.Empty(listed.Failures);
        Assert.Equal([(102, "Compendium"), (103, null)], listed.Listings.Select(issue => (issue.Number, issue.ListingId)));
        var first = listed.Listings[0];
        Assert.Equal(
            (Index, new Uri($"https://github.com/{Index}/issues/102"), "Compendium: the watcher found a problem", Day.AddHours(102)),
            (first.Repository, first.Url, first.Title, first.Updated));
        Assert.Empty(listed.Watchdog);
    }

    [Fact]
    public async Task ListAsync_ReadsBothRepositoriesWithoutTheToken()
    {
        var issues = await SignedInAsync();

        await issues.ListAsync();

        Assert.Equal([WatchdogUrl, ListingsUrl], _sent.Select(sent => sent.Url).Order(StringComparer.Ordinal));
        Assert.All(_sent, sent => Assert.Equal(("GET", null), (sent.Method, sent.Authorization)));
    }

    [Fact]
    public async Task ListAsync_OpenWatchdogIssue_IsTheOneWithTheMarkerOfTheWatchdogBot()
    {
        Issue(WatchdogUrl, Releases, 70, "The watcher is not ticking", WatchdogBody, author: "alice");
        Issue(WatchdogUrl, Releases, 80, "Amend a release", "An issue about something else.", author: IndexVerdict.BotLogin);
        Issue(WatchdogUrl, Releases, 81, "The watcher is not ticking", WatchdogBody, author: IndexVerdict.BotLogin);
        PullRequest(WatchdogUrl, Releases, 82, WatchdogBody);
        var issues = await SignedInAsync();

        var listed = await issues.ListAsync();

        var watchdog = Assert.Single(listed.Watchdog);
        Assert.Equal((Releases, 81, "The watcher is not ticking", (string?)null), (watchdog.Repository, watchdog.Number, watchdog.Title, watchdog.ListingId));
        Assert.Empty(listed.Failures);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, StewardFailure.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, StewardFailure.NotFound)]
    [InlineData(HttpStatusCode.BadGateway, StewardFailure.UnexpectedResponse)]
    public async Task ListAsync_ListingsFail_TheWatchdogStillShows(HttpStatusCode status, StewardFailure failure)
    {
        Issue(ListingsUrl, Index, 102, "Compendium: the watcher found a problem", "<!-- watcher:listing=Compendium -->");
        Issue(WatchdogUrl, Releases, 81, "The watcher is not ticking", WatchdogBody, author: IndexVerdict.BotLogin);
        _failing[ListingsUrl] = status;
        var issues = await SignedInAsync();

        var listed = await issues.ListAsync();

        Assert.Empty(listed.Listings);
        Assert.Equal([81], listed.Watchdog.Select(issue => issue.Number));
        var failed = Assert.Single(listed.Failures);
        Assert.Equal((Index, failure), (failed.Repository, failed.Error.Failure));
    }

    [Fact]
    public async Task ListAsync_WatchdogFails_TheListingsStillShow()
    {
        Issue(ListingsUrl, Index, 102, "Compendium: the watcher found a problem", "<!-- watcher:listing=Compendium -->");
        _failing[WatchdogUrl] = HttpStatusCode.InternalServerError;
        var issues = await SignedInAsync();

        var listed = await issues.ListAsync();

        Assert.Equal(["Compendium"], listed.Listings.Select(issue => issue.ListingId));
        Assert.Empty(listed.Watchdog);
        var failed = Assert.Single(listed.Failures);
        Assert.Equal((Releases, StewardFailure.UnexpectedResponse), (failed.Repository, failed.Error.Failure));
    }

    [Fact]
    public async Task ListAsync_RateLimitWithoutTheToken_SaysWhenToTryAgain()
    {
        var reset = DateTimeOffset.FromUnixTimeSeconds(1790000000);
        var issues = new GitHubWatcherIssues(await SignInAsync(), Http(request => request.RequestUri!.AbsoluteUri == ListingsUrl
            ? RateLimited(reset)
            : null));

        var listed = await issues.ListAsync();

        var failed = Assert.Single(listed.Failures);
        Assert.Equal((Index, StewardFailure.RateLimited, reset), (failed.Repository, failed.Error.Failure, failed.Error.RetryAt));
    }

    [Fact]
    public async Task ListAsync_SignedOut_StillReadsThePublicIssues()
    {
        Issue(ListingsUrl, Index, 102, "Compendium: the watcher found a problem", "<!-- watcher:listing=Compendium -->");
        var session = new GitHubSession(Http(), "Iv1.testclient", "borea-test");
        var issues = new GitHubWatcherIssues(session, Http());

        var listed = await issues.ListAsync();

        Assert.Equal([102], listed.Listings.Select(issue => issue.Number));
        Assert.Empty(listed.Failures);
    }

    private void Issue(string url, string repository, int number, string title, string body, string author = "ksamodding-indexer-bot[bot]") =>
        _issues[url].Add(new
        {
            number,
            title,
            html_url = $"https://github.com/{repository}/issues/{number}",
            body,
            updated_at = Day.AddHours(number),
            user = new { login = author, type = author.EndsWith("[bot]", StringComparison.Ordinal) ? "Bot" : "User" },
        });

    private void PullRequest(string url, string repository, int number, string body) =>
        _issues[url].Add(new
        {
            number,
            title = $"Pull {number}",
            html_url = $"https://github.com/{repository}/pull/{number}",
            body,
            updated_at = Day,
            user = new { login = IndexVerdict.BotLogin, type = "Bot" },
            pull_request = new { url = $"{Api}/repos/{repository}/pulls/{number}" },
        });

    private async Task<GitHubWatcherIssues> SignedInAsync() => new(await SignInAsync(), Http());

    private async Task<GitHubSession> SignInAsync()
    {
        var session = new GitHubSession(Http(), "Iv1.testclient", "borea-test", new InstantTimeProvider());
        Assert.True((await session.SignInAsync()).SignedIn);
        lock (_sent)
            _sent.Clear();
        return session;
    }

    private HttpClient Http(Func<HttpRequestMessage, HttpResponseMessage?>? respond = null) =>
        new(new FakeHttpMessageHandler(request => Task.FromResult(respond?.Invoke(request) ?? Respond(request))));

    private HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var url = request.RequestUri!.AbsoluteUri;
        lock (_sent)
            _sent.Add(new Sent(request.Method.Method, url, request.Headers.Authorization?.ToString()));

        if (url == "https://github.com/login/device/code")
            return Json("""{"device_code":"d","user_code":"WDJB-MJHT","verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}""");
        if (url == "https://github.com/login/oauth/access_token")
            return Json($$"""{"access_token":"{{Token}}","token_type":"bearer"}""");
        if (url == Api + "/user")
            return Json("""{"login":"octocat","id":1}""");
        if (_failing.TryGetValue(url, out var status))
            return Json("""{"message":"Failed"}""", status);

        return _issues.TryGetValue(url, out var issues)
            ? Json(JsonSerializer.Serialize(issues))
            : Json("""{"message":"Not Found"}""", HttpStatusCode.NotFound);
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

    private sealed record Sent(string Method, string Url, string? Authorization);

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
