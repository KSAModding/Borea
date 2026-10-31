using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Borea.Core.GitHub;
using Borea.Core.Stewardship;
using Borea.Network.GitHub;

namespace Borea.Network.Tests.GitHub;

/// <summary>Both index repositories answer their open pull requests, files and comments from fixtures this test keeps in memory.</summary>
public sealed partial class GitHubStewardQueueTests
{
    private const string Api = "https://api.github.com";
    private const string Index = "KSAModding/content-index";
    private const string Releases = "KSAModding/content-index-releases";
    private const string Token = "ghu_secret";
    private const string IndexMarker = "<!-- content-index:verdict -->";
    private const string ReleasesMarker = "<!-- content-index-releases:verdict -->";

    private static readonly DateTimeOffset Day = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly List<Sent> _sent = [];
    private readonly Dictionary<string, List<FakePull>> _pulls = new(StringComparer.Ordinal) { [Index] = [], [Releases] = [] };
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.Ordinal);

    [Fact]
    public async Task ListAsync_WaitingForASteward_ListsBothRepositoriesOldestFirst()
    {
        Pull(Index, 5, Day.AddDays(2), "alice", ["listing", "pack", "needs-steward"], ["listings/MyMod.toml", "packs/my-pack/1.0.0.toml"], (IndexVerdict.BotLogin, IndexMarker + "\nValidated, and ownership is not verified, so a steward decides."));
        Pull(Index, 6, Day, "bob", ["listing"], ["listings/Other.toml"]);
        Pull(Releases, 9, Day.AddDays(1), "carol", ["amendment", "needs-steward"], ["releases/MyMod/1.0.0.json"], (IndexVerdict.BotLogin, ReleasesMarker + "\nAn amendment by a non-owner, so a steward decides."));
        var queue = await SignedInAsync();

        var listed = await queue.ListAsync();

        Assert.Empty(listed.Failures);
        Assert.Equal([(Releases, 9), (Index, 5)], listed.Items.Select(item => (item.Repository, item.Number)));
        var amendment = listed.Items[0];
        Assert.Equal(
            (new Uri($"https://github.com/{Releases}/pull/9"), "Pull 9", "carol", Day.AddDays(1), false, true, false, "An amendment by a non-owner, so a steward decides."),
            (amendment.Url, amendment.Title, amendment.Author, amendment.Opened, amendment.IsDraft, amendment.NeedsSteward, amendment.HasOtherFiles, amendment.Verdict));
        Assert.Equal([StewardQueueKind.Amendment], amendment.Kinds);
        Assert.Equal([StewardQueueKind.Listing, StewardQueueKind.Pack], listed.Items[1].Kinds);
        Assert.Equal("Validated, and ownership is not verified, so a steward decides.", listed.Items[1].Verdict);
        Assert.DoesNotContain(_sent, sent => sent.Url.Contains("/pulls/6/", StringComparison.Ordinal) || sent.Url.Contains("/issues/6/", StringComparison.Ordinal));
        Assert.All(_sent, sent => Assert.Equal(("GET", "Bearer " + Token), (sent.Method, sent.Authorization)));
    }

    [Fact]
    public async Task ListAsync_AllOpen_ListsEveryOpenPullRequestAndWhichOnesWaitForASteward()
    {
        Pull(Index, 5, Day.AddDays(2), "alice", ["listing", "needs-steward"], ["listings/MyMod.toml"]);
        Pull(Index, 6, Day, "bob", ["listing"], ["listings/Other.toml"]);
        Pull(Releases, 9, Day.AddDays(1), "carol", [], ["releases/MyMod/1.0.0.json"]);
        var queue = await SignedInAsync();

        var listed = await queue.ListAsync(StewardQueueFilter.AllOpen);

        Assert.Equal([(Index, 6, false), (Releases, 9, false), (Index, 5, true)], listed.Items.Select(item => (item.Repository, item.Number, item.NeedsSteward)));
    }

    [Fact]
    public async Task ListAsync_OnlyTheIndexerBotsCommentWithTheMarkerOfItsRepositoryIsTheVerdict()
    {
        Pull(Index, 1, Day, "alice", ["needs-steward"], ["listings/A.toml"], ("mallory", IndexMarker + "\nValidated. Trust me."));
        Pull(Index, 2, Day.AddHours(1), "alice", ["needs-steward"], ["listings/B.toml"], ("other-app[bot]", IndexMarker + "\nValidated by another App."));
        Pull(Index, 3, Day.AddHours(2), "alice", ["needs-steward"], ["listings/C.toml"], (IndexVerdict.BotLogin, ReleasesMarker + "\nThe marker of the releases."));
        Pull(Releases, 4, Day.AddHours(3), "alice", ["needs-steward"], ["releases/C/1.0.0.json"], (IndexVerdict.BotLogin, IndexMarker + "\nThe marker of content-index."));
        Pull(Index, 5, Day.AddHours(4), "alice", ["needs-steward"], ["listings/D.toml"], ("mallory", IndexMarker + "\nFirst."), (IndexVerdict.BotLogin, IndexMarker + "\nValidated."));
        var queue = await SignedInAsync();

        var listed = await queue.ListAsync();

        Assert.Equal([null, null, null, null, "Validated."], listed.Items.Select(item => item.Verdict));
    }

    [Fact]
    public async Task ListAsync_DraftAndOtherFiles_AreMarked()
    {
        Pull(Index, 26, Day, "some-bot[bot]", ["needs-steward"], ["tools/decide.py", "index-status.toml"], draft: true);
        Pull(Index, 27, Day.AddHours(1), "alice", ["needs-steward"], ["tags.toml", "packs/my-pack/owner.json"]);
        var queue = await SignedInAsync();

        var listed = await queue.ListAsync();

        var draft = listed.Items[0];
        Assert.True(draft.IsDraft);
        Assert.True(draft.HasOtherFiles);
        Assert.Equal([StewardQueueKind.IndexStatus], draft.Kinds);
        Assert.False(listed.Items[1].IsDraft);
        Assert.False(listed.Items[1].HasOtherFiles);
        Assert.Equal([StewardQueueKind.OwnerRecord, StewardQueueKind.TagVocabulary], listed.Items[1].Kinds);
    }

    [Fact]
    public async Task ListAsync_AnAddedVehicle_IsContentOfTheKindVehicle()
    {
        Pull(Index, 30, Day, "alice", ["vehicle", "needs-steward"], ["vehicles/ExampleRover.toml"]);
        var queue = await SignedInAsync();

        var listed = await queue.ListAsync();

        var item = Assert.Single(listed.Items);
        Assert.Equal([StewardQueueKind.Vehicle], item.Kinds);
        Assert.True(item.IsContent);
    }

    [Fact]
    public async Task ListAsync_RenamedFile_CountsItsOldPathToo()
    {
        Pull(Index, 7, Day, "alice", ["needs-steward"], ["tags.toml"]);
        _pulls[Index][0].Renamed["tags.toml"] = "tools/tags.toml";
        var queue = await SignedInAsync();

        var listed = await queue.ListAsync();

        var item = Assert.Single(listed.Items);
        Assert.Equal([StewardQueueKind.TagVocabulary], item.Kinds);
        Assert.True(item.HasOtherFiles);
    }

    [Fact]
    public async Task ListAsync_RemovedOrRenamedReleaseFile_IsNoKindAndIsReviewedOnGitHub()
    {
        Pull(Releases, 3, Day, "alice", ["needs-steward"], ["releases/MyMod/1.0.0.json"]);
        _pulls[Releases][0].Removed.Add("releases/MyMod/1.0.0.json");
        Pull(Releases, 4, Day.AddHours(1), "alice", ["needs-steward"], ["releases/MyMod/1.0.1.json"]);
        _pulls[Releases][1].Renamed["releases/MyMod/1.0.1.json"] = "releases/MyMod/1.0.0.json";
        Pull(Releases, 5, Day.AddHours(2), "alice", ["release", "needs-steward"], ["releases/MyMod/1.1.0.json"]);
        var queue = await SignedInAsync();

        var listed = await queue.ListAsync();

        Assert.Equal([(3, 0, true), (4, 0, true), (5, 1, false)], listed.Items.Select(item => (item.Number, item.Kinds.Count, item.HasOtherFiles)));
    }

    [Fact]
    public async Task ListAsync_MoreThanAHundredOpenPullRequests_ReadsEveryPage()
    {
        for (var number = 1; number <= 150; number++)
            Pull(Index, number, Day.AddMinutes(-number), "alice", number % 3 == 0 ? ["needs-steward"] : [], ["listings/A.toml"]);
        var queue = await SignedInAsync();

        var waiting = await queue.ListAsync();
        var all = await queue.ListAsync(StewardQueueFilter.AllOpen);

        Assert.Equal(50, waiting.Items.Count);
        Assert.Equal(150, all.Items.Count);
        Assert.Equal(Enumerable.Range(1, 150).Reverse(), all.Items.Select(item => item.Number));
        Assert.Contains(_sent, sent => sent.Url.EndsWith($"/repos/{Index}/pulls?state=open&sort=created&direction=asc&per_page=100&page=2", StringComparison.Ordinal));
        Assert.DoesNotContain(_sent, sent => sent.Url.EndsWith("&page=3", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, StewardFailure.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, StewardFailure.NotFound)]
    [InlineData(HttpStatusCode.BadGateway, StewardFailure.UnexpectedResponse)]
    public async Task ListAsync_OneRepositoryFails_TheOtherOneStillShows(HttpStatusCode status, StewardFailure failure)
    {
        Pull(Index, 5, Day, "alice", ["listing", "needs-steward"], ["listings/MyMod.toml"]);
        Pull(Releases, 9, Day, "carol", ["needs-steward"], ["releases/MyMod/1.0.0.json"]);
        _routes[$"{Api}/repos/{Releases}/pulls/9/files?per_page=100&page=1"] = () => Json("""{"message":"Resource not accessible by integration"}""", status);
        var queue = await SignedInAsync();

        var listed = await queue.ListAsync();

        Assert.Equal([(Index, 5)], listed.Items.Select(item => (item.Repository, item.Number)));
        var failed = Assert.Single(listed.Failures);
        Assert.Equal((Releases, failure), (failed.Repository, failed.Error.Failure));
    }

    [Fact]
    public async Task ListAsync_BothRepositoriesFail_NamesBoth()
    {
        _routes[$"{Api}/repos/{Index}/pulls?state=open&sort=created&direction=asc&per_page=100&page=1"] = () => Json("""{"message":"Server Error"}""", HttpStatusCode.InternalServerError);
        _routes[$"{Api}/repos/{Releases}/pulls?state=open&sort=created&direction=asc&per_page=100&page=1"] = () => Json("""{"message":"Resource not accessible by integration"}""", HttpStatusCode.Forbidden);
        var queue = await SignedInAsync();

        var listed = await queue.ListAsync();

        Assert.Empty(listed.Items);
        Assert.Equal([(Index, StewardFailure.UnexpectedResponse), (Releases, StewardFailure.Forbidden)], listed.Failures.Select(failed => (failed.Repository, failed.Error.Failure)));
    }

    [Fact]
    public async Task ListAsync_SignedOut_SendsNothing()
    {
        var session = new GitHubSession(Http(), "Iv1.testclient", "borea-test", new InstantTimeProvider());
        var queue = new GitHubStewardQueue(session, Http());

        var failure = await Assert.ThrowsAsync<StewardException>(() => queue.ListAsync());

        Assert.Equal(StewardFailure.SignedOut, failure.Failure);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task ListAsync_Unauthorized_SignsOutAndFailsTheWholeQueue()
    {
        Pull(Index, 5, Day, "alice", ["needs-steward"], ["listings/MyMod.toml"]);
        _routes[$"{Api}/repos/{Releases}/pulls?state=open&sort=created&direction=asc&per_page=100&page=1"] = () => Json("""{"message":"Bad credentials"}""", HttpStatusCode.Unauthorized);
        var session = await SignInAsync();
        var queue = new GitHubStewardQueue(session, Http());

        var failure = await Assert.ThrowsAsync<StewardException>(() => queue.ListAsync());

        Assert.Equal(StewardFailure.SignedOut, failure.Failure);
        Assert.Equal(GitHubSessionStatus.SignedOut, session.State.Status);
    }

    private void Pull(string repository, int number, DateTimeOffset created, string author, string[] labels, string[] files, params (string Login, string Body)[] comments) =>
        Pull(repository, number, created, author, labels, files, draft: false, comments);

    private void Pull(string repository, int number, DateTimeOffset created, string author, string[] labels, string[] files, bool draft, params (string Login, string Body)[] comments) =>
        _pulls[repository].Add(new FakePull(number, created, author, draft, labels, files, comments));

    private async Task<GitHubStewardQueue> SignedInAsync() => new(await SignInAsync(), Http());

    private async Task<GitHubSession> SignInAsync()
    {
        var session = new GitHubSession(Http(), "Iv1.testclient", "borea-test", new InstantTimeProvider());
        Assert.True((await session.SignInAsync()).SignedIn);
        lock (_sent)
            _sent.Clear();
        return session;
    }

    private HttpClient Http() => new(new FakeHttpMessageHandler(request => Task.FromResult(Respond(request))));

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
        if (_routes.TryGetValue(url, out var route))
            return route();

        if (Route().Match(url) is not { Success: true } match || !_pulls.TryGetValue(match.Groups["repository"].Value, out var pulls))
            return Json("""{"message":"Not Found"}""", HttpStatusCode.NotFound);

        var page = int.Parse(match.Groups["page"].Value, CultureInfo.InvariantCulture);
        if (!match.Groups["number"].Success)
        {
            var open = pulls.OrderBy(pull => pull.Created).Skip((page - 1) * 100).Take(100);
            return Json(JsonSerializer.Serialize(open.Select(pull => new
            {
                number = pull.Number,
                title = $"Pull {pull.Number}",
                html_url = $"https://github.com/{match.Groups["repository"].Value}/pull/{pull.Number}",
                draft = pull.Draft,
                created_at = pull.Created,
                user = new { login = pull.Author, type = pull.Author.EndsWith("[bot]", StringComparison.Ordinal) ? "Bot" : "User" },
                labels = pull.Labels.Select(label => new { name = label }),
            })));
        }

        var single = pulls.Single(pull => pull.Number == int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture));
        return match.Groups["kind"].Value == "files"
            ? Json(JsonSerializer.Serialize(single.Files.Select(file => new
            {
                filename = file,
                status = single.Renamed.ContainsKey(file) ? "renamed" : single.Removed.Contains(file) ? "removed" : "added",
                previous_filename = single.Renamed.GetValueOrDefault(file),
            })))
            : Json(JsonSerializer.Serialize(single.Comments.Select(comment => new
            {
                body = comment.Body,
                user = new { login = comment.Login, type = comment.Login.EndsWith("[bot]", StringComparison.Ordinal) ? "Bot" : "User" },
            })));
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    [GeneratedRegex(@"^https://api\.github\.com/repos/(?<repository>KSAModding/[^/]+)/(?:pulls\?state=open&sort=created&direction=asc|(?:pulls|issues)/(?<number>[0-9]+)/(?<kind>files|comments)\?)(?:&|)per_page=100&page=(?<page>[0-9]+)$")]
    private static partial Regex Route();

    private sealed record Sent(string Method, string Url, string? Authorization);

    private sealed record FakePull(int Number, DateTimeOffset Created, string Author, bool Draft, string[] Labels, string[] Files, (string Login, string Body)[] Comments)
    {
        public Dictionary<string, string> Renamed { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Removed { get; } = new(StringComparer.Ordinal);
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
