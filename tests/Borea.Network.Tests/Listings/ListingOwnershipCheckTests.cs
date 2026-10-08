using System.Net;
using System.Text;
using System.Text.Json;
using Borea.Core.GitHub;
using Borea.Core.Listings;
using Borea.Network.GitHub;
using Borea.Network.Listings;

namespace Borea.Network.Tests.Listings;

/// <summary>The steward octocat (id 1) is signed in and checks the author alice (id 5).</summary>
public sealed class ListingOwnershipCheckTests
{
    private const string Api = "https://api.github.com";
    private const string Upstream = Api + "/repos/KSAModding/content-index";
    private const string Token = "ghu_secret";
    private const string Head = "c0ffee1";
    private const string ListingPath = "listings/MyMod.toml";
    private const string ListedForums = "https://forums.ahwoo.com/threads/my-mod.783/";
    private const string SubmittedForums = "https://forums.ahwoo.com/threads/my-new-mod.901/";
    private const string MarkerFile = "/contents/.github/ksa-content-index.toml";

    private static readonly GitHubAccount Alice = new("alice", 5);

    private readonly List<Sent> _sent = [];
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.Ordinal);

    public ListingOwnershipCheckTests()
    {
        On("POST", "https://github.com/login/device/code", () => Json("""{"device_code":"d","user_code":"WDJB-MJHT","verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}"""));
        On("POST", "https://github.com/login/oauth/access_token", () => Json($$"""{"access_token":"{{Token}}","token_type":"bearer","scope":"","expires_in":28800}"""));
        On("GET", Api + "/user", () => Json("""{"login":"octocat","id":1}"""));
    }

    [Theory]
    [InlineData(5L, "", null, ListingOwnershipProof.Owner)]
    [InlineData(1L, "", null, null)]
    [InlineData(99L, "ksa-index-alice", null, ListingOwnershipProof.Topic)]
    [InlineData(99L, "ksa-index-octocat", null, null)]
    [InlineData(99L, "", "login = \"Alice\"\nid = \"mymod\"\n", ListingOwnershipProof.MarkerFile)]
    [InlineData(99L, "", "account = \"alice\"\n", ListingOwnershipProof.MarkerFile)]
    [InlineData(99L, "", "login = \"alice\"\nlisting = \"Other\"\n", null)]
    [InlineData(99L, "", "login = \"octocat\"\n", null)]
    public async Task CheckAsync_AnotherLogin_ProvesItByItsOwnIdTopicAndMarker(long ownerId, string topic, string? marker, ListingOwnershipProof? proof)
    {
        On("GET", Api + "/repos/Studio/MyMod", () => Json(Repository("Studio/MyMod", ownerId)));
        On("GET", Api + "/repos/Studio/MyMod/topics", () => Json(JsonSerializer.Serialize(new { names = new[] { "ksa", topic } })));
        if (marker is not null)
            On("GET", Api + "/repos/Studio/MyMod/contents/.github/ksa-content-index.toml", () => Json(Content(marker)));
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft("Studio/MyMod"), null);

        Assert.Equal(
            proof is null
                ? new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.NoProof, Repository: "Studio/MyMod")
                : new ListingOwnership(ListingOwnershipState.Verified, proof, Repository: "Studio/MyMod"),
            ownership);
        Assert.All(_sent, sent => Assert.Null(sent.Authorization));
    }

    [Theory]
    [InlineData("alice", 7L, null)]
    [InlineData("alice-before-rename", 5L, ListingOwnershipProof.Owner)]
    public async Task CheckAsync_LoginWhoseAccountIdChanged_ProvesTheOwnerByTheIdOnly(string ownerLogin, long ownerId, ListingOwnershipProof? proof)
    {
        On("GET", Api + "/repos/Studio/MyMod", () => Json(Repository("Studio/MyMod", ownerId, ownerLogin: ownerLogin)));
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft("Studio/MyMod"), null);

        Assert.Equal(proof, ownership.Proof);
        Assert.Equal(proof is null ? ListingOwnershipProblem.NoProof : null, ownership.Problem);
    }

    [Fact]
    public async Task CheckAsync_MissingOrRenamedRepository_SaysWhichOne()
    {
        On("GET", Api + "/repos/alice/OldName", () => Redirect(Api + "/repositories/42"));
        On("GET", Api + "/repositories/42", () => Json(Repository("alice/NewName", 5)));
        var check = await SignedInAsync();

        var missing = await check.CheckAsync(Alice, Draft("alice/Gone"), null);
        var renamed = await check.CheckAsync(Alice, Draft("alice/OldName"), null);
        var noHost = await check.CheckAsync(Alice, new ListingDraft { Id = "MyMod" }, null);

        Assert.Equal(ListingOwnershipProblem.RepositoryMissing, missing.Problem);
        Assert.Equal(new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.RepositoryRenamed, Repository: "alice/OldName", RenamedTo: "alice/NewName"), renamed);
        Assert.Equal(new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.NoHost), noHost);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.UnavailableForLegalReasons)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task CheckAsync_HostDoesNotAnswer_CouldNotEvaluateAndKeepsTheSession(HttpStatusCode status)
    {
        On("GET", Api + "/repos/Studio/MyMod", () => Json("""{"message":"No"}""", status));
        var session = await SignInAsync();
        var check = Check(session);

        var ownership = await check.CheckAsync(Alice, Draft("Studio/MyMod"), null);

        Assert.Equal(ListingOwnership.Unknown, ownership);
        Assert.Equal(GitHubSessionStatus.SignedIn, session.State.Status);
    }

    [Fact]
    public async Task CheckAsync_SpaceDockMod_ProvesItThroughItsSourceCodeLink()
    {
        On("GET", "https://spacedock.info/api/mod/4253", () => Json("""{"id":4253,"game_id":22409,"source_code":"https://github.com/Studio/MyMod"}"""));
        OnProof("Studio/MyMod", proven: true);
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft(spaceDock: 4253), null);

        Assert.Equal(new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.Topic, Repository: "Studio/MyMod", SpaceDockMod: "4253"), ownership);
        Assert.Equal(new Uri("https://spacedock.info/mod/4253"), ownership.SpaceDockModUrl);
        Assert.Equal(new Uri("https://github.com/Studio/MyMod"), ownership.RepositoryUrl);
    }

    [Theory]
    [InlineData("""{"id":4253,"game_id":22409,"source_code":""}""", ListingOwnershipProblem.SpaceDockNoSourceLink)]
    [InlineData("""{"id":4253,"game_id":3102,"source_code":"https://github.com/Studio/MyMod"}""", ListingOwnershipProblem.SpaceDockModUnusable)]
    public async Task CheckAsync_SpaceDockModWithoutAUsableLink_NamesTheMod(string mod, ListingOwnershipProblem problem)
    {
        On("GET", "https://spacedock.info/api/mod/4253", () => Json(mod));
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft(spaceDock: 4253), null);

        Assert.Equal(new ListingOwnership(ListingOwnershipState.NotVerified, Problem: problem, SpaceDockMod: "4253"), ownership);
    }

    /// <summary>The fork cases of test_ownership.py in content-index (RFC 0079): the owner id and the topic prove a fork, its marker file never does.</summary>
    [Theory]
    [InlineData(5L, false, false, ListingOwnershipProof.Owner)]
    [InlineData(99L, true, false, ListingOwnershipProof.Topic)]
    [InlineData(99L, false, true, null)]
    [InlineData(99L, false, false, null)]
    public async Task CheckAsync_Fork_ProvesItByTheOwnerOrTheTopicButNotTheMarker(long ownerId, bool topic, bool marker, ListingOwnershipProof? proof)
    {
        OnRepository("alice/Forked", ownerId, fork: true, topic ? "ksa-index-alice" : "ksa", marker ? "login = \"alice\"\nid = \"MyMod\"\n" : null);
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft("alice/Forked"), null);

        Assert.Equal(
            proof is null
                ? new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.RepositoryFork, Repository: "alice/Forked")
                : new ListingOwnership(ListingOwnershipState.Verified, proof, Repository: "alice/Forked"),
            ownership);
        Assert.DoesNotContain(_sent, sent => sent.Url.EndsWith(MarkerFile, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CheckAsync_SameMarkerWhereItIsNoFork_Verifies()
    {
        OnRepository("alice/Original", 99, fork: false, "ksa", "login = \"alice\"\nid = \"MyMod\"\n");
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft("alice/Original"), null);

        Assert.Equal(new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.MarkerFile, Repository: "alice/Original"), ownership);
    }

    [Fact]
    public async Task CheckAsync_ForkWhoseTopicsCannotBeRead_CouldNotEvaluate()
    {
        On("GET", Api + "/repos/alice/Forked", () => Json(Repository("alice/Forked", 99, fork: true)));
        On("GET", Api + "/repos/alice/Forked/topics", () => Json("""{"message":"Server Error"}""", HttpStatusCode.BadGateway));
        var check = await SignedInAsync();

        Assert.Equal(ListingOwnership.Unknown, await check.CheckAsync(Alice, Draft("alice/Forked"), null));
    }

    /// <summary>The SpaceDock cases of test_ownership.py in content-index (RFC 0079): every GitHub proof on the repository the source code link names.</summary>
    [Theory]
    [InlineData(5L, false, false, false, ListingOwnershipProof.Owner)]
    [InlineData(99L, false, true, false, ListingOwnershipProof.Topic)]
    [InlineData(99L, false, false, true, ListingOwnershipProof.MarkerFile)]
    [InlineData(99L, false, false, false, null)]
    [InlineData(5L, true, false, false, ListingOwnershipProof.Owner)]
    [InlineData(99L, true, true, false, ListingOwnershipProof.Topic)]
    [InlineData(99L, true, false, true, null)]
    public async Task CheckAsync_SpaceDockMod_ProvesTheLinkedRepositoryAsAGitHubListing(long ownerId, bool fork, bool topic, bool marker, ListingOwnershipProof? proof)
    {
        OnSpaceDock("""{"id":4253,"game_id":22409,"source_code":"https://github.com/Studio/MyMod"}""");
        OnRepository("Studio/MyMod", ownerId, fork, topic ? "ksa-index-alice" : "ksa", marker ? "login = \"alice\"\nid = \"MyMod\"\n" : null);
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft(spaceDock: 4253), null);

        var problem = fork ? ListingOwnershipProblem.RepositoryFork : ListingOwnershipProblem.NoProof;
        Assert.Equal(
            proof is null
                ? new ListingOwnership(ListingOwnershipState.NotVerified, Problem: problem, Repository: "Studio/MyMod", SpaceDockMod: "4253")
                : new ListingOwnership(ListingOwnershipState.Verified, proof, Repository: "Studio/MyMod", SpaceDockMod: "4253"),
            ownership);
        Assert.Equal(["GET https://spacedock.info/api/mod/4253", "GET " + Api + "/repos/Studio/MyMod"], _sent.Take(2).Select(sent => sent.Line));
        Assert.All(_sent, sent => Assert.Null(sent.Authorization));
    }

    [Theory]
    [InlineData("https://github.com/Studio/MyMod.git")]
    [InlineData("https://github.com/Studio/MyMod/tree/main")]
    public async Task CheckAsync_SpaceDockLinkWithAGitSuffixOrADeeperPath_StillNamesTheRepository(string link)
    {
        OnSpaceDock(JsonSerializer.Serialize(new { id = 4253, game_id = 22409, source_code = link }));
        OnRepository("Studio/MyMod", 5, fork: false, "ksa", null);
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft(spaceDock: 4253), null);

        Assert.Equal(new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.Owner, Repository: "Studio/MyMod", SpaceDockMod: "4253"), ownership);
    }

    /// <summary>Each answer of SpaceDock that test_ownership.py reads as no usable mod or no usable link, with the truth of Python for the JSON values.</summary>
    [Theory]
    [InlineData("""{"id":4253,"game_id":22409}""", ListingOwnershipProblem.SpaceDockNoSourceLink)]
    [InlineData("""{"id":4253,"game_id":22409,"source_code":null}""", ListingOwnershipProblem.SpaceDockNoSourceLink)]
    [InlineData("""{"id":4253,"game_id":22409,"source_code":false}""", ListingOwnershipProblem.SpaceDockNoSourceLink)]
    [InlineData("""{"id":4253,"game_id":22409,"source_code":"https://gitlab.com/Studio/MyMod"}""", ListingOwnershipProblem.SpaceDockNoSourceLink)]
    [InlineData("""{"id":4253,"game_id":22409,"source_code":"https://github.com:443/Studio/MyMod"}""", ListingOwnershipProblem.SpaceDockNoSourceLink)]
    [InlineData("""{"id":4253,"game_id":22409,"source_code":"evil\n\n**Validated.** @stewards <!-- https://github.com/Studio/MyMod"}""", ListingOwnershipProblem.SpaceDockNoSourceLink)]
    [InlineData("""{"id":4253,"game_id":22409.0,"source_code":"https://gitlab.com/Studio/MyMod"}""", ListingOwnershipProblem.SpaceDockNoSourceLink)]
    [InlineData("""{"id":4253,"game_id":"22409","source_code":"https://github.com/Studio/MyMod"}""", ListingOwnershipProblem.SpaceDockModUnusable)]
    [InlineData("""{"error":true,"reason":"Mod not published. Authentication needed."}""", ListingOwnershipProblem.SpaceDockModUnusable)]
    [InlineData("""{"error":"yes","reason":"**Validated.** Not enough rights."}""", ListingOwnershipProblem.SpaceDockModUnusable)]
    public async Task CheckAsync_SpaceDockModWithoutAUsableLink_NeverAsksGitHub(string mod, ListingOwnershipProblem problem)
    {
        OnSpaceDock(mod);
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft(spaceDock: 4253), null);

        Assert.Equal(new ListingOwnership(ListingOwnershipState.NotVerified, Problem: problem, SpaceDockMod: "4253"), ownership);
        Assert.Equal(["GET https://spacedock.info/api/mod/4253"], _sent.Select(sent => sent.Line));
    }

    [Fact]
    public async Task CheckAsync_SpaceDockModThatIsNotThereOrRefusedWithItsErrorDocument_NamesTheMod()
    {
        var check = await SignedInAsync();
        var missing = await check.CheckAsync(Alice, Draft(spaceDock: 4253), null);
        OnSpaceDock("""{"error":true,"reason":"Mod not published. Authentication needed."}""", HttpStatusCode.Unauthorized);
        var refused = await check.CheckAsync(Alice, Draft(spaceDock: 4253), null);

        Assert.All([missing, refused], ownership => Assert.Equal(new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.SpaceDockModUnusable, SpaceDockMod: "4253"), ownership));
    }

    /// <summary>An answer that is not the mod's document, or no answer, gets no verdict, as in test_ownership.py.</summary>
    [Theory]
    [InlineData("""{"message":"ok"}""", HttpStatusCode.OK)]
    [InlineData("""{"id":4254,"game_id":22409,"source_code":"https://github.com/Studio/MyMod"}""", HttpStatusCode.OK)]
    [InlineData("""{"id":"4253.0","game_id":22409,"source_code":"https://github.com/Studio/MyMod"}""", HttpStatusCode.OK)]
    [InlineData("""{"id":4253,"game_id":22409,"source_code":5}""", HttpStatusCode.OK)]
    [InlineData("""<html>Bad gateway</html>""", HttpStatusCode.OK)]
    [InlineData("""[4253]""", HttpStatusCode.OK)]
    [InlineData("""{"message":"Forbidden"}""", HttpStatusCode.Forbidden)]
    [InlineData("""{"message":"Server Error"}""", HttpStatusCode.BadGateway)]
    public async Task CheckAsync_SpaceDockAnswerThatIsNotTheMod_CouldNotEvaluate(string answer, HttpStatusCode status)
    {
        OnSpaceDock(answer, status);
        OnRepository("Studio/MyMod", 5, fork: false, "ksa", null);
        var check = await SignedInAsync();

        Assert.Equal(ListingOwnership.Unknown, await check.CheckAsync(Alice, Draft(spaceDock: 4253), null));
    }

    [Fact]
    public async Task CheckAsync_SpaceDockThatTimesOut_CouldNotEvaluate()
    {
        On("GET", "https://spacedock.info/api/mod/4253", () => throw new HttpRequestException("timed out"));
        var check = await SignedInAsync();

        Assert.Equal(ListingOwnership.Unknown, await check.CheckAsync(Alice, Draft(spaceDock: 4253), null));
    }

    [Theory]
    [InlineData("/repos/Studio/MyMod")]
    [InlineData("/repos/Studio/MyMod/topics")]
    [InlineData("/repos/Studio/MyMod/contents/.github/ksa-content-index.toml")]
    public async Task CheckAsync_LinkedRepositoryThatDoesNotAnswer_CouldNotEvaluate(string path)
    {
        OnSpaceDock("""{"id":4253,"game_id":22409,"source_code":"https://github.com/Studio/MyMod"}""");
        OnRepository("Studio/MyMod", 99, fork: false, "ksa", "login = \"alice\"\n");
        On("GET", Api + path, () => Json("""{"message":"Server Error"}""", HttpStatusCode.BadGateway));
        var check = await SignedInAsync();

        Assert.Equal(ListingOwnership.Unknown, await check.CheckAsync(Alice, Draft(spaceDock: 4253), null));
    }

    [Fact]
    public async Task CheckAsync_LinkedRepositoryThatMoved_MakesTheLinkStale()
    {
        OnSpaceDock("""{"id":4253,"game_id":22409,"source_code":"https://github.com/Studio/MyMod"}""");
        On("GET", Api + "/repos/Studio/MyMod", () => Json(Repository("Studio/Renamed", 5)));
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft(spaceDock: 4253), null);

        Assert.Equal(new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.RepositoryRenamed, Repository: "Studio/MyMod", SpaceDockMod: "4253", RenamedTo: "Studio/Renamed"), ownership);
    }

    /// <summary>The fork and SpaceDock cases of VerifyChange in test_ownership.py: an edit proves the listed host, and the new one when it moves.</summary>
    [Theory]
    [InlineData(5L, false, ListingOwnershipState.Verified, null, ListingOwnershipProof.Owner)]
    [InlineData(99L, true, ListingOwnershipState.NotVerified, ListingOwnershipProblem.RepositoryFork, null)]
    public async Task CheckAsync_ChangeToTheNewNameOfARenamedFork_ChecksItAsAFork(long ownerId, bool marker, ListingOwnershipState state, ListingOwnershipProblem? problem, ListingOwnershipProof? proof)
    {
        On("GET", Api + "/repos/alice/Old", () => Json(Repository("alice/New", ownerId, fork: true)));
        OnRepository("alice/New", ownerId, fork: true, "ksa", marker ? "login = \"alice\"\nid = \"MyMod\"\n" : null);
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft("alice/New"), Draft("alice/Old"));

        Assert.Equal(new ListingOwnership(state, proof, problem, Repository: "alice/New"), ownership);
    }

    [Theory]
    [InlineData(true, 5L, false, ListingOwnershipState.Verified, "alice/MyMod")]
    [InlineData(false, 5L, false, ListingOwnershipState.NotVerified, "Original/MyMod")]
    [InlineData(true, 99L, true, ListingOwnershipState.NotVerified, "alice/MyMod")]
    public async Task CheckAsync_ChangeThatMovesTheListingToAFork_NeedsBothProofs(bool originalProven, long forkOwnerId, bool forkMarker, ListingOwnershipState state, string repository)
    {
        OnProof("Original/MyMod", originalProven);
        OnRepository("alice/MyMod", forkOwnerId, fork: true, "ksa", forkMarker ? "login = \"alice\"\nid = \"MyMod\"\n" : null);
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft("alice/MyMod"), Draft("Original/MyMod"));

        Assert.Equal((state, repository), (ownership.State, ownership.Repository));
        if (!originalProven)
            Assert.DoesNotContain(_sent, sent => sent.Url.StartsWith(Api + "/repos/alice/MyMod", StringComparison.Ordinal));
        if (forkMarker)
            Assert.Equal(ListingOwnershipProblem.RepositoryFork, ownership.Problem);
    }

    [Theory]
    [InlineData("https://github.com/Studio/MyMod", ListingOwnershipState.Verified, "Studio/MyMod")]
    [InlineData("https://github.com/Someone/MyMod", ListingOwnershipState.NotVerified, "Someone/MyMod")]
    public async Task CheckAsync_ChangeThatMovesTheAuthorityToSpaceDock_NeedsTheLinkedRepositoryProved(string link, ListingOwnershipState state, string repository)
    {
        OnRepository("Studio/MyMod", 5, fork: false, "ksa", null);
        OnRepository("Someone/MyMod", 99, fork: false, "ksa", null);
        OnSpaceDock(JsonSerializer.Serialize(new { id = 4253, game_id = 22409, source_code = link }));
        var check = await SignedInAsync();
        var listed = Draft("Studio/MyMod") with { Releases = new ListingReleases("Studio/MyMod", 4253, ListingAuthority.GitHub) };
        var submitted = listed with { Releases = new ListingReleases("Studio/MyMod", 4253, ListingAuthority.SpaceDock) };

        var ownership = await check.CheckAsync(Alice, submitted, listed);

        Assert.Equal((state, repository, "4253"), (ownership.State, ownership.Repository, ownership.SpaceDockMod));
    }

    [Theory]
    [InlineData(5L, ListingOwnershipState.Verified)]
    [InlineData(99L, ListingOwnershipState.NotVerified)]
    public async Task CheckAsync_EditOfASpaceDockListing_IsCheckedThroughItsLink(long ownerId, ListingOwnershipState state)
    {
        OnSpaceDock("""{"id":4253,"game_id":22409,"source_code":"https://github.com/Studio/MyMod"}""");
        OnRepository("Studio/MyMod", ownerId, fork: false, "ksa", null);
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft(spaceDock: 4253) with { Name = "Edited" }, Draft(spaceDock: 4253));

        Assert.Equal((state, "Studio/MyMod", "4253"), (ownership.State, ownership.Repository, ownership.SpaceDockMod));
    }

    [Fact]
    public async Task CheckAsync_ChangeOnTheSameHost_ChecksTheListedHost()
    {
        OnProof("studio/mymod", proven: true);
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft("Studio/MyMod") with { Name = "New name" }, Draft("studio/mymod"));

        Assert.Equal(ListingOwnershipState.Verified, ownership.State);
        Assert.DoesNotContain(_sent, sent => sent.Url == Api + "/repos/Studio/MyMod");
    }

    [Theory]
    [InlineData(true, true, ListingOwnershipState.Verified, "Studio/NewHost")]
    [InlineData(true, false, ListingOwnershipState.NotVerified, "Studio/NewHost")]
    [InlineData(false, true, ListingOwnershipState.NotVerified, "Studio/MyMod")]
    public async Task CheckAsync_ChangeThatMovesTheHost_NeedsBothProofs(bool listedProven, bool newProven, ListingOwnershipState state, string repository)
    {
        OnProof("Studio/MyMod", listedProven);
        OnProof("Studio/NewHost", newProven);
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft("Studio/NewHost"), Draft("Studio/MyMod"));

        Assert.Equal(state, ownership.State);
        Assert.Equal(repository, ownership.Repository);
    }

    [Fact]
    public async Task CheckAsync_ChangeToTheNewNameOfARenamedHost_ChecksTheNewName()
    {
        On("GET", Api + "/repos/Studio/OldName", () => Redirect(Api + "/repositories/42"));
        On("GET", Api + "/repositories/42", () => Json(Repository("Studio/NewName", 99)));
        OnProof("Studio/NewName", proven: true);
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft("Studio/NewName"), Draft("Studio/OldName"));

        Assert.Equal(new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.Topic, Repository: "Studio/NewName"), ownership);
    }

    [Theory]
    [InlineData(true, true, ListedForums)]
    [InlineData(true, false, null)]
    [InlineData(false, false, SubmittedForums)]
    public async Task CheckAsync_ForumsThread_IsTheListedOneForAnEdit(bool edit, bool listedThread, string? forums)
    {
        OnProof("Studio/MyMod", proven: false);
        var check = await SignedInAsync();
        var listed = Draft("Studio/MyMod") with { Links = listedThread ? [new ListingLink("forums", ListedForums)] : [] };
        var submitted = Draft("Studio/MyMod") with { Links = [new ListingLink("Forums", SubmittedForums)] };

        var ownership = await check.CheckAsync(Alice, submitted, edit ? listed : null);

        Assert.Equal(forums is null ? null : new Uri(forums), ownership.ForumsThread);
    }

    [Fact]
    public async Task CheckAsync_ForumsLinkThatIsNoThread_IsLeftOut()
    {
        OnProof("Studio/MyMod", proven: false);
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft("Studio/MyMod") with { Links = [new ListingLink("forums", "https://example.com/threads/my-mod.783/")] }, null);

        Assert.Null(ownership.ForumsThread);
    }

    [Fact]
    public async Task CheckPullRequestAsync_ListingThatDiffersBetweenBaseAndHead_ChecksTheListedHost()
    {
        OnListing(Head, Document("alice/Mine", SubmittedForums));
        OnListing("main", Document("Studio/MyMod", ListedForums));
        On("GET", Api + "/repos/alice/Mine", () => Json(Repository("alice/Mine", 5)));
        OnProof("Studio/MyMod", proven: false);
        var check = await SignedInAsync();

        var ownership = await check.CheckPullRequestAsync(Alice, ListingPath, "main", Head);

        Assert.Equal(
            new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.NoProof, Repository: "Studio/MyMod", ForumsThread: new Uri(ListedForums)),
            ownership);
        Assert.DoesNotContain(_sent, sent => sent.Url == Api + "/repos/alice/Mine");
        Assert.Equal(
            [("GET " + ListingUrl(Head), "Bearer " + Token), ("GET " + ListingUrl("main"), "Bearer " + Token)],
            _sent.Take(2).Select(sent => (sent.Line, sent.Authorization)));
        Assert.All(_sent.Skip(2), sent => Assert.Null(sent.Authorization));
    }

    [Theory]
    [InlineData(true, true, ListingOwnershipState.Verified, "Studio/NewHost")]
    [InlineData(true, false, ListingOwnershipState.NotVerified, "Studio/NewHost")]
    [InlineData(false, true, ListingOwnershipState.NotVerified, "Studio/MyMod")]
    public async Task CheckPullRequestAsync_HeadThatMovesTheHost_NeedsBothProofs(bool listedProven, bool newProven, ListingOwnershipState state, string repository)
    {
        OnListing(Head, Document("Studio/NewHost", ListedForums));
        OnListing("main", Document("Studio/MyMod", ListedForums));
        OnProof("Studio/MyMod", listedProven);
        OnProof("Studio/NewHost", newProven);
        var check = await SignedInAsync();

        var ownership = await check.CheckPullRequestAsync(Alice, ListingPath, "main", Head);

        Assert.Equal(state, ownership.State);
        Assert.Equal(repository, ownership.Repository);
    }

    [Fact]
    public async Task CheckPullRequestAsync_NewListing_ChecksTheHostOfTheHead()
    {
        OnListing(Head, Document("alice/Mine", SubmittedForums));
        On("GET", Api + "/repos/alice/Mine", () => Json(Repository("alice/Mine", 5)));
        var check = await SignedInAsync();

        var ownership = await check.CheckPullRequestAsync(Alice, ListingPath, "main", Head);

        Assert.Equal(new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.Owner, Repository: "alice/Mine", ForumsThread: new Uri(SubmittedForums)), ownership);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task CheckPullRequestAsync_DocumentMissingOrNotParsing_CouldNotEvaluate(bool headThere, bool headParses, bool listedParses)
    {
        if (headThere)
            OnListing(Head, headParses ? Document("Studio/MyMod", ListedForums) : "[releases");
        OnListing("main", listedParses ? Document("Studio/MyMod", ListedForums) : "[releases");
        OnProof("Studio/MyMod", proven: true);
        var check = await SignedInAsync();

        var ownership = await check.CheckPullRequestAsync(Alice, ListingPath, "main", Head);

        Assert.Equal(ListingOwnership.Unknown, ownership);
        Assert.DoesNotContain(_sent, sent => sent.Url.StartsWith(Api + "/repos/Studio/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CheckPullRequestAsync_ListedDocumentThatCannotBeRead_CouldNotEvaluate(bool serverError)
    {
        OnListing(Head, Document("Studio/MyMod", ListedForums));
        On("GET", ListingUrl("main"), () => serverError
            ? Json("""{"message":"Server Error"}""", HttpStatusCode.BadGateway)
            : Json(JsonSerializer.Serialize(new { sha = "b1", encoding = "base64", content = Convert.ToBase64String([0x69, 0x64, 0x20, 0x3d, 0xe9]) })));
        OnProof("Studio/MyMod", proven: true);
        var check = await SignedInAsync();

        var ownership = await check.CheckPullRequestAsync(Alice, ListingPath, "main", Head);

        Assert.Equal(ListingOwnership.Unknown, ownership);
    }

    [Fact]
    public async Task CheckPullRequestAsync_EscapesThePathAndTheCommit()
    {
        var check = await SignedInAsync();

        await check.CheckPullRequestAsync(Alice, "listings/My Mod#1.toml", "main", "abc?x");

        Assert.Equal(Upstream + "/contents/listings/My%20Mod%231.toml?ref=abc%3Fx", _sent.Single().Url);
    }

    [Theory]
    [InlineData("packs/MyPack/pack.toml")]
    [InlineData("listings/nested/MyMod.toml")]
    [InlineData("listings/.toml")]
    [InlineData("listings/MyMod.json")]
    [InlineData("index-status.toml")]
    public async Task CheckPullRequestAsync_NoListingDocument_SendsNothing(string path)
    {
        var check = await SignedInAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => check.CheckPullRequestAsync(Alice, path, "main", Head));

        Assert.Empty(_sent);
    }

    [Fact]
    public async Task CheckPullRequestAsync_SignedOut_Throws()
    {
        var check = Check(new GitHubSession(Http(), "Iv1.testclient", "borea-test"));

        var failure = await Assert.ThrowsAsync<ListingPublishException>(() => check.CheckPullRequestAsync(Alice, ListingPath, "main", Head));

        Assert.Equal(ListingPublishFailure.SignedOut, failure.Failure);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task CheckPullRequestAsync_TokenRefused_SignsOutAndThrows()
    {
        On("GET", ListingUrl(Head), () => Json("""{"message":"Bad credentials"}""", HttpStatusCode.Unauthorized));
        var session = await SignInAsync();
        var check = Check(session);

        var failure = await Assert.ThrowsAsync<ListingPublishException>(() => check.CheckPullRequestAsync(Alice, ListingPath, "main", Head));

        Assert.Equal(ListingPublishFailure.SignedOut, failure.Failure);
        Assert.Equal(GitHubSessionStatus.SignedOut, session.State.Status);
    }

    [Theory]
    [InlineData("User", false, "ksa-index-bob", "login = \"carol\"\n", "alice")]
    [InlineData("Organization", false, "ksa-index-carol,ksa-index-bob,ksa", "login = \"Alice\"\nid = \"mymod\"\n", "Alice,bob,carol")]
    [InlineData("Organization", false, "ksa-index-alice", "login = \"Alice\"\n", "alice")]
    [InlineData("Organization", false, "ksa-index-bob", "login = \"alice\"\nid = \"Other\"\n", "bob")]
    [InlineData("Organization", false, "ksa-index--bad", "login = \"not a login\"\n", "")]
    [InlineData("Organization", true, "ksa-index-bob", "login = \"alice\"\n", "bob")]
    [InlineData("User", true, "", null, "alice")]
    public async Task OwnersAsync_NamesWhoTheProofsNameOnTheHost(string ownerType, bool fork, string topics, string? marker, string owners)
    {
        On("GET", Api + "/repos/Studio/MyMod", () => Json(Repository("Studio/MyMod", 99, fork, ownerLogin: "alice", ownerType: ownerType)));
        On("GET", Api + "/repos/Studio/MyMod/topics", () => Json(JsonSerializer.Serialize(new { names = topics.Split(',', StringSplitOptions.RemoveEmptyEntries) })));
        if (marker is not null)
            On("GET", Api + "/repos/Studio/MyMod/contents/.github/ksa-content-index.toml", () => Json(Content(marker)));
        var check = await SignedInAsync();

        var found = await check.OwnersAsync(Draft("Studio/MyMod"));

        Assert.Equal(owners.Split(',', StringSplitOptions.RemoveEmptyEntries), found);
        Assert.All(_sent, sent => Assert.Null(sent.Authorization));
    }

    [Fact]
    public async Task OwnersAsync_SpaceDockMod_NamesTheOwnerOfItsSourceCodeRepository()
    {
        On("GET", "https://spacedock.info/api/mod/4253", () => Json("""{"id":4253,"game_id":22409,"source_code":"https://github.com/alice/MyMod"}"""));
        On("GET", Api + "/repos/alice/MyMod", () => Json(Repository("alice/MyMod", 5, ownerType: "User")));
        var check = await SignedInAsync();

        Assert.Equal(["alice"], await check.OwnersAsync(Draft(spaceDock: 4253)));
    }

    [Fact]
    public async Task OwnersAsync_SpaceDockModWithoutALink_NamesNobody()
    {
        OnSpaceDock("""{"id":4253,"game_id":22409,"source_code":null}""");
        var check = await SignedInAsync();

        Assert.Empty(await check.OwnersAsync(Draft(spaceDock: 4253)));
        Assert.Equal(["GET https://spacedock.info/api/mod/4253"], _sent.Select(sent => sent.Line));
    }

    [Fact]
    public async Task OwnersAsync_HostThatNamesNobodyOrDoesNotAnswer_NamesNobody()
    {
        On("GET", Api + "/repos/alice/OldName", () => Json(Repository("alice/NewName", 5, ownerType: "User")));
        On("GET", Api + "/repos/Studio/Down", () => Json(Repository("Studio/Down", 99, ownerType: "Organization")));
        On("GET", Api + "/repos/Studio/Down/topics", () => Json("""{"message":"Server Error"}""", HttpStatusCode.BadGateway));
        On("GET", Api + "/repos/Studio/Topics", () => Json(Repository("Studio/Topics", 99, ownerType: "Organization")));
        On("GET", Api + "/repos/Studio/Topics/topics", () => Json("""{"names":["ksa-index-bob"]}"""));
        On("GET", Api + "/repos/Studio/Topics/contents/.github/ksa-content-index.toml", () => Json("""{"message":"Server Error"}""", HttpStatusCode.BadGateway));
        var check = await SignedInAsync();

        Assert.Empty(await check.OwnersAsync(Draft("alice/Missing")));
        Assert.Empty(await check.OwnersAsync(Draft("alice/OldName")));
        Assert.Empty(await check.OwnersAsync(Draft("Studio/Down")));
        Assert.Equal(["bob"], await check.OwnersAsync(Draft("Studio/Topics")));
        Assert.Empty(await check.OwnersAsync(Draft()));
    }

    private async Task<ListingOwnershipCheck> SignedInAsync() => Check(await SignInAsync());

    private async Task<GitHubSession> SignInAsync()
    {
        var session = new GitHubSession(Http(), "Iv1.testclient", "borea-test", new InstantTimeProvider());
        Assert.True((await session.SignInAsync()).SignedIn);
        _sent.Clear();
        return session;
    }

    private ListingOwnershipCheck Check(IGitHubSession session) => new(session, Http(), new TableFormat());

    private HttpClient Http() => new(new FakeHttpMessageHandler(request => Task.FromResult(Respond(request))));

    private void On(string method, string url, Func<HttpResponseMessage> answer) => _routes[method + " " + url] = answer;

    private void OnProof(string repository, bool proven)
    {
        On("GET", $"{Api}/repos/{repository}", () => Json(Repository(repository, 99)));
        On("GET", $"{Api}/repos/{repository}/topics", () => Json(proven ? """{"names":["ksa-index-alice"]}""" : """{"names":[]}"""));
    }

    private void OnRepository(string repository, long ownerId, bool fork, string topic, string? marker)
    {
        On("GET", $"{Api}/repos/{repository}", () => Json(Repository(repository, ownerId, fork)));
        On("GET", $"{Api}/repos/{repository}/topics", () => Json(JsonSerializer.Serialize(new { names = new[] { topic } })));
        if (marker is not null)
            On("GET", $"{Api}/repos/{repository}{MarkerFile}", () => Json(Content(marker)));
    }

    private void OnSpaceDock(string answer, HttpStatusCode status = HttpStatusCode.OK) => On("GET", "https://spacedock.info/api/mod/4253", () => Json(answer, status));

    private void OnListing(string reference, string text) => On("GET", ListingUrl(reference), () => Json(Content(text)));

    private static string ListingUrl(string reference) => $"{Upstream}/contents/{ListingPath}?ref={Uri.EscapeDataString(reference)}";

    private HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var url = request.RequestUri!.AbsoluteUri;
        if (url.StartsWith(Api, StringComparison.Ordinal) || url.StartsWith("https://spacedock.info/", StringComparison.Ordinal))
            _sent.Add(new Sent(request.Method.Method, url, request.Headers.Authorization?.ToString()));

        return _routes.TryGetValue(request.Method.Method + " " + url, out var answer)
            ? answer()
            : Json("""{"message":"Not Found"}""", HttpStatusCode.NotFound);
    }

    private static ListingDraft Draft(string? github = null, long? spaceDock = null) => new()
    {
        Id = "MyMod",
        Name = "My Mod",
        Releases = new ListingReleases(github, spaceDock),
    };

    private static string Document(string github, string forums) =>
        $"id = \"MyMod\"\nname = \"My Mod\"\n[releases]\ngithub = \"{github}\"\n[links]\nforums = \"{forums}\"\n";

    private static string Repository(string fullName, long ownerId, bool fork = false, string? ownerLogin = null, string? ownerType = null) =>
        JsonSerializer.Serialize(new { full_name = fullName, fork, owner = new { id = ownerId, login = ownerLogin ?? fullName.Split('/')[0], type = ownerType } });

    private static string Content(string text) =>
        JsonSerializer.Serialize(new { sha = "b1", encoding = "base64", content = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).Insert(4, "\n") });

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently) { Content = new StringContent("""{"message":"Moved Permanently"}""") };
        response.Headers.Location = new Uri(location);
        return response;
    }

    private sealed record Sent(string Method, string Url, string? Authorization)
    {
        public string Line => Method + " " + Url;
    }

    /// <summary>Fires every delay at once, so the sign-in does not wait for its poll interval.</summary>
    private sealed class InstantTimeProvider : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
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
