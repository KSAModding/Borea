using System.Net;
using System.Text.Json;
using Borea.Core.Stewardship;
using Borea.Network.GitHub;

namespace Borea.Network.Tests.GitHub;

/// <summary>The steward octocat comments on, closes and reopens report #10 of content-index.</summary>
public sealed partial class GitHubIndexReportsTests
{
    private const string ClosedUrl = $"{Api}/search/issues?q=repo:{Index}+is:issue+is:closed+in:title+(Takedown+OR+Dispute)&sort=updated&order=desc&per_page=100&page=1";
    private const string InstallationsUrl = $"{Api}/user/installations?per_page=100&page=1";
    private const string IssueUrl = $"{Api}/repos/{Index}/issues/10";
    private const string CommentsUrl = IssueUrl + "/comments";

    [Fact]
    public async Task ListAsync_RecentlyClosed_SearchesOnePageOfClosedReportsWithoutTheToken_WithTheReasonOfEachReport()
    {
        var closed = Day.AddDays(3);
        _routes[$"GET {ClosedUrl}"] = () => Json(JsonSerializer.Serialize(new
        {
            total_count = 4,
            items = new object[]
            {
                ClosedIssue(12, "[Dispute] MeasureTools is mine", DisputeBody, "not_planned", closed),
                ClosedIssue(11, "Compendium: the watcher found a problem", "<!-- watcher:listing=Compendium -->", "completed", closed),
                ClosedIssue(10, "[Takedown] Harmful installer", TakedownBody, "completed", closed),
                ClosedIssue(9, "[Takedown] An old one", TakedownBody, "duplicate", closed),
            },
        }));
        var reports = await SignedInAsync();

        var listed = await reports.ListAsync(IndexReportFilter.RecentlyClosed);

        Assert.Equal(
            [(12, IndexReportCloseReason.NotPlanned), (10, IndexReportCloseReason.Completed), (9, (IndexReportCloseReason?)null)],
            listed.Select(report => (report.Number, report.CloseReason)));
        Assert.All(listed, report => Assert.Equal((true, (DateTimeOffset?)closed), (report.IsClosed, report.Closed)));
        var sent = Assert.Single(_sent);
        Assert.Equal(("GET", ClosedUrl, (string?)null), (sent.Method, sent.Url, sent.Authorization));
    }

    [Fact]
    public async Task ListAsync_Open_SaysTheReportsAreOpen()
    {
        Issue(10, "[Takedown] Harmful installer", TakedownBody, "alice");
        var reports = await SignedInAsync();

        var report = Assert.Single(await reports.ListAsync());

        Assert.Equal((false, (IndexReportCloseReason?)null, (DateTimeOffset?)null), (report.IsClosed, report.CloseReason, report.Closed));
    }

    [Fact]
    public async Task CommentAsync_PostsTheTrimmedCommentWithTheToken()
    {
        Report("open");
        _routes[$"POST {CommentsUrl}"] = () => Json("""{"id":1}""", HttpStatusCode.Created);
        var reports = await SignedInAsync();

        await reports.CommentAsync(Shown(), "  Please answer here, @alice.  ");

        Assert.Equal([$"GET {IssueUrl}", $"POST {CommentsUrl}"], _sent.Select(sent => $"{sent.Method} {sent.Url}"));
        AssertJson("""{"body":"Please answer here, @alice."}""", _sent[^1].Body);
        Assert.All(_sent, sent => Assert.Equal("Bearer " + Token, sent.Authorization));
    }

    [Theory]
    [InlineData(IndexReportCloseReason.Completed, "completed")]
    [InlineData(IndexReportCloseReason.NotPlanned, "not_planned")]
    public async Task CloseAsync_PostsTheCommentFirst_AndClosesWithTheReason(IndexReportCloseReason reason, string stateReason)
    {
        Report("open");
        _routes[$"POST {CommentsUrl}"] = () => Json("""{"id":1}""", HttpStatusCode.Created);
        _routes[$"PATCH {IssueUrl}"] = () => Json("""{"number":10,"state":"closed"}""");
        var reports = await SignedInAsync();

        await reports.CloseAsync(Shown(), reason, "The takedown does not hold.");

        Assert.Equal([$"GET {IssueUrl}", $"POST {CommentsUrl}", $"PATCH {IssueUrl}"], _sent.Select(sent => $"{sent.Method} {sent.Url}"));
        AssertJson("""{"body":"The takedown does not hold."}""", _sent[1].Body);
        AssertJson($$"""{"state":"closed","state_reason":"{{stateReason}}"}""", _sent[2].Body);
    }

