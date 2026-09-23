using System.Net;
using System.Text;
using System.Text.Json;
using Borea.Core.GitHub;
using Borea.Core.Listings;
using Borea.Network.GitHub;
using Borea.Network.Listings;

namespace Borea.Network.Tests.Listings;

public sealed class ListingPublisherTests
{
    private const string Api = "https://api.github.com";
    private const string Upstream = Api + "/repos/KSAModding/content-index";
    private const string Fork = Api + "/repos/octocat/content-index";
    private const string Installations = Api + "/user/installations?per_page=100&page=1";
    private const string InstalledRepositories = Api + "/user/installations/7/repositories?per_page=100&page=1";
    private const string Compare = Upstream + "/compare/main...octocat:content-index:main?per_page=1";
    private const string MainSha = "4f1c2a9";
    private const string BaseSha = "0054ba6";
    private const string Token = "ghu_secret";
    private const string Text = "spec_version = 1\nid = \"MyMod\"\nname = \"My Mod\"\n";

    private const string ForkJson = """
        { "id": 42, "full_name": "octocat/content-index", "fork": true, "default_branch": "main",
          "owner": { "id": 1, "login": "octocat" }, "parent": { "full_name": "KSAModding/content-index" } }
        """;

    private const string InstallationsJson = """
        { "total_count": 2, "installations": [
            { "id": 3, "account": { "login": "KSAModding", "id": 50 } },
            { "id": 7, "account": { "login": "OctoCat", "id": 1 } } ] }
        """;

    private const string PullJson = """{ "number": 90, "html_url": "https://github.com/KSAModding/content-index/pull/90", "state": "open" }""";

    // The verdict comments of content-index pull requests #87, #86 and #49, as the bot wrote them.
    private const string MergingComment = "<!-- content-index:verdict -->\nValidated.\n\nNotes:\n- `release`: listings/OhMyStars.toml: the latest release 1.0.3 stamps cleanly\n\nThis pull request merges on its own once the checks finish. The watcher stamps the first release within about ten minutes after the merge, and the snapshot follows.\n\n[The validation run](https://github.com/KSAModding/content-index/actions/runs/35433439925)";

    private const string StewardComment = "<!-- content-index:verdict -->\nValidated, and ownership is not verified, so a steward decides.\n\nNotes:\n- `release`: listings/StarMap.toml: the latest release 0.4.7 stamps cleanly\n\nMaximilian-Nesslauer did not prove control of StarMapLoader/StarMap: no matching owner, no ksa-index-maximilian-nesslauer topic, and no .github/ksa-content-index.toml.\n\nThe proof is something only you can put on the release repository, which is what says you agree to it being indexed. Either set the topic `ksa-index-<your-github-username>` on it, or commit `.github/ksa-content-index.toml` naming your username. For a SpaceDock host, set your GitHub repository as the mod's source code link on SpaceDock, and put the proof on that repository.\n\n[The validation run](https://github.com/KSAModding/content-index/actions/runs/35432243938)";

    private const string RejectedComment = "<!-- content-index:verdict -->\nThe validation rejected this change.\n\n- `schema`: listings/ksamods_gg_submit_test_2.toml: the document: Additional properties are not allowed ('metadata' was unexpected)\n\n[The validation run](https://github.com/KSAModding/content-index/actions/runs/34032571484)";

    private const string NoVerdictComment = "<!-- content-index:verdict -->\nThe validation could not reach a verdict, so nothing is decided yet.\n\n- `validate`: the validation run left no verdict\n\nPush a fix and the checks run again.";

    private readonly StepTimeProvider _time = new();
    private readonly List<Sent> _sent = [];
    private readonly Dictionary<string, Queue<Func<HttpResponseMessage>>> _routes = new(StringComparer.Ordinal);
    private Func<HttpRequestMessage, HttpResponseMessage?>? _fallback;

    public ListingPublisherTests()
    {
        On("POST", "https://github.com/login/device/code", () => Json("""{"device_code":"d","user_code":"WDJB-MJHT","verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}"""));
        On("POST", "https://github.com/login/oauth/access_token", () => Json($$"""{"access_token":"{{Token}}","token_type":"bearer","scope":"","expires_in":28800}"""));
        On("GET", Api + "/user", () => Json("""{"login":"octocat","id":1}"""));
        On("GET", Upstream + "/pulls?state=open&per_page=100&page=1", () => Json("[]"));
        OnCompare(MainSha);
    }

    [Fact]
    public async Task PublishAsync_NewListing_FindsTheForkBranchesCommitsAndOpensThePullRequest()
    {
        OnInstalledFork();
        On("POST", Upstream + "/pulls", () => Json(PullJson, HttpStatusCode.Created));
        var (publisher, _) = await SignedInAsync();
        var steps = new List<ListingPublishStep>();

        var pullRequest = await publisher.PublishAsync(New(), new ListProgress<ListingPublishStep>(steps));

        Assert.Equal(new ListingPullRequest(90, new Uri("https://github.com/KSAModding/content-index/pull/90"), ListingPublishOutcome.Opened), pullRequest);
        Assert.Equal(
            [
                "GET " + Installations,
                "GET " + InstalledRepositories,
                "GET " + Fork,
                "GET " + Upstream + "/pulls?state=open&per_page=100&page=1",
                "GET " + Compare,
                "GET " + Upstream + "/contents/listings/MyMod.toml?ref=" + MainSha,
                "GET " + Fork + "/git/ref/heads/listing-mymod",
                "POST " + Fork + "/git/refs",
                "PUT " + Fork + "/contents/listings/MyMod.toml",
                "POST " + Upstream + "/pulls",
            ],
            _sent.Select(sent => sent.Line));
        Assert.All(_sent, sent => Assert.Equal("Bearer " + Token, sent.Authorization));
        Assert.Equal($$"""{"ref":"refs/heads/listing-mymod","sha":"{{MainSha}}"}""", Body("POST", Fork + "/git/refs"));
        Assert.Equal(JsonSerializer.Serialize(new { message = "List MyMod", content = Base64(Text), branch = "listing-mymod" }), Body("PUT", Fork + "/contents/listings/MyMod.toml"));
        Assert.Equal("""{"title":"List My Mod","head":"octocat:listing-mymod","base":"main","body":"Lists My Mod.","maintainer_can_modify":true}""", Body("POST", Upstream + "/pulls"));
        Assert.Equal([ListingPublishStep.Fork, ListingPublishStep.FindPullRequest, ListingPublishStep.Branch, ListingPublishStep.Commit, ListingPublishStep.PullRequest], steps);
    }

    [Fact]
    public async Task PublishAsync_Change_CommitsOverTheListedFileWithItsSha()
    {
        OnInstalledFork();
        On("GET", Upstream + "/contents/listings/MyMod.toml?ref=" + MainSha, () => Json(Content("id = \"MyMod\"\n", "b10b5a")));
        On("POST", Upstream + "/pulls", () => Json(PullJson, HttpStatusCode.Created));
        var (publisher, _) = await SignedInAsync();

        await publisher.PublishAsync(New() with { IsEdit = true });

        Assert.Equal(JsonSerializer.Serialize(new { message = "Update MyMod", content = Base64(Text), branch = "listing-mymod", sha = "b10b5a" }), Body("PUT", Fork + "/contents/listings/MyMod.toml"));
        Assert.Equal("""{"title":"Update My Mod","head":"octocat:listing-mymod","base":"main","body":"Updates the listing of My Mod.","maintainer_can_modify":true}""", Body("POST", Upstream + "/pulls"));
    }

