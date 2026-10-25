using System.Net;
using System.Text;
using System.Text.Json;
using Borea.Core.GitHub;
using Borea.Core.Listings;
using Borea.Core.Stewardship;
using Borea.Network.GitHub;
using Borea.Network.Listings;
using Borea.Network.Tests.Listings;

namespace Borea.Network.Tests.GitHub;

/// <summary>
/// The steward octocat reviews pull requests that GitHub answers from routes this test keeps in memory.
/// A GET route is keyed by its URL, and any other by its method and URL.
/// </summary>
public sealed partial class GitHubPullRequestReviewsTests
{
    private const string Api = "https://api.github.com";
    private const string Index = "KSAModding/content-index";
    private const string Releases = "KSAModding/content-index-releases";
    private const string Token = "ghu_secret";
    private const string First = "0123456789abcdef0123456789abcdef01234567";
    private const string Second = "89abcdef0123456789abcdef0123456789abcdef";
    private const string IndexMarker = "<!-- content-index:verdict -->";
    private const string ReleasesMarker = "<!-- content-index-releases:verdict -->";
    private const string Listing = "id = \"MyMod\"\nname = \"My Mod\"\n[releases]\ngithub = \"alice/MyMod\"\n";
    private const string VehicleListing = "id = \"ExampleRover\"\ntype = \"vehicle\"\nname = \"Example Rover\"\n[releases]\ngithub = \"alice/ExampleRover\"\n";