    [Fact]
    public async Task ReopenAsync_ClosedReport_PostsTheCommentAndReopens()
    {
        Report("closed");
        _routes[$"POST {CommentsUrl}"] = () => Json("""{"id":1}""", HttpStatusCode.Created);
        _routes[$"PATCH {IssueUrl}"] = () => Json("""{"number":10,"state":"open"}""");
        var reports = await SignedInAsync();

        await reports.ReopenAsync(Shown(), "The author posted the original thread.");

        Assert.Equal([$"GET {IssueUrl}", $"POST {CommentsUrl}", $"PATCH {IssueUrl}"], _sent.Select(sent => $"{sent.Method} {sent.Url}"));
        AssertJson("""{"state":"open"}""", _sent[2].Body);
    }

    [Fact]
    public async Task CloseAsync_RetryAfterACloseThatFailed_FindsItsCommentOnTheLastPage_AndOnlyCloses()
    {
        Report("open", comments: 101);
        _routes[$"GET {CommentsUrl}?per_page=100&page=2"] = () => Json("""[{"body":"The takedown does not hold.\r\n","user":{"login":"OctoCat"}}]""");
        _routes[$"PATCH {IssueUrl}"] = () => Json("""{"number":10,"state":"closed"}""");
        var reports = await SignedInAsync();

        await reports.CloseAsync(Shown(), IndexReportCloseReason.NotPlanned, "The takedown does not hold.");

        Assert.Equal([$"GET {IssueUrl}", $"GET {CommentsUrl}?per_page=100&page=2", $"PATCH {IssueUrl}"], _sent.Select(sent => $"{sent.Method} {sent.Url}"));
    }

    [Fact]
    public async Task CloseAsync_LastCommentOfSomebodyElse_PostsTheComment()
    {
        Report("open", comments: 3);
        _routes[$"GET {CommentsUrl}?per_page=100&page=1"] = () => Json("""[{"body":"The takedown does not hold.","user":{"login":"alice"}}]""");
        _routes[$"POST {CommentsUrl}"] = () => Json("""{"id":1}""", HttpStatusCode.Created);
        _routes[$"PATCH {IssueUrl}"] = () => Json("""{"number":10,"state":"closed"}""");
        var reports = await SignedInAsync();

        await reports.CloseAsync(Shown(), IndexReportCloseReason.Completed, "The takedown does not hold.");

        Assert.Contains(_sent, sent => sent.Method == "POST");
    }

    [Theory]
    [InlineData("closed", false)]
    [InlineData("open", true)]
    public async Task StateChange_ReportChangedSinceTheRead_RefusesAndWritesNothing(string state, bool reopen)
    {
        Report(state);
        var reports = await SignedInAsync();

        var refused = await Assert.ThrowsAsync<ReportRefusedException>(() => reopen
            ? reports.ReopenAsync(Shown(), "New evidence.")
            : reports.CloseAsync(Shown(), IndexReportCloseReason.Completed, "Done."));

        Assert.Equal(reopen ? ReportRefusal.Open : ReportRefusal.Closed, refused.Refusal);
        Assert.All(_sent, sent => Assert.Equal("GET", sent.Method));
    }

    [Theory]
    [InlineData("""{"installations":[{"id":1,"account":{"login":"octocat"},"permissions":{"issues":"write"}},{"id":2,"account":{"login":"ksamodding"},"permissions":{"issues":"write","contents":"write"}}]}""", true)]
    [InlineData("""{"installations":[{"id":2,"account":{"login":"KSAModding"},"permissions":{"issues":"read","contents":"write"}}]}""", false)]
    [InlineData("""{"installations":[{"id":2,"account":{"login":"KSAModding"},"permissions":{"contents":"write"}}]}""", false)]
    [InlineData("""{"installations":[{"id":1,"account":{"login":"octocat"},"permissions":{"issues":"write"}}]}""", false)]
    public async Task CanActAsync_AsksTheInstallationOnKSAModdingForTheIssuesPermission(string installations, bool canAct)
    {
        _routes[$"GET {InstallationsUrl}"] = () => Json(installations);
        var reports = await SignedInAsync();

        Assert.Equal(canAct, await reports.CanActAsync());
        var sent = Assert.Single(_sent);
        Assert.Equal(("GET", InstallationsUrl, "Bearer " + Token), (sent.Method, sent.Url, sent.Authorization));
    }

    [Fact]
    public async Task CanActAsync_CheckFails_OrSignedOut_IsNo()
    {
        _routes[$"GET {InstallationsUrl}"] = () => Json("""{"message":"Failed"}""", HttpStatusCode.InternalServerError);
        var failing = await SignedInAsync();
        var signedOut = new GitHubIndexReports(new GitHubSession(Http(), "Iv1.testclient", "borea-test"), Http());

        Assert.False(await failing.CanActAsync());
        Assert.False(await signedOut.CanActAsync());
        Assert.Single(_sent);
    }