    [Fact]
    public async Task PublishAsync_ChangeThatChangesNothing_StopsBeforeAnyWrite()
    {
        OnInstalledFork();
        On("GET", Upstream + "/contents/listings/MyMod.toml?ref=" + MainSha, () => Json(Content(Text, "b10b5a")));
        var (publisher, _) = await SignedInAsync();

        var failure = await Assert.ThrowsAsync<ListingPublishException>(() => publisher.PublishAsync(New() with { IsEdit = true }));

        Assert.Equal(ListingPublishFailure.NoChange, failure.Failure);
        Assert.All(_sent, sent => Assert.Equal("GET", sent.Method));
    }

    [Fact]
    public async Task PublishAsync_ForkUnderAnotherName_IsFoundOnTheInstallation()
    {
        On("GET", Installations, () => Json(InstallationsJson));
        On("GET", InstalledRepositories, () => Json("""
            { "total_count": 3, "repositories": [
                { "full_name": "octocat/MyMod", "fork": false, "default_branch": "main" },
                { "full_name": "octocat/other-fork", "fork": true, "default_branch": "main" },
                { "full_name": "octocat/ksa-index-fork", "fork": true, "default_branch": "main" } ] }
            """));
        On("GET", Api + "/repos/octocat/other-fork", () => Json("""{ "full_name": "octocat/other-fork", "fork": true, "default_branch": "main", "parent": { "full_name": "someone/content-index" } }"""));
        On("GET", Api + "/repos/octocat/ksa-index-fork", () => Json(ForkJson.Replace("octocat/content-index", "octocat/ksa-index-fork", StringComparison.Ordinal)));
        OnCompare(MainSha, url: Upstream + "/compare/main...octocat:ksa-index-fork:main?per_page=1");
        On("POST", Api + "/repos/octocat/ksa-index-fork/git/refs", () => Json("{}", HttpStatusCode.Created));
        On("PUT", Api + "/repos/octocat/ksa-index-fork/contents/listings/MyMod.toml", () => Json("{}", HttpStatusCode.Created));
        On("POST", Upstream + "/pulls", () => Json(PullJson, HttpStatusCode.Created));
        var (publisher, _) = await SignedInAsync();

        await publisher.PublishAsync(New());

        Assert.DoesNotContain(_sent, sent => sent.Url == Api + "/repos/octocat/MyMod" || sent.Url == Fork);
        Assert.Contains(_sent, sent => sent.Line == "PUT " + Api + "/repos/octocat/ksa-index-fork/contents/listings/MyMod.toml");
        Assert.Contains("\"head\":\"octocat:listing-mymod\"", Body("POST", Upstream + "/pulls"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PublishAsync_ForkWithoutTheApp_NamesTheForkAndWritesNothing(bool installedOnOtherRepositories)
    {
        On("GET", Installations, () => Json(installedOnOtherRepositories ? InstallationsJson : """{ "total_count": 0, "installations": [] }"""));
        On("GET", InstalledRepositories, () => Json("""{ "total_count": 1, "repositories": [{ "full_name": "octocat/MyMod", "fork": false }] }"""));
        On("GET", Fork, () => Json(ForkJson));
        var (publisher, _) = await SignedInAsync();

        var failure = await Assert.ThrowsAsync<ListingPublishException>(() => publisher.PublishAsync(New()));

        Assert.Equal(ListingPublishFailure.AppNotOnFork, failure.Failure);
        Assert.Equal(ListingPublishStep.Fork, failure.Step);
        Assert.Equal("octocat/content-index", failure.Detail);
        Assert.Equal(42, failure.RepositoryId);
        Assert.Null(_sent.Single(sent => sent.Url == Fork).Authorization);
        Assert.All(_sent, sent => Assert.Equal("GET", sent.Method));
        Assert.DoesNotContain(_sent, sent => sent.Url.StartsWith(Upstream, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{ "full_name": "octocat/content-index", "fork": false, "default_branch": "main" }""")]
    [InlineData("""{ "full_name": "octocat/content-index", "fork": true, "default_branch": "main", "parent": { "full_name": "someone/content-index" } }""")]
    public async Task PublishAsync_NoForkOfContentIndex_AsksForOne(string? named)
    {
        On("GET", Installations, () => Json("""{ "total_count": 0, "installations": [] }"""));
        if (named is not null)
            On("GET", Fork, () => Json(named));
        var (publisher, _) = await SignedInAsync();

        var failure = await Assert.ThrowsAsync<ListingPublishException>(() => publisher.PublishAsync(New()));

        Assert.Equal(ListingPublishFailure.NoFork, failure.Failure);
        Assert.Equal(ListingPublishStep.Fork, failure.Step);
        Assert.Null(_sent.Single(sent => sent.Url == Fork).Authorization);
        Assert.Equal(["GET " + Installations, "GET " + Fork], _sent.Select(sent => sent.Line));
    }

    [Fact]
    public async Task PublishAsync_ForkCheckWithoutTokenRateLimited_SaysWhenToTryAgain()
    {
        var reset = new DateTimeOffset(2026, 9, 19, 13, 0, 0, TimeSpan.Zero);
        On("GET", Installations, () => Json("""{ "total_count": 0, "installations": [] }"""));
        On("GET", Fork, () => RateLimited(reset));
        var (publisher, session) = await SignedInAsync();

        var failure = await Assert.ThrowsAsync<ListingPublishException>(() => publisher.PublishAsync(New()));

        Assert.Equal(ListingPublishFailure.RateLimited, failure.Failure);
        Assert.Equal(ListingPublishStep.Fork, failure.Step);
        Assert.Equal(reset, failure.RetryAt);
        Assert.Equal(GitHubSessionStatus.SignedIn, session.State.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("f0cc000")]
    public async Task PublishAsync_ForkBehindContentIndex_BranchesAtTheMergeBaseAndKeepsItsOwnCommitsOut(string? ownCommit)
    {
        OnInstalledFork();
        OnCompare(BaseSha, ownCommit);
        On("POST", Upstream + "/pulls", () => Json(PullJson, HttpStatusCode.Created));
        var (publisher, _) = await SignedInAsync();

        await publisher.PublishAsync(New());

        Assert.Equal($$"""{"ref":"refs/heads/listing-mymod","sha":"{{BaseSha}}"}""", Body("POST", Fork + "/git/refs"));
        Assert.Equal(JsonSerializer.Serialize(new { message = "List MyMod", content = Base64(Text), branch = "listing-mymod" }), Body("PUT", Fork + "/contents/listings/MyMod.toml"));
        Assert.Contains("\"head\":\"octocat:listing-mymod\"", Body("POST", Upstream + "/pulls"), StringComparison.Ordinal);
        Assert.Equal(["POST " + Fork + "/git/refs", "PUT " + Fork + "/contents/listings/MyMod.toml", "POST " + Upstream + "/pulls"], _sent.Where(sent => sent.Method != "GET").Select(sent => sent.Line));
        Assert.DoesNotContain(_sent, sent => ownCommit is not null && sent.Body?.Contains(ownCommit, StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task PublishAsync_ChangeOfAFileUnchangedSinceTheMergeBase_CommitsOverItAtTheMergeBase()
    {
        OnInstalledFork();
        OnCompare(BaseSha);
        On("GET", Upstream + "/contents/listings/MyMod.toml?ref=" + MainSha, () => Json(Content("id = \"MyMod\"\n", "b10b5a")));
        On("GET", Upstream + "/contents/listings/MyMod.toml?ref=" + BaseSha, () => Json(Content("id = \"MyMod\"\n", "b10b5a")));
        On("POST", Upstream + "/pulls", () => Json(PullJson, HttpStatusCode.Created));
        var (publisher, _) = await SignedInAsync();

        await publisher.PublishAsync(New() with { IsEdit = true });

        Assert.Equal($$"""{"ref":"refs/heads/listing-mymod","sha":"{{BaseSha}}"}""", Body("POST", Fork + "/git/refs"));
        Assert.Equal(JsonSerializer.Serialize(new { message = "Update MyMod", content = Base64(Text), branch = "listing-mymod", sha = "b10b5a" }), Body("PUT", Fork + "/contents/listings/MyMod.toml"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PublishAsync_ChangeOfAFileChangedAfterTheMergeBase_AsksForASyncAndWritesNothing(bool atMergeBase)
    {
        OnInstalledFork();
        OnCompare(BaseSha);
        On("GET", Upstream + "/contents/listings/MyMod.toml?ref=" + MainSha, () => Json(Content("id = \"MyMod\"\n", "b10b5a")));
        if (atMergeBase)
            On("GET", Upstream + "/contents/listings/MyMod.toml?ref=" + BaseSha, () => Json(Content("id = \"Old\"\n", "01d5a")));
        var (publisher, _) = await SignedInAsync();

        var failure = await Assert.ThrowsAsync<ListingPublishException>(() => publisher.PublishAsync(New() with { IsEdit = true }));

        Assert.Equal(ListingPublishFailure.ForkNeedsSync, failure.Failure);
        Assert.Equal(ListingPublishStep.Branch, failure.Step);
        Assert.Equal("octocat/content-index", failure.Detail);
        Assert.Contains(_sent, sent => sent.Line == "GET " + Upstream + "/contents/listings/MyMod.toml?ref=" + BaseSha);
        Assert.All(_sent, sent => Assert.Equal("GET", sent.Method));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("GET")]
    public async Task PublishAsync_BranchForbidden_AsksForASyncAndKeepsGitHubsReason(string method)
    {
        OnInstalledFork();
        On(method, method == "GET" ? Fork + "/git/ref/heads/listing-mymod" : Fork + "/git/refs", () => Json("""{"message":"Resource not accessible by integration"}""", HttpStatusCode.Forbidden));
        var (publisher, _) = await SignedInAsync();

        var failure = await Assert.ThrowsAsync<ListingPublishException>(() => publisher.PublishAsync(New()));

        Assert.Equal(ListingPublishFailure.ForkNeedsSync, failure.Failure);
        Assert.Equal(ListingPublishStep.Branch, failure.Step);
        Assert.Equal("octocat/content-index", failure.Detail);
        Assert.Equal("Branch failed: ForkNeedsSync, octocat/content-index (Forbidden, Resource not accessible by integration)", failure.Message);
        Assert.DoesNotContain(_sent, sent => sent.Method == "PUT" || sent.Url.EndsWith("/merge-upstream", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublishAsync_BranchRefused_KeepsGitHubsReasonWithoutMergingUpstream()
    {
        OnInstalledFork();
        On("POST", Fork + "/git/refs", () => Json("""{"message":"Object does not exist"}""", HttpStatusCode.UnprocessableEntity));
        var (publisher, _) = await SignedInAsync();

        var failure = await Assert.ThrowsAsync<ListingPublishException>(() => publisher.PublishAsync(New()));

        Assert.Equal(ListingPublishFailure.Refused, failure.Failure);
        Assert.Equal(ListingPublishStep.Branch, failure.Step);
        Assert.Equal("Object does not exist", failure.Detail);
        Assert.Single(_sent, sent => sent.Line == "POST " + Fork + "/git/refs");
        Assert.DoesNotContain(_sent, sent => sent.Url.EndsWith("/merge-upstream", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublishAsync_TakenBranchNames_AddsTheNextFreeNumber()
    {
        OnInstalledFork();
        On("GET", Fork + "/git/ref/heads/listing-mymod", () => Json("""{"object":{"sha":"x"}}"""));
        On("GET", Fork + "/git/ref/heads/listing-mymod-2", () => Json("""{"object":{"sha":"y"}}"""));
        On("POST", Fork + "/git/refs",
            () => Json("""{"message":"Reference already exists"}""", HttpStatusCode.UnprocessableEntity),
            () => Json("{}", HttpStatusCode.Created));
        On("POST", Upstream + "/pulls", () => Json(PullJson, HttpStatusCode.Created));
        var (publisher, _) = await SignedInAsync();

        await publisher.PublishAsync(New());

        Assert.Equal(
            [$$"""{"ref":"refs/heads/listing-mymod-3","sha":"{{MainSha}}"}""", $$"""{"ref":"refs/heads/listing-mymod-4","sha":"{{MainSha}}"}"""],
            _sent.Where(sent => sent.Line == "POST " + Fork + "/git/refs").Select(sent => sent.Body));
        Assert.Contains("\"head\":\"octocat:listing-mymod-4\"", Body("POST", Upstream + "/pulls"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishAsync_OpenPullRequestOfTheAuthor_CommitsToItsBranchInstead()
    {
        OnInstalledFork();
        On("GET", Upstream + "/pulls?state=open&per_page=100&page=1", () => Json("""
            [
              { "number": 80, "html_url": "https://github.com/KSAModding/content-index/pull/80", "user": { "login": "someone" },
                "head": { "ref": "patch-1", "repo": { "full_name": "someone/content-index" } } },
              { "number": 81, "html_url": "https://github.com/KSAModding/content-index/pull/81", "user": { "login": "OctoCat" },
                "head": { "ref": "other", "repo": { "full_name": "octocat/content-index" } } },
              { "number": 82, "html_url": "https://github.com/KSAModding/content-index/pull/82", "user": { "login": "octocat" },
                "head": { "ref": "patch-2", "repo": { "full_name": "octocat/content-index" } } }
            ]
            """));
        On("GET", Upstream + "/pulls/81/files?per_page=100&page=1", () => Json("""[{ "filename": "listings/Other.toml" }]"""));
        On("GET", Upstream + "/pulls/82/files?per_page=100&page=1", () => Json("""[{ "filename": "listings/MyMod.toml" }]"""));
        On("GET", Fork + "/contents/listings/MyMod.toml?ref=patch-2", () => Json(Content("id = \"MyMod\"\n", "c0ffee")));
        On("PUT", Fork + "/contents/listings/MyMod.toml", () => Json("{}"));
        var (publisher, _) = await SignedInAsync();

        var pullRequest = await publisher.PublishAsync(New());

        Assert.Equal(new ListingPullRequest(82, new Uri("https://github.com/KSAModding/content-index/pull/82"), ListingPublishOutcome.Updated), pullRequest);
        Assert.Equal(JsonSerializer.Serialize(new { message = "List MyMod", content = Base64(Text), branch = "patch-2", sha = "c0ffee" }), Body("PUT", Fork + "/contents/listings/MyMod.toml"));
        Assert.DoesNotContain(_sent, sent => sent.Url.EndsWith("/pulls/80/files?per_page=100&page=1", StringComparison.Ordinal));
        Assert.DoesNotContain(_sent, sent => sent.Url.EndsWith("/git/refs", StringComparison.Ordinal) || sent.Line == "POST " + Upstream + "/pulls");
    }

    [Theory]
    [InlineData("someone-else/content-index")]
    [InlineData("KSAModding/content-index")]
    public async Task PublishAsync_OpenPullRequestFromAnotherRepository_NamesItAndWritesNothing(string head)
    {
        OnInstalledFork();
        On("GET", Upstream + "/pulls?state=open&per_page=100&page=1", () => Json($$"""
            [ { "number": 82, "html_url": "https://github.com/KSAModding/content-index/pull/82", "user": { "login": "octocat" },
                "head": { "ref": "listing-mymod", "repo": { "full_name": "{{head}}" } } } ]
            """));
        On("GET", Upstream + "/pulls/82/files?per_page=100&page=1", () => Json("""[{ "filename": "listings/MyMod.toml" }]"""));
        var (publisher, _) = await SignedInAsync();

        var failure = await Assert.ThrowsAsync<ListingPublishException>(() => publisher.PublishAsync(New()));

        Assert.Equal(ListingPublishFailure.PullRequestNotOnFork, failure.Failure);
        Assert.Equal(ListingPublishStep.FindPullRequest, failure.Step);
        Assert.Equal("82", failure.Detail);
        Assert.All(_sent, sent => Assert.Equal("GET", sent.Method));
        Assert.DoesNotContain(_sent, sent => sent.Url.StartsWith(Api + "/repos/" + head + "/", StringComparison.OrdinalIgnoreCase) && !sent.Url.StartsWith(Upstream + "/pulls", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublishAsync_InstalledForkOfAFork_IsNotUsed()
    {
        On("GET", Installations, () => Json(InstallationsJson));
        On("GET", InstalledRepositories, () => Json("""
            { "total_count": 1, "repositories": [{ "full_name": "octocat/content-index", "fork": true, "default_branch": "main" }] }
            """));
        On("GET", Fork, () => Json("""
            { "full_name": "octocat/content-index", "fork": true, "default_branch": "main",
              "parent": { "full_name": "someone/content-index" }, "source": { "full_name": "KSAModding/content-index" } }
            """));
        var (publisher, _) = await SignedInAsync();

        var failure = await Assert.ThrowsAsync<ListingPublishException>(() => publisher.PublishAsync(New()));

        Assert.Equal(ListingPublishFailure.NoFork, failure.Failure);
        Assert.All(_sent, sent => Assert.Equal("GET", sent.Method));
        Assert.DoesNotContain(_sent, sent => sent.Url.StartsWith(Upstream, StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublishAsync_OpenPullRequestHasTheFileAlready_CommitsNothing()
    {
        OnInstalledFork();
        On("GET", Upstream + "/pulls?state=open&per_page=100&page=1", () => Json("""
            [ { "number": 82, "html_url": "https://github.com/KSAModding/content-index/pull/82", "user": { "login": "octocat" },
                "head": { "ref": "listing-mymod", "repo": { "full_name": "octocat/content-index" } } } ]
            """));
        On("GET", Upstream + "/pulls/82/files?per_page=100&page=1", () => Json("""[{ "filename": "listings/MyMod.toml" }]"""));
        On("GET", Fork + "/contents/listings/MyMod.toml?ref=listing-mymod", () => Json(Content(Text, "c0ffee")));
        var (publisher, _) = await SignedInAsync();

        var pullRequest = await publisher.PublishAsync(New());

        Assert.Equal(ListingPublishOutcome.Unchanged, pullRequest.Outcome);
        Assert.All(_sent, sent => Assert.Equal("GET", sent.Method));
    }

    [Fact]
    public async Task PublishAsync_TokenRefused_SignsOut()
    {
        On("GET", Installations, () => Json("""{"message":"Bad credentials"}""", HttpStatusCode.Unauthorized));
        var (publisher, session) = await SignedInAsync();

        var failure = await Assert.ThrowsAsync<ListingPublishException>(() => publisher.PublishAsync(New()));

        Assert.Equal(ListingPublishFailure.SignedOut, failure.Failure);
        Assert.Equal(GitHubSessionStatus.SignedOut, session.State.Status);
        Assert.DoesNotContain(Token, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishAsync_SignedOut_SendsNothing()
    {
        var (publisher, session) = await SignedInAsync();
        session.SignOut();

        var failure = await Assert.ThrowsAsync<ListingPublishException>(() => publisher.PublishAsync(New()));

        Assert.Equal(ListingPublishFailure.SignedOut, failure.Failure);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task PublishAsync_RateLimit_SaysWhenToTryAgain()
    {
        var reset = new DateTimeOffset(2026, 9, 19, 13, 0, 0, TimeSpan.Zero);
        On("GET", Installations, () => RateLimited(reset));
        var (publisher, _) = await SignedInAsync();

        var failure = await Assert.ThrowsAsync<ListingPublishException>(() => publisher.PublishAsync(New()));

        Assert.Equal(ListingPublishFailure.RateLimited, failure.Failure);
        Assert.Equal(ListingPublishStep.Fork, failure.Step);
        Assert.Equal(reset, failure.RetryAt);
    }

    [Theory]
    [InlineData("30", 30)]
    [InlineData(null, 60)]
    public async Task PublishAsync_SecondaryRateLimit_WaitsAsGitHubSays(string? retryAfter, int seconds)
    {
        OnInstalledFork();
        On("POST", Fork + "/git/refs", () =>
        {
            var response = Json("""{"message":"You have exceeded a secondary rate limit. Please wait a few minutes before you try again."}""", HttpStatusCode.Forbidden);
            if (retryAfter is not null)
                response.Headers.Add("Retry-After", retryAfter);
            return response;
        });
        var (publisher, _) = await SignedInAsync();

        var failure = await Assert.ThrowsAsync<ListingPublishException>(() => publisher.PublishAsync(New()));

        Assert.Equal(ListingPublishFailure.RateLimited, failure.Failure);
        Assert.Equal(_time.GetUtcNow() + TimeSpan.FromSeconds(seconds), failure.RetryAt);
    }

    [Fact]
    public async Task PublishAsync_Forbidden_NamesTheStep()
    {
        OnInstalledFork();
        On("PUT", Fork + "/contents/listings/MyMod.toml", () => Json("""{"message":"Resource not accessible by integration"}""", HttpStatusCode.Forbidden));
        var (publisher, _) = await SignedInAsync();

        var failure = await Assert.ThrowsAsync<ListingPublishException>(() => publisher.PublishAsync(New()));

        Assert.Equal(ListingPublishFailure.Forbidden, failure.Failure);
        Assert.Equal(ListingPublishStep.Commit, failure.Step);
        Assert.Null(failure.RetryAt);
    }

    [Fact]
    public async Task PublishAsync_PullRequestRefused_KeepsGitHubsReason()
    {
        OnInstalledFork();
        On("POST", Upstream + "/pulls", () => Json("""{"message":"Validation Failed","errors":[{"resource":"PullRequest","code":"custom","message":"A pull request already exists for octocat:listing-mymod."}]}""", HttpStatusCode.UnprocessableEntity));
        var (publisher, _) = await SignedInAsync();

        var failure = await Assert.ThrowsAsync<ListingPublishException>(() => publisher.PublishAsync(New()));

        Assert.Equal(ListingPublishFailure.Refused, failure.Failure);
        Assert.Equal(ListingPublishStep.PullRequest, failure.Step);
        Assert.Equal("Validation Failed. A pull request already exists for octocat:listing-mymod.", failure.Detail);
    }

    [Fact]
    public async Task PublishAsync_UpstreamMissing_IsNotFoundAtTheBranch()
    {
        OnInstalledFork();
        On("GET", Compare, () => Json("""{"message":"Not Found"}""", HttpStatusCode.NotFound));
        var (publisher, _) = await SignedInAsync();

        var failure = await Assert.ThrowsAsync<ListingPublishException>(() => publisher.PublishAsync(New()));

        Assert.Equal(ListingPublishFailure.NotFound, failure.Failure);
        Assert.Equal(ListingPublishStep.Branch, failure.Step);
        Assert.All(_sent, sent => Assert.Equal("GET", sent.Method));
    }

    [Fact]
    public async Task PublishAsync_NetworkError_SaysSo()
    {
        _fallback = request => request.RequestUri!.AbsoluteUri == Installations ? throw new HttpRequestException("offline") : null;
        var (publisher, _) = await SignedInAsync();

        var failure = await Assert.ThrowsAsync<ListingPublishException>(() => publisher.PublishAsync(New()));

        Assert.Equal(ListingPublishFailure.NetworkError, failure.Failure);
        Assert.Equal(ListingPublishStep.Fork, failure.Step);
    }

    [Fact]
    public async Task PublishAsync_Cancelled_Stops()
    {
        OnInstalledFork();
        var (publisher, _) = await SignedInAsync();
        using var cancel = new CancellationTokenSource();
        _fallback = request =>
        {
            if (request.RequestUri!.AbsoluteUri == InstalledRepositories)
                cancel.Cancel();
            return null;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.PublishAsync(New(), cancellationToken: cancel.Token));

        Assert.Equal(["GET " + Installations, "GET " + InstalledRepositories], _sent.Select(sent => sent.Line));
    }

    [Fact]
    public async Task CheckOwnershipAsync_RepositoryOfTheAuthor_ProvesItByTheOwner()
    {
        On("GET", Api + "/repos/octocat/MyMod", () => Json(Repository("octocat/MyMod", ownerId: 1)));
        var (publisher, _) = await SignedInAsync();

        var ownership = await publisher.CheckOwnershipAsync(Draft("octocat/MyMod"), null);

        Assert.Equal(new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.Owner, Repository: "octocat/MyMod"), ownership);
        Assert.DoesNotContain(_sent, sent => sent.Url.EndsWith("/topics", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CheckOwnershipAsync_OrganizationRepositoryWithTheTopic_ProvesItByTheTopic()
    {
        On("GET", Api + "/repos/Studio/MyMod", () => Json(Repository("Studio/MyMod", ownerId: 99)));
        On("GET", Api + "/repos/Studio/MyMod/topics", () => Json("""{"names":["ksa","ksa-index-octocat"]}"""));
        var (publisher, _) = await SignedInAsync();

        var ownership = await publisher.CheckOwnershipAsync(Draft("Studio/MyMod"), null);

        Assert.Equal(ListingOwnershipProof.Topic, ownership.Proof);
    }

    [Fact]
    public async Task CheckOwnershipAsync_ReadsTheModRepositoryWithoutTheToken()
    {
        On("GET", Api + "/repos/Studio/MyMod", () => Json(Repository("Studio/MyMod", ownerId: 99)));
        On("GET", Api + "/repos/Studio/MyMod/topics", () => Json("""{"names":[]}"""));
        On("GET", Api + "/repos/Studio/MyMod/contents/.github/ksa-content-index.toml", () => Json(Content("login = \"octocat\"\n", "m1")));
        var (publisher, _) = await SignedInAsync();

        var ownership = await publisher.CheckOwnershipAsync(Draft("Studio/MyMod"), null);

        Assert.Equal(ListingOwnershipProof.MarkerFile, ownership.Proof);
        Assert.Equal(
            ["GET " + Api + "/user", "GET " + Api + "/repos/Studio/MyMod", "GET " + Api + "/repos/Studio/MyMod/topics", "GET " + Api + "/repos/Studio/MyMod/contents/.github/ksa-content-index.toml"],
            _sent.Take(4).Select(sent => sent.Line));
        Assert.Equal("Bearer " + Token, _sent[0].Authorization);
        Assert.All(_sent.Skip(1).Take(3), sent => Assert.Null(sent.Authorization));
        Assert.All(_sent.Skip(4), sent => Assert.Equal("Bearer " + Token, sent.Authorization));
    }

    [Theory]
    [InlineData("login = \"OctoCat\"\nid = \"mymod\"\n", true)]
    [InlineData("account = \"octocat\"\n", true)]
    [InlineData("login = \"octocat\"\nlisting = \"Other\"\n", false)]
    [InlineData("login = \"someone\"\nid = \"MyMod\"\n", false)]
    public async Task CheckOwnershipAsync_MarkerFile_ProvesItWhenItNamesTheLoginAndTheListing(string marker, bool verified)
    {
        On("GET", Api + "/repos/Studio/MyMod", () => Json(Repository("Studio/MyMod", ownerId: 99)));
        On("GET", Api + "/repos/Studio/MyMod/topics", () => Json("""{"names":[]}"""));
        On("GET", Api + "/repos/Studio/MyMod/contents/.github/ksa-content-index.toml", () => Json(Content(marker, "m1")));
        var (publisher, _) = await SignedInAsync();

        var ownership = await publisher.CheckOwnershipAsync(Draft("Studio/MyMod"), null);

        Assert.Equal(
            verified
                ? new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.MarkerFile, Repository: "Studio/MyMod")
                : new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.NoProof, Repository: "Studio/MyMod"),
            ownership);
    }

    [Fact]
    public async Task CheckOwnershipAsync_NoProof_NamesTheRepositoryForTheTopic()
    {
        On("GET", Api + "/repos/Studio/MyMod", () => Json(Repository("Studio/MyMod", ownerId: 99)));
        On("GET", Api + "/repos/Studio/MyMod/topics", () => Json("""{"names":["ksa-index"]}"""));
        var (publisher, _) = await SignedInAsync();

        var ownership = await publisher.CheckOwnershipAsync(Draft("Studio/MyMod"), null);

        Assert.Equal(new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.NoProof, Repository: "Studio/MyMod"), ownership);
        Assert.Equal(1, _sent.Count(sent => sent.Url.EndsWith("/contents/.github/ksa-content-index.toml", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task CheckOwnershipAsync_ForkOrMissingRepository_SaysWhichOne()
    {
        On("GET", Api + "/repos/octocat/MyMod", () => Json(Repository("octocat/MyMod", ownerId: 1, fork: true)));
        var (publisher, _) = await SignedInAsync();

        var fork = await publisher.CheckOwnershipAsync(Draft("octocat/MyMod"), null);
        var missing = await publisher.CheckOwnershipAsync(Draft("octocat/Gone"), null);

        Assert.Equal(ListingOwnershipProblem.RepositoryFork, fork.Problem);
        Assert.Equal(new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.RepositoryMissing, Repository: "octocat/Gone"), missing);
    }

    [Fact]
    public async Task CheckOwnershipAsync_RepositoryUnavailableForLegalReasons_CouldNotEvaluate()
    {
        On("GET", Api + "/repos/octocat/MyMod", () => Json("""{"message":"Repository access blocked"}""", HttpStatusCode.UnavailableForLegalReasons));
        var (publisher, _) = await SignedInAsync();

        var ownership = await publisher.CheckOwnershipAsync(Draft("octocat/MyMod"), null);

        Assert.Equal(ListingOwnership.Unknown, ownership);
    }

    [Fact]
    public async Task CheckOwnershipAsync_RenamedRepository_FollowsTheRedirectAndNamesTheNewName()
    {
        On("GET", Api + "/repos/octocat/OldName", () => Redirect(Api + "/repositories/42"));
        On("GET", Api + "/repositories/42", () => Json(Repository("octocat/NewName", ownerId: 1)));
        var (publisher, _) = await SignedInAsync();

        var ownership = await publisher.CheckOwnershipAsync(Draft("octocat/OldName"), null);

        Assert.Equal(new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.RepositoryRenamed, Repository: "octocat/OldName", RenamedTo: "octocat/NewName"), ownership);
        Assert.Null(_sent.Single(sent => sent.Url == Api + "/repositories/42").Authorization);
    }

    [Fact]
    public async Task CheckOwnershipAsync_NoReleaseHost_SaysSo()
    {
        var (publisher, _) = await SignedInAsync();

        var ownership = await publisher.CheckOwnershipAsync(new ListingDraft { Id = "MyMod" }, null);

        Assert.Equal(new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.NoHost), ownership);
    }

    [Fact]
    public async Task CheckOwnershipAsync_SpaceDockMod_ProvesItThroughItsSourceCodeLink()
    {
        On("GET", "https://spacedock.info/api/mod/4253", () => Json("""{"id":4253,"game_id":22409,"source_code":"https://github.com/Studio/MyMod"}"""));
        On("GET", Api + "/repos/Studio/MyMod", () => Json(Repository("Studio/MyMod", ownerId: 99)));
        On("GET", Api + "/repos/Studio/MyMod/topics", () => Json("""{"names":["ksa-index-octocat"]}"""));
        var (publisher, _) = await SignedInAsync();

        var ownership = await publisher.CheckOwnershipAsync(Draft(spaceDock: 4253), null);

        Assert.Equal(new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.Topic, Repository: "Studio/MyMod", SpaceDockMod: "4253"), ownership);
        Assert.Null(_sent.Single(sent => sent.Url.StartsWith("https://spacedock.info/", StringComparison.Ordinal)).Authorization);
    }

    [Theory]
    [InlineData("""{"id":4253,"game_id":22409,"source_code":""}""", ListingOwnershipProblem.SpaceDockNoSourceLink)]
    [InlineData("""{"id":4253,"game_id":22409,"source_code":"https://gitlab.com/Studio/MyMod"}""", ListingOwnershipProblem.SpaceDockNoSourceLink)]
    [InlineData("""{"id":4253,"game_id":3102,"source_code":"https://github.com/Studio/MyMod"}""", ListingOwnershipProblem.SpaceDockModUnusable)]
    [InlineData("""{"error":true,"reason":"Mod not published"}""", ListingOwnershipProblem.SpaceDockModUnusable)]
    public async Task CheckOwnershipAsync_SpaceDockModWithoutAUsableLink_NamesTheMod(string mod, ListingOwnershipProblem problem)
    {
        On("GET", "https://spacedock.info/api/mod/4253", () => Json(mod));
        var (publisher, _) = await SignedInAsync();

        var ownership = await publisher.CheckOwnershipAsync(Draft(spaceDock: 4253), null);

        Assert.Equal(new ListingOwnership(ListingOwnershipState.NotVerified, Problem: problem, SpaceDockMod: "4253"), ownership);
    }

    [Fact]
    public async Task CheckOwnershipAsync_SpaceDockAnswerAboutAnotherMod_CouldNotEvaluate()
    {
        On("GET", "https://spacedock.info/api/mod/4253", () => Json("""{"id":17,"game_id":3102,"source_code":"https://github.com/Studio/MyMod"}"""));
        var (publisher, _) = await SignedInAsync();

        var ownership = await publisher.CheckOwnershipAsync(Draft(spaceDock: 4253), null);

        Assert.Equal(ListingOwnership.Unknown, ownership);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CheckOwnershipAsync_OpenPullRequestOfTheAuthor_NeedsAStewardWhenItHasOtherFiles(bool otherFiles)
    {
        On("GET", Api + "/repos/octocat/MyMod", () => Json(Repository("octocat/MyMod", ownerId: 1)));
        On("GET", Upstream + "/pulls?state=open&per_page=100&page=1", () => Json("""
            [ { "number": 82, "html_url": "https://github.com/KSAModding/content-index/pull/82", "user": { "login": "octocat" },
                "head": { "ref": "listing-mymod", "repo": { "full_name": "octocat/content-index" } } } ]
            """));
        On("GET", Upstream + "/pulls/82/files?per_page=100&page=1", () => Json(otherFiles
            ? """[{ "filename": "listings/MyMod.toml" }, { "filename": "listings/Other.toml" }]"""
            : """[{ "filename": "listings/MyMod.toml" }]"""));
        var (publisher, _) = await SignedInAsync();

        var ownership = await publisher.CheckOwnershipAsync(Draft("octocat/MyMod"), null);

        Assert.Equal(
            otherFiles
                ? new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.PullRequestHasOtherFiles, PullRequest: 82)
                : new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.Owner, Repository: "octocat/MyMod"),
            ownership);
    }

    [Fact]
    public async Task CheckOwnershipAsync_NotVerified_LooksForNoPullRequest()
    {
        OnProof("Studio/MyMod", proven: false);
        var (publisher, _) = await SignedInAsync();

        await publisher.CheckOwnershipAsync(Draft("Studio/MyMod"), null);

        Assert.DoesNotContain(_sent, sent => sent.Url.StartsWith(Upstream + "/pulls", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CheckOwnershipAsync_ChangeOnTheSameHost_ChecksTheListedHost()
    {
        OnProof("Studio/MyMod", proven: true);
        var (publisher, _) = await SignedInAsync();

        var ownership = await publisher.CheckOwnershipAsync(Draft("Studio/MyMod") with { Name = "New name" }, Draft("studio/mymod"));

        Assert.Equal(ListingOwnershipState.Verified, ownership.State);
        Assert.Equal(1, _sent.Count(sent => sent.Url == Api + "/repos/studio/mymod"));
        Assert.DoesNotContain(_sent, sent => sent.Url == Api + "/repos/Studio/MyMod");
    }

    [Theory]
    [InlineData(true, true, ListingOwnershipState.Verified, "Studio/NewHost")]
    [InlineData(true, false, ListingOwnershipState.NotVerified, "Studio/NewHost")]
    [InlineData(false, true, ListingOwnershipState.NotVerified, "Studio/MyMod")]
    public async Task CheckOwnershipAsync_ChangeThatMovesTheHost_NeedsBothProofs(bool listedProven, bool newProven, ListingOwnershipState state, string repository)
    {
        OnProof("Studio/MyMod", listedProven);
        OnProof("Studio/NewHost", newProven);
        var (publisher, _) = await SignedInAsync();

        var ownership = await publisher.CheckOwnershipAsync(Draft("Studio/NewHost"), Draft("Studio/MyMod"));

        Assert.Equal(state, ownership.State);
        Assert.Equal(repository, ownership.Repository);
    }

    [Fact]
    public async Task CheckOwnershipAsync_ChangeToTheNewNameOfARenamedHost_ChecksTheNewName()
    {
        On("GET", Api + "/repos/Studio/OldName", () => Redirect(Api + "/repositories/42"));
        On("GET", Api + "/repositories/42", () => Json(Repository("Studio/NewName", ownerId: 99)));
        OnProof("Studio/NewName", proven: true);
        var (publisher, _) = await SignedInAsync();

        var ownership = await publisher.CheckOwnershipAsync(Draft("Studio/NewName"), Draft("Studio/OldName"));

        Assert.Equal(new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.Topic, Repository: "Studio/NewName"), ownership);
    }

    [Fact]
    public async Task CheckOwnershipAsync_HostDoesNotAnswer_CouldNotEvaluate()
    {
        On("GET", Api + "/repos/Studio/MyMod", () => Json("""{"message":"Server Error"}""", HttpStatusCode.BadGateway));
        var (publisher, _) = await SignedInAsync();

        var ownership = await publisher.CheckOwnershipAsync(Draft("Studio/MyMod"), null);

        Assert.Equal(ListingOwnership.Unknown, ownership);
    }

    [Fact]
    public async Task CheckOwnershipAsync_TokenRefused_Throws()
    {
        var (publisher, session) = await SignedInAsync();
        On("GET", Api + "/user", () => Json("""{"message":"Bad credentials"}""", HttpStatusCode.Unauthorized));

        var failure = await Assert.ThrowsAsync<ListingPublishException>(() => publisher.CheckOwnershipAsync(Draft("Studio/MyMod"), null));

        Assert.Equal(ListingPublishFailure.SignedOut, failure.Failure);
        Assert.Equal(GitHubSessionStatus.SignedOut, session.State.Status);
    }

    [Fact]
    public async Task CheckOwnershipAsync_UnauthorizedWithoutTheToken_KeepsTheSession()
    {
        On("GET", Api + "/repos/Studio/MyMod", () => Json("""{"message":"Requires authentication"}""", HttpStatusCode.Unauthorized));
        var (publisher, session) = await SignedInAsync();

        var ownership = await publisher.CheckOwnershipAsync(Draft("Studio/MyMod"), null);

        Assert.Equal(ListingOwnership.Unknown, ownership);
        Assert.Equal(GitHubSessionStatus.SignedIn, session.State.Status);
    }

    [Theory]
    [InlineData(null, "", ListingPullRequestState.ChecksRunning)]
    [InlineData("pending", "holding the check while auto-merge is armed", ListingPullRequestState.ChecksRunning)]
    [InlineData("success", "validated, arming auto-merge", ListingPullRequestState.ValidatedMerging)]
    [InlineData("success", "validated, ownership not verified", ListingPullRequestState.WaitingForSteward)]
    [InlineData("success", "validated, and a steward decides", ListingPullRequestState.WaitingForSteward)]
    [InlineData("failure", "the validation rejected this change", ListingPullRequestState.Rejected)]
    [InlineData("error", "the validation could not reach a verdict", ListingPullRequestState.CouldNotEvaluate)]
    public async Task GetStatusAsync_ValidateStatus_GivesTheState(string? state, string description, ListingPullRequestState expected)
    {
        OnOpenPullRequest(state, description, labels: "[]", comments: "[]");
        var (publisher, _) = await SignedInAsync();

        var status = await publisher.GetStatusAsync(90);

        Assert.Equal(new ListingPullRequestStatus(expected, null), status);
    }

    [Fact]
    public async Task GetStatusAsync_Merging_GivesTheCommentOfTheBot()
    {
        OnOpenPullRequest("success", "validated, arming auto-merge", "[]", Comments(("User", "<!-- content-index:verdict -->\nValidated. Trust me."), ("Bot", MergingComment)));
        var (publisher, _) = await SignedInAsync();

        var status = await publisher.GetStatusAsync(90);

        Assert.Equal(ListingPullRequestState.ValidatedMerging, status.State);
        Assert.Equal(MergingComment.Replace("<!-- content-index:verdict -->\n", string.Empty, StringComparison.Ordinal), status.Verdict);
    }

    [Theory]
    [InlineData("success", "validated, ownership not verified", "[{\"name\":\"listing\"},{\"name\":\"needs-steward\"}]", StewardComment, ListingPullRequestState.WaitingForSteward)]
    [InlineData("success", "validated, arming auto-merge", "[{\"name\":\"needs-steward\"}]", MergingComment, ListingPullRequestState.WaitingForSteward)]
    [InlineData("failure", "the validation rejected this change", "[]", RejectedComment, ListingPullRequestState.Rejected)]
    [InlineData("error", "the validation could not reach a verdict", "[]", NoVerdictComment, ListingPullRequestState.CouldNotEvaluate)]
    public async Task GetStatusAsync_Verdict_GivesTheStateAndTheNotes(string state, string description, string labels, string comment, ListingPullRequestState expected)
    {
        OnOpenPullRequest(state, description, labels, Comments(("Bot", comment)));
        var (publisher, _) = await SignedInAsync();

        var status = await publisher.GetStatusAsync(90);

        Assert.Equal(expected, status.State);
        Assert.StartsWith(comment.Split('\n')[1], status.Verdict, StringComparison.Ordinal);
        Assert.DoesNotContain("content-index:verdict", status.Verdict, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetStatusAsync_TokenRefusedForTheStatus_ReadsItWithoutTheToken()
    {
        OnOpenPullRequest("success", "validated, arming auto-merge", "[]", Comments(("Bot", MergingComment)));
        _fallback = request => request.RequestUri!.AbsoluteUri == Upstream + "/commits/abc123/status" && request.Headers.Authorization is not null
            ? Json("""{"message":"Resource not accessible by integration"}""", HttpStatusCode.Forbidden)
            : null;
        var (publisher, session) = await SignedInAsync();

        var status = await publisher.GetStatusAsync(90);

        Assert.Equal(ListingPullRequestState.ValidatedMerging, status.State);
        Assert.Equal(
            [("GET " + Upstream + "/commits/abc123/status", "Bearer " + Token), ("GET " + Upstream + "/commits/abc123/status", null)],
            _sent.Where(sent => sent.Url.EndsWith("/status", StringComparison.Ordinal)).Select(sent => (sent.Line, sent.Authorization)));
        Assert.Equal("Bearer " + Token, _sent.Single(sent => sent.Url.Contains("/comments", StringComparison.Ordinal)).Authorization);
        Assert.Equal(GitHubSessionStatus.SignedIn, session.State.Status);
    }

    [Fact]
    public async Task GetStatusAsync_RateLimitedWithTheToken_DoesNotAskWithoutIt()
    {
        var reset = new DateTimeOffset(2026, 9, 19, 13, 0, 0, TimeSpan.Zero);
        OnOpenPullRequest("success", "validated, arming auto-merge", "[]", "[]");
        On("GET", Upstream + "/commits/abc123/status", () => RateLimited(reset));
        var (publisher, _) = await SignedInAsync();

        var failure = await Assert.ThrowsAsync<ListingPublishException>(() => publisher.GetStatusAsync(90));

        Assert.Equal(ListingPublishFailure.RateLimited, failure.Failure);
        Assert.Single(_sent, sent => sent.Url.EndsWith("/status", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{ "number": 90, "state": "closed", "merged": true }""", ListingPullRequestState.Merged)]
    [InlineData("""{ "number": 90, "state": "closed", "merged": false }""", ListingPullRequestState.Closed)]
    public async Task GetStatusAsync_MergedOrClosed_IsFinal(string pull, ListingPullRequestState expected)
    {
        On("GET", Upstream + "/pulls/90", () => Json(pull));
        var (publisher, _) = await SignedInAsync();

        var status = await publisher.GetStatusAsync(90);

        Assert.Equal(expected, status.State);
        Assert.True(status.IsFinal);
        Assert.Single(_sent);
    }

    private async Task<(ListingPublisher Publisher, GitHubSession Session)> SignedInAsync()
    {
        var http = new HttpClient(new FakeHttpMessageHandler(RespondAsync));
        var session = new GitHubSession(http, "Iv1.testclient", "borea-test", _time);
        Assert.True((await session.SignInAsync()).SignedIn);
        _sent.Clear();
        return (new ListingPublisher(session, http, new LineFormat(), _time), session);
    }

    private void On(string method, string url, params Func<HttpResponseMessage>[] answers) =>
        _routes[method + " " + url] = new Queue<Func<HttpResponseMessage>>(answers);

    /// <summary>The App installed on the author's account with the fork among other repositories, and free branch names.</summary>
    private void OnInstalledFork()
    {
        On("GET", Installations, () => Json(InstallationsJson));
        On("GET", InstalledRepositories, () => Json("""
            { "total_count": 2, "repositories": [
                { "full_name": "octocat/MyMod", "fork": false, "default_branch": "main" },
                { "full_name": "octocat/content-index", "fork": true, "default_branch": "main" } ] }
            """));
        On("GET", Fork, () => Json(ForkJson));
        On("POST", Fork + "/git/refs", () => Json("{}", HttpStatusCode.Created));
        On("PUT", Fork + "/contents/listings/MyMod.toml", () => Json("{}", HttpStatusCode.Created));
    }

    /// <summary>The compare of main of content-index with the default branch of the fork.</summary>
    /// <param name="ownCommit">A commit on the fork that content-index does not have.</param>
    private void OnCompare(string mergeBase, string? ownCommit = null, string url = Compare) =>
        On("GET", url, () => Json(JsonSerializer.Serialize(new
        {
            status = ownCommit is null ? "behind" : "diverged",
            base_commit = new { sha = MainSha },
            merge_base_commit = new { sha = mergeBase },
            commits = ownCommit is null ? [] : new[] { new { sha = ownCommit } },
        })));

    private void OnProof(string repository, bool proven)
    {
        foreach (var name in new[] { repository, repository.ToLowerInvariant() })
        {
            On("GET", $"{Api}/repos/{name}", () => Json(Repository(repository, ownerId: 99)));
            On("GET", $"{Api}/repos/{name}/topics", () => Json(proven ? """{"names":["ksa-index-octocat"]}""" : """{"names":[]}"""));
        }
    }

    private void OnOpenPullRequest(string? state, string description, string labels, string comments)
    {
        On("GET", Upstream + "/pulls/90", () => Json($$"""{ "number": 90, "state": "open", "merged": false, "head": { "sha": "abc123" }, "labels": {{labels}} }"""));
        On("GET", Upstream + "/commits/abc123/status", () => Json(state is null
            ? """{"state":"pending","statuses":[]}"""
            : $$"""{"state":"{{state}}","statuses":[{"context":"coverage","state":"success"},{"context":"validate","state":"{{state}}","description":"{{description}}"}]}"""));
        On("GET", Upstream + "/issues/90/comments?per_page=100", () => Json(comments));
    }

    private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync();
        var url = request.RequestUri!.AbsoluteUri;
        if (url.StartsWith(Api, StringComparison.Ordinal) || url.StartsWith("https://spacedock.info/", StringComparison.Ordinal))
            _sent.Add(new Sent(request.Method.Method, url, body, request.Headers.Authorization?.ToString()));

        if (_fallback?.Invoke(request) is { } answer)
            return answer;

        if (!_routes.TryGetValue(request.Method.Method + " " + url, out var answers))
            return Json("""{"message":"Not Found"}""", HttpStatusCode.NotFound);

        return answers.Count > 1 ? answers.Dequeue()() : answers.Peek()();
    }

    private string Body(string method, string url) => _sent.Last(sent => sent.Method == method && sent.Url == url).Body!;

    private static ListingSubmission New() => new("MyMod", "My Mod", Text, IsEdit: false);

    private static ListingDraft Draft(string? github = null, long? spaceDock = null) => new()
    {
        Id = "MyMod",
        Name = "My Mod",
        Releases = new ListingReleases(github, spaceDock),
    };

    private static string Repository(string fullName, long ownerId, bool fork = false) =>
        $$"""{ "full_name": "{{fullName}}", "fork": {{(fork ? "true" : "false")}}, "default_branch": "main", "owner": { "id": {{ownerId}}, "login": "{{fullName.Split('/')[0]}}" } }""";

    private static string Content(string text, string sha) =>
        JsonSerializer.Serialize(new { sha, encoding = "base64", content = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).Insert(4, "\n") });

    private static string Comments(params (string Type, string Body)[] comments) =>
        JsonSerializer.Serialize(comments.Select(comment => new { body = comment.Body, user = new { type = comment.Type } }));

    private static string Base64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage RateLimited(DateTimeOffset reset)
    {
        var response = Json("""{"message":"API rate limit exceeded."}""", HttpStatusCode.Forbidden);
        response.Headers.Add("x-ratelimit-remaining", "0");
        response.Headers.Add("x-ratelimit-reset", reset.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        return response;
    }

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently) { Content = new StringContent("""{"message":"Moved Permanently"}""") };
        response.Headers.Location = new Uri(location);
        return response;
    }

    private sealed record Sent(string Method, string Url, string? Body, string? Authorization)
    {
        public string Line => Method + " " + Url;
    }

    private sealed class ListProgress<T>(List<T> reports) : IProgress<T>
    {
        public void Report(T value)
        {
            lock (reports)
                reports.Add(value);
        }
    }

    /// <summary>Reads the <c>key = "value"</c> lines of a marker file.</summary>
    private sealed class LineFormat : IListingFormat
    {
        public string Write(AuthoredTable document, string? original = null) => throw new NotSupportedException();

        public AuthoredTable Read(string text)
        {
            var table = new AuthoredTable();
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                table.Set(parts[0], parts[1].Trim('"'));
            }

            return table;
        }
    }

    /// <summary>Fires every delay at once and moves the clock by it.</summary>
    private sealed class StepTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private DateTimeOffset _now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
                return _now;
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (_gate)
                _now += dueTime;

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