    private readonly List<Sent> _sent = [];
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.Ordinal);
    private readonly FakeOwnership _ownership = new();

    [Fact]
    public async Task ReadAsync_ListingFromAFork_ReadsItAtTheHeadCommitAndChecksItsAuthorAgainstTheBaseBranch()
    {
        Pull(Index, 5, First, "alice/content-index", labels: ["listing", "needs-steward"]);
        Files(Index, 5, File("listings/MyMod.toml", "added", "@@ -0,0 +1,4 @@\n+id = \"MyMod\"", additions: 4));
        Comments(Index, 5, ("mallory", IndexMarker + "\nValidated. Trust me."), (IndexVerdict.BotLogin, IndexMarker + "\nValidated, and ownership is not verified, so a steward decides."));
        Status(Index, First, ("other", "failure", "Another check."), ("validate", "success", "validated, a steward decides"));
        Content(Index, "listings/MyMod.toml", First, Listing);
        _ownership.Result = new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.NoProof, Repository: "alice/MyMod");
        var reviews = await SignedInAsync();

        var review = await reviews.ReadAsync(Index, 5);

        Assert.Equal(
            (new Uri($"https://github.com/{Index}/pull/5"), "Pull 5", "alice", PullRequestState.Open, false, "main", First, "alice/content-index", true, true),
            (review.Url, review.Title, review.Author, review.State, review.IsDraft, review.BaseBranch, review.HeadCommit, review.HeadRepository, review.IsFromFork, review.CanRunChecks));
        Assert.Equal(["listing", "needs-steward"], review.Labels);
        Assert.Equal(new ValidateStatus(ValidateState.Success, "validated, a steward decides", new Uri($"https://github.com/{Index}/actions/runs/1")), review.Validate);
        Assert.Null(review.ValidateFailure);
        Assert.Equal("Validated, and ownership is not verified, so a steward decides.", review.Verdict);
        var file = Assert.Single(review.Files);
        Assert.Equal(("listings/MyMod.toml", "added", 4, "@@ -0,0 +1,4 @@\n+id = \"MyMod\""), (file.Path, file.Status, file.Additions, file.Patch));
        Assert.Equal(PullRequestFile.DiffUrlOf(review.Url, "listings/MyMod.toml"), file.DiffUrl);
        var document = Assert.Single(review.Documents);
        Assert.Equal(("listings/MyMod.toml", StewardQueueKind.Listing, Listing, null), (document.Path, document.Kind, document.Text, document.ParseError));
        Assert.Equal("My Mod", document.Document?.GetString("name"));
        Assert.Same(_ownership.Result, document.Ownership);
        Assert.Equal([(new GitHubAccount("alice", 5), "listings/MyMod.toml", "main", First)], _ownership.Calls);

        // the fork's head commit is read from the base repository, and only the status goes without the token
        Assert.Contains(_sent, sent => sent.Url == $"{Api}/repos/{Index}/contents/listings/MyMod.toml?ref={First}");
        Assert.DoesNotContain(_sent, sent => sent.Url.Contains("/repos/alice/", StringComparison.Ordinal));
        Assert.All(_sent, sent => Assert.Equal(sent.Url.Contains("/status", StringComparison.Ordinal) ? null : "Bearer " + Token, sent.Authorization));
        Assert.All(_sent, sent => Assert.Equal("GET", sent.Method));
    }

    [Theory]
    [InlineData("vehicles/ExampleRover.toml", StewardQueueKind.Vehicle)]
    [InlineData("saves/ExampleSave.toml", StewardQueueKind.Save)]
    public async Task ReadAsync_VehicleOrSave_IsADocumentWhoseAuthorIsCheckedAsForAListing(string path, StewardQueueKind kind)
    {
        Pull(Index, 9, First, "alice/content-index", labels: [kind.ToString().ToLowerInvariant(), "needs-steward"]);
        Files(Index, 9, File(path, "added", "@@"));
        Status(Index, First, ("validate", "success", "validated, a steward decides"));
        Content(Index, path, First, VehicleListing);
        var reviews = await SignedInAsync();

        var review = await reviews.ReadAsync(Index, 9);

        var document = Assert.Single(review.Documents);
        Assert.Equal((path, kind, "Example Rover"), (document.Path, document.Kind, document.Document?.GetString("name")));
        Assert.Same(_ownership.Result, document.Ownership);
        Assert.Equal([(new GitHubAccount("alice", 5), path, "main", First)], _ownership.Calls);
        Assert.Equal((null, null), (document.NewestRelease, document.NewestReleaseFailure));
    }

    [Fact]
    public async Task ReadAsync_Vehicle_ReadsTheInstallFolderOfItsNewestReleaseOnMainOfTheReleases()
    {
        VehiclePull();
        _routes[$"{Api}/repos/{Releases}/contents/releases/ExampleRover?ref=main"] = () => Json(JsonSerializer.Serialize(
            new[] { "1.2.0.json", "1.10.0.json", "notes.md" }.Select(name => new { name, path = "releases/ExampleRover/" + name, type = "file" })));
        Content(Releases, "releases/ExampleRover/1.10.0.json", "main", """{"id": "ExampleRover", "type": "vehicle", "version": "1.10.0", "install": {"root": "Example Rover", "derived": true, "folder": "Example Rover"}}""");
        var reviews = await SignedInAsync();

        var document = Assert.Single((await reviews.ReadAsync(Index, 9)).Documents);

        Assert.Equal(new PullRequestRelease("1.10.0", "Example Rover"), document.NewestRelease);
        Assert.Null(document.NewestReleaseFailure);
        Assert.DoesNotContain(_sent, sent => sent.Url.Contains("1.2.0.json", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, StewardFailure.UnexpectedResponse)]
    [InlineData(HttpStatusCode.Forbidden, StewardFailure.Forbidden)]
    public async Task ReadAsync_VehicleWhoseReleasesCannotBeRead_StillShowsWithItsOwnership(HttpStatusCode status, StewardFailure failure)
    {
        VehiclePull();
        _routes[$"{Api}/repos/{Releases}/contents/releases/ExampleRover?ref=main"] = () => Json("""{"message":"Resource not accessible by integration"}""", status);
        var reviews = await SignedInAsync();

        var document = Assert.Single((await reviews.ReadAsync(Index, 9)).Documents);

        Assert.Null(document.NewestRelease);
        Assert.Equal(failure, document.NewestReleaseFailure?.Failure);
        Assert.Same(_ownership.Result, document.Ownership);
        Assert.Equal("Example Rover", document.Document?.GetString("name"));
    }

    [Fact]
    public async Task ReadAsync_VehicleWhoseNewestReleaseIsNoJsonObject_SaysItCannotBeRead()
    {
        VehiclePull();
        _routes[$"{Api}/repos/{Releases}/contents/releases/ExampleRover?ref=main"] = () => Json("""[{"name": "1.0.0.json", "type": "file"}]""");
        Content(Releases, "releases/ExampleRover/1.0.0.json", "main", "[1]");
        var reviews = await SignedInAsync();

        var document = Assert.Single((await reviews.ReadAsync(Index, 9)).Documents);

        Assert.Null(document.NewestRelease);
        Assert.Equal(StewardFailure.UnreadableFile, document.NewestReleaseFailure?.Failure);
    }

    [Fact]
    public async Task ReadAsync_ListingOrPack_ReadsNoRelease()
    {
        Pull(Index, 5, First, Index, labels: ["listing"]);
        Files(Index, 5, File("listings/MyMod.toml", "added", "@@"));
        Status(Index, First);
        Content(Index, "listings/MyMod.toml", First, Listing);
        var reviews = await SignedInAsync();

        var document = Assert.Single((await reviews.ReadAsync(Index, 5)).Documents);

        Assert.Null(document.NewestRelease);
        Assert.DoesNotContain(_sent, sent => sent.Url.Contains(Releases, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadAsync_AmendmentWithThreeReleaseFiles_ListsEachWithItsPatch()
    {
        Pull(Releases, 71, First, Releases, labels: ["amendment", "needs-steward"]);
        Files(
            Releases,
            71,
            File("releases/MyMod/1.0.0.json", "modified", "@@ -3 +3 @@\n-  \"game_max\": null\n+  \"game_max\": \"2026.9\""),
            File("releases/MyMod/1.1.0.json", "modified", "@@ -3 +3 @@\n-  \"yanked\": false\n+  \"yanked\": true"),
            File("releases/MyMod/1.2.0.json", "modified", "@@ -7 +7 @@\n-  \"os\": null\n+  \"os\": [\"windows\"]"));
        Comments(Releases, 71, (IndexVerdict.BotLogin, ReleasesMarker + "\nAn amendment by a non-owner, so a steward decides."));
        Status(Releases, First, ("validate", "success", "a steward decides"));
        var reviews = await SignedInAsync();

        var review = await reviews.ReadAsync(Releases, 71);

        Assert.Equal(["releases/MyMod/1.0.0.json", "releases/MyMod/1.1.0.json", "releases/MyMod/1.2.0.json"], review.Files.Select(file => file.Path));
        Assert.All(review.Files, file => Assert.StartsWith("@@ -", file.Patch, StringComparison.Ordinal));
        Assert.Contains("\"yanked\": true", review.Files[1].Patch, StringComparison.Ordinal);
        Assert.Empty(review.Documents);
        Assert.Equal("An amendment by a non-owner, so a steward decides.", review.Verdict);
        Assert.Equal((false, false), (review.IsFromFork, review.CanRunChecks));
        Assert.Empty(_ownership.Calls);
        Assert.DoesNotContain(_sent, sent => sent.Url.Contains("/contents/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadAsync_FileWithoutPatch_HasNoPatchAndItsPlaceOnGitHub()
    {
        Pull(Index, 7, First, Index);
        Files(Index, 7, File("packs/my-pack/icon.png", "added", patch: null), File("tags.toml", "modified", "@@ -1 +1,2 @@\n+[mod.planes]"));
        Status(Index, First);
        var reviews = await SignedInAsync();

        var review = await reviews.ReadAsync(Index, 7);

        Assert.Null(review.Files[0].Patch);
        Assert.Equal(PullRequestFile.DiffUrlOf(new Uri($"https://github.com/{Index}/pull/7"), "packs/my-pack/icon.png"), review.Files[0].DiffUrl);
        Assert.NotNull(review.Files[1].Patch);
        Assert.Empty(review.Documents);
        Assert.Equal(new ValidateStatus(ValidateState.Missing), review.Validate);
        Assert.Null(review.Verdict);
    }

    [Fact]
    public async Task ReadAsync_DocumentThatDoesNotParse_KeepsItsTextAndTheError()
    {
        const string Broken = "id = \"MyMod\"\n[links\n";
        const string Pack = "id = \"my-pack\"\nname = \"My Pack\"\n";
        Pull(Index, 8, First, "alice/content-index");
        Files(Index, 8, File("listings/MyMod.toml", "modified", "@@"), File("packs/my-pack/1.0.0.toml", "added", "@@"), File("listings/Binary.toml", "added", "@@"));
        Status(Index, First, ("validate", "failure", "listings/MyMod.toml does not parse"));
        Content(Index, "listings/MyMod.toml", First, Broken);
        Content(Index, "packs/my-pack/1.0.0.toml", First, Pack);
        _routes[$"{Api}/repos/{Index}/contents/listings/Binary.toml?ref={First}"] = () => Json(JsonSerializer.Serialize(new { sha = "b2", encoding = "base64", content = Convert.ToBase64String([0xff, 0xfe, 0x00]) }));
        var reviews = await SignedInAsync();

        var review = await reviews.ReadAsync(Index, 8);

        Assert.Equal(
            [("listings/MyMod.toml", StewardQueueKind.Listing, Broken, false, "An unclosed table header."), ("packs/my-pack/1.0.0.toml", StewardQueueKind.Pack, Pack, true, null), ("listings/Binary.toml", StewardQueueKind.Listing, null, false, null)],
            review.Documents.Select(document => (document.Path, document.Kind, document.Text, document.Document is not null, document.ParseError)));
        Assert.All(review.Documents, document => Assert.Null(document.Ownership));
        Assert.Empty(_ownership.Calls);
        Assert.Equal(ValidateState.Failure, review.Validate?.State);
    }

    [Fact]
    public async Task ReadAsync_NewCommitSinceTheLastRead_ReadsEverythingAtTheNewHead()
    {
        Pull(Index, 5, First, "alice/content-index");
        Files(Index, 5, File("listings/MyMod.toml", "added", "@@"));
        Status(Index, First, ("validate", "success", "validated, a steward decides"));
        Content(Index, "listings/MyMod.toml", First, Listing);
        var reviews = await SignedInAsync();
        var before = await reviews.ReadAsync(Index, 5);

        Pull(Index, 5, Second, "alice/content-index");
        Status(Index, Second, ("validate", "pending", "the checks run"));
        Content(Index, "listings/MyMod.toml", Second, Listing.Replace("My Mod", "My Mod 2", StringComparison.Ordinal));
        var after = await reviews.ReadAsync(Index, 5);

        Assert.Equal((First, ValidateState.Success, "My Mod"), (before.HeadCommit, before.Validate?.State, before.Documents[0].Document?.GetString("name")));
        Assert.Equal((Second, ValidateState.Pending, "My Mod 2"), (after.HeadCommit, after.Validate?.State, after.Documents[0].Document?.GetString("name")));
        Assert.Equal([First, Second], _ownership.Calls.Select(call => call.Head));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, true, StewardFailure.RateLimited)]
    [InlineData(HttpStatusCode.Forbidden, false, StewardFailure.Forbidden)]
    [InlineData(HttpStatusCode.BadGateway, false, StewardFailure.UnexpectedResponse)]
    public async Task ReadAsync_StatusCannotBeRead_TheRestStillShows(HttpStatusCode status, bool rateLimited, StewardFailure failure)
    {
        Pull(Index, 5, First, Index);
        Files(Index, 5, File("tags.toml", "modified", "@@"));
        _routes[$"{Api}/repos/{Index}/commits/{First}/status?per_page=100"] = () =>
        {
            var response = Json(rateLimited ? """{"message":"API rate limit exceeded for 203.0.113.9."}""" : """{"message":"Forbidden"}""", status);
            if (rateLimited)
            {
                response.Headers.Add("x-ratelimit-remaining", "0");
                response.Headers.Add("x-ratelimit-reset", "1790000000");
            }

            return response;
        };
        var reviews = await SignedInAsync();

        var review = await reviews.ReadAsync(Index, 5);

        Assert.Null(review.Validate);
        Assert.Equal(failure, review.ValidateFailure?.Failure);
        Assert.Single(review.Files);
    }

    [Theory]
    [InlineData("open", false, null, PullRequestState.Open)]
    [InlineData("closed", false, null, PullRequestState.Closed)]
    [InlineData("closed", true, "2026-09-25T10:00:00Z", PullRequestState.Merged)]
    [InlineData("closed", false, "2026-09-25T10:00:00Z", PullRequestState.Merged)]
    public async Task ReadAsync_ClosedOrMerged_SaysSo(string state, bool merged, string? mergedAt, PullRequestState expected)
    {
        Pull(Index, 5, First, Index, state: state, merged: merged, mergedAt: mergedAt, draft: true);
        Files(Index, 5);
        Status(Index, First);
        var reviews = await SignedInAsync();

        var review = await reviews.ReadAsync(Index, 5);

        Assert.Equal((expected, true), (review.State, review.IsDraft));
    }

    [Fact]
    public async Task ReadAsync_AuthorAccountIsGone_TheProofCannotBeChecked()
    {
        Pull(Index, 5, First, null, author: null);
        Files(Index, 5, File("listings/MyMod.toml", "added", "@@"));
        Status(Index, First);
        Content(Index, "listings/MyMod.toml", First, Listing);
        var reviews = await SignedInAsync();

        var review = await reviews.ReadAsync(Index, 5);

        Assert.Equal((null, null, true), (review.Author, review.HeadRepository, review.IsFromFork));
        Assert.Same(ListingOwnership.Unknown, review.Documents[0].Ownership);
        Assert.Empty(_ownership.Calls);
    }

    [Fact]
    public async Task ReadAsync_ListingWithAnUpperCaseExtension_ShowsIt_AndTheRealCheckGivesNoProof()
    {
        Pull(Index, 5, First, "alice/content-index");
        Files(Index, 5, File("listings/MyMod.TOML", "added", "@@"));
        Status(Index, First, ("validate", "failure", "the file name has to end in '.toml', in lowercase"));
        Content(Index, "listings/MyMod.TOML", First, Listing);
        var session = await SignInAsync();
        var reviews = new GitHubPullRequestReviews(session, Http(), new TableFormat(), new ListingOwnershipCheck(session, Http(), new TableFormat()));

        var review = await reviews.ReadAsync(Index, 5);

        var document = Assert.Single(review.Documents);
        Assert.Equal(("listings/MyMod.TOML", StewardQueueKind.Listing, "My Mod"), (document.Path, document.Kind, document.Document?.GetString("name")));
        Assert.Same(ListingOwnership.Unknown, document.Ownership);
        Assert.DoesNotContain(_sent, sent => sent.Url.Contains("ref=main", StringComparison.Ordinal) || sent.Url.Contains("alice/MyMod", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, StewardFailure.NotFound)]
    [InlineData(HttpStatusCode.Forbidden, StewardFailure.Forbidden)]
    public async Task ReadAsync_PullRequestCannotBeRead_Throws(HttpStatusCode status, StewardFailure failure)
    {
        _routes[$"{Api}/repos/{Releases}/pulls/9"] = () => Json("""{"message":"Resource not accessible by integration"}""", status);
        var reviews = await SignedInAsync();

        var exception = await Assert.ThrowsAsync<StewardException>(() => reviews.ReadAsync(Releases, 9));

        Assert.Equal(failure, exception.Failure);
    }

    [Fact]
    public async Task ReadAsync_DocumentIsGoneFromTheHeadCommit_Throws()
    {
        Pull(Index, 5, First, Index);
        Files(Index, 5, File("listings/MyMod.toml", "added", "@@"));
        Status(Index, First);
        var reviews = await SignedInAsync();

        var exception = await Assert.ThrowsAsync<StewardException>(() => reviews.ReadAsync(Index, 5));

        Assert.Equal(StewardFailure.UnexpectedResponse, exception.Failure);
    }

    [Fact]
    public async Task ReadAsync_Unauthorized_SignsOut()
    {
        _routes[$"{Api}/repos/{Index}/pulls/5"] = () => Json("""{"message":"Bad credentials"}""", HttpStatusCode.Unauthorized);
        var session = await SignInAsync();
        var reviews = new GitHubPullRequestReviews(session, Http(), new TableFormat(), _ownership);

        var exception = await Assert.ThrowsAsync<StewardException>(() => reviews.ReadAsync(Index, 5));

        Assert.Equal(StewardFailure.SignedOut, exception.Failure);
        Assert.Equal(GitHubSessionStatus.SignedOut, session.State.Status);
    }

    [Fact]
    public async Task ReadAsync_OwnershipCheckSignedOut_SignsTheReviewOut()
    {
        Pull(Index, 5, First, Index);
        Files(Index, 5, File("listings/MyMod.toml", "added", "@@"));
        Status(Index, First);
        Content(Index, "listings/MyMod.toml", First, Listing);
        _ownership.Failure = new ListingPublishException(ListingPublishFailure.SignedOut, ListingPublishStep.Ownership);
        var reviews = await SignedInAsync();

        var exception = await Assert.ThrowsAsync<StewardException>(() => reviews.ReadAsync(Index, 5));

        Assert.Equal(StewardFailure.SignedOut, exception.Failure);
    }

    [Fact]
    public async Task ReadAsync_SignedOutOrAnotherRepository_SendsNothing()
    {
        var session = new GitHubSession(Http(), "Iv1.testclient", "borea-test", new InstantTimeProvider());
        var reviews = new GitHubPullRequestReviews(session, Http(), new TableFormat(), _ownership);

        var signedOut = await Assert.ThrowsAsync<StewardException>(() => reviews.ReadAsync(Index, 5));
        await Assert.ThrowsAsync<ArgumentException>(() => reviews.ReadAsync("KSAModding/Borea", 5));

        Assert.Equal(StewardFailure.SignedOut, signedOut.Failure);
        Assert.Empty(_sent);
    }

    /// <summary>Answers the pull request, and no comments until <see cref="Comments"/> gives some.</summary>
    private void Pull(string repository, int number, string head, string? headRepository, string[]? labels = null, string state = "open", bool merged = false, string? mergedAt = null, bool draft = false, string? author = "alice", string baseBranch = "main")
    {
        _routes.TryAdd($"{Api}/repos/{repository}/issues/{number}/comments?per_page=100&page=1", () => Json("[]"));
        _routes[$"{Api}/repos/{repository}/pulls/{number}"] = () => Json(JsonSerializer.Serialize(new
        {
            number,
            title = $"Pull {number}",
            html_url = $"https://github.com/{repository}/pull/{number}",
            state,
            draft,
            merged,
            merged_at = mergedAt,
            user = author is null ? null : new { login = author, id = 5 },
            labels = (labels ?? []).Select(label => new { name = label }),
            head = new { @ref = "listing-MyMod", sha = head, repo = headRepository is null ? null : new { full_name = headRepository } },
            @base = new { @ref = baseBranch, sha = "fedcba9876543210fedcba9876543210fedcba98", repo = new { full_name = repository } },
        }));
    }

    /// <summary>A pull request that changes vehicles/ExampleRover.toml, whose releases the test gives.</summary>
    private void VehiclePull()
    {
        Pull(Index, 9, First, Index, labels: ["vehicle"]);
        Files(Index, 9, File("vehicles/ExampleRover.toml", "modified", "@@"));
        Status(Index, First);
        Content(Index, "vehicles/ExampleRover.toml", First, VehicleListing);
    }

    private static object File(string path, string status, string? patch, int additions = 1) =>
        new { filename = path, status, additions, deletions = 0, patch };

    private void Files(string repository, int number, params object[] files) =>
        _routes[$"{Api}/repos/{repository}/pulls/{number}/files?per_page=100&page=1"] = () => Json(JsonSerializer.Serialize(files));

    private void Comments(string repository, int number, params (string Login, string Body)[] comments) =>
        _routes[$"{Api}/repos/{repository}/issues/{number}/comments?per_page=100&page=1"] = () => Json(JsonSerializer.Serialize(comments.Select(comment => new { body = comment.Body, user = new { login = comment.Login } })));

    private void Status(string repository, string sha, params (string Context, string State, string Description)[] statuses) =>
        _routes[$"{Api}/repos/{repository}/commits/{sha}/status?per_page=100"] = () => Json(JsonSerializer.Serialize(new
        {
            state = "pending",
            statuses = statuses.Select(status => new { context = status.Context, state = status.State, description = status.Description, target_url = $"https://github.com/{repository}/actions/runs/1" }),
        }));

    private void Content(string repository, string path, string sha, string text) =>
        _routes[$"{Api}/repos/{repository}/contents/{path}?ref={sha}"] = () => Json(JsonSerializer.Serialize(new { sha = "b1", encoding = "base64", content = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) }));

    private async Task<GitHubPullRequestReviews> SignedInAsync() => new(await SignInAsync(), Http(), new TableFormat(), _ownership);

    private async Task<GitHubSession> SignInAsync()
    {
        var session = new GitHubSession(Http(), "Iv1.testclient", "borea-test", new InstantTimeProvider());
        Assert.True((await session.SignInAsync()).SignedIn);
        lock (_sent)
            _sent.Clear();
        return session;
    }

    private HttpClient Http() => new(new FakeHttpMessageHandler(async request => Respond(request, request.Content is null ? null : await request.Content.ReadAsStringAsync())));

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

        var key = request.Method == HttpMethod.Get ? url : $"{request.Method.Method} {url}";
        return _routes.TryGetValue(key, out var route) ? route() : Json("""{"message":"Not Found"}""", HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed record Sent(string Method, string Url, string? Authorization, string? Body = null);

    /// <summary>Records each check of a pull request author and answers with <see cref="Result"/>.</summary>
    private sealed class FakeOwnership : IListingOwnershipCheck
    {
        public List<(GitHubAccount Author, string Path, string BaseBranch, string Head)> Calls { get; } = [];

        public ListingOwnership Result { get; set; } = new(ListingOwnershipState.Verified, ListingOwnershipProof.Owner, Repository: "alice/MyMod");

        public Exception? Failure { get; set; }

        public Task<ListingOwnership> CheckAsync(GitHubAccount author, ListingDraft submitted, ListingDraft? listed, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ListingOwnership> CheckPullRequestAsync(GitHubAccount author, string path, string baseBranch, string headCommit, CancellationToken cancellationToken = default)
        {
            // the same refusal as ListingOwnershipCheck gives for a name without a lower-case .toml
            if (!path.EndsWith(".toml", StringComparison.Ordinal))
                throw new ArgumentException($"{path} is no listing document.", nameof(path));

            lock (Calls)
                Calls.Add((author, path, baseBranch, headCommit));
            return Failure is { } failure ? Task.FromException<ListingOwnership>(failure) : Task.FromResult(Result);
        }

        public Task<IReadOnlyList<string>> OwnersAsync(ListingDraft listed, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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