    [Fact]
    public async Task EveryAction_AppWithoutTheIssuesPermission_IsForbidden_AndTheStateStays()
    {
        Report("open");
        _routes[$"POST {CommentsUrl}"] = () => Json("""{"message":"Resource not accessible by integration"}""", HttpStatusCode.Forbidden);
        var reports = await SignedInAsync();

        var comment = await Assert.ThrowsAsync<StewardException>(() => reports.CommentAsync(Shown(), "Please answer."));
        var close = await Assert.ThrowsAsync<StewardException>(() => reports.CloseAsync(Shown(), IndexReportCloseReason.Completed, "Done."));
        Report("closed");
        var reopen = await Assert.ThrowsAsync<StewardException>(() => reports.ReopenAsync(Shown(), "New evidence."));

        Assert.Equal((StewardFailure.Forbidden, StewardFailure.Forbidden, StewardFailure.Forbidden), (comment.Failure, close.Failure, reopen.Failure));
        Assert.Equal("Resource not accessible by integration", comment.Detail);
        Assert.DoesNotContain(_sent, sent => sent.Method == "PATCH");
    }

    [Fact]
    public async Task EveryAction_ReadRefusedWithTheToken_ReadsTheReportWithoutIt()
    {
        _routes[$"GET {IssueUrl}"] = () => Json("""{"message":"Resource not accessible by integration"}""", HttpStatusCode.Forbidden);
        _routes[$"GET {IssueUrl} anonymous"] = () => Json(IssueJson("open", 0, $"https://github.com/{Index}/issues/10"));
        _routes[$"POST {CommentsUrl}"] = () => Json("""{"id":1}""", HttpStatusCode.Created);
        var reports = await SignedInAsync();

        await reports.CommentAsync(Shown(), "Please answer.");

        Assert.Equal([("GET", "Bearer " + Token), ("GET", null), ("POST", "Bearer " + Token)], _sent.Select(sent => (sent.Method, sent.Authorization)));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "https://github.com/KSAModding/other/issues/4")]
    [InlineData(HttpStatusCode.NotFound, null)]
    [InlineData(HttpStatusCode.Gone, null)]
    public async Task EveryAction_ReportMovedOrGone_IsNotFound_AndWritesNothing(HttpStatusCode status, string? movedTo)
    {
        _routes[$"GET {IssueUrl}"] = () => status == HttpStatusCode.OK ? Json(IssueJson("open", 0, movedTo!)) : Json("""{"message":"Gone"}""", status);
        var reports = await SignedInAsync();

        var comment = await Assert.ThrowsAsync<StewardException>(() => reports.CommentAsync(Shown(), "Please answer."));
        var reopen = await Assert.ThrowsAsync<StewardException>(() => reports.ReopenAsync(Shown(), "New evidence."));

        Assert.Equal((StewardFailure.NotFound, StewardFailure.NotFound), (comment.Failure, reopen.Failure));
        Assert.All(_sent, sent => Assert.Equal("GET", sent.Method));
    }

    [Fact]
    public async Task EveryAction_WithoutAComment_OrOnAnotherRepository_SendsNothing()
    {
        var reports = await SignedInAsync();
        var elsewhere = Shown() with { Url = new Uri("https://github.com/KSAModding/Borea/issues/10") };

        await Assert.ThrowsAsync<ArgumentException>(() => reports.CommentAsync(Shown(), " "));
        await Assert.ThrowsAsync<ArgumentException>(() => reports.CloseAsync(Shown(), IndexReportCloseReason.Completed, ""));
        await Assert.ThrowsAsync<ArgumentException>(() => reports.ReopenAsync(elsewhere, "New evidence."));

        Assert.Empty(_sent);
    }

    private static IndexReport Shown() =>
        IndexReport.FromIssue(10, new Uri($"https://github.com/{Index}/issues/10"), "[Takedown] Harmful installer", "alice", Day, TakedownBody)!;

    /// <summary>Report #10 as GitHub has it now.</summary>
    private void Report(string state, int comments = 0) =>
        _routes[$"GET {IssueUrl}"] = () => Json(IssueJson(state, comments, $"https://github.com/{Index}/issues/10"));

    private static string IssueJson(string state, int comments, string htmlUrl) => JsonSerializer.Serialize(new
    {
        number = 10,
        title = "[Takedown] Harmful installer",
        html_url = htmlUrl,
        body = TakedownBody,
        state,
        state_reason = state == "closed" ? "not_planned" : (string?)null,
        comments,
        created_at = Day,
        updated_at = Day,
        user = new { login = "alice", type = "User" },
    });

    private static object ClosedIssue(int number, string title, string body, string reason, DateTimeOffset closed) => new
    {
        number,
        title,
        html_url = $"https://github.com/{Index}/issues/{number}",
        body,
        state = "closed",
        state_reason = reason,
        closed_at = closed,
        created_at = Day,
        updated_at = closed,
        user = new { login = "alice", type = "User" },
    };

    private static void AssertJson(string expected, string? actual)
    {
        Assert.NotNull(actual);
        using var want = JsonDocument.Parse(expected);
        using var got = JsonDocument.Parse(actual);
        Assert.True(JsonElement.DeepEquals(want.RootElement, got.RootElement), actual);
    }
}
