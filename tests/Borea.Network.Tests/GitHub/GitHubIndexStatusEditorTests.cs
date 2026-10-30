using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Borea.Core.GitHub;
using Borea.Core.Listings;
using Borea.Core.Stewardship;
using Borea.Network.GitHub;

namespace Borea.Network.Tests.GitHub;

/// <summary>The steward octocat (id 1) edits index-status.toml of a content-index that this test keeps in memory.</summary>
public sealed partial class GitHubIndexStatusEditorTests
{
    private const string Api = "https://api.github.com";
    private const string Upstream = Api + "/repos/KSAModding/content-index";
    private const string Token = "ghu_secret";

    private const string Current = "# Steward-owned, see .github/CODEOWNERS. One entry per affected id.\n\nentries = []\n";

    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly List<Sent> _sent = [];
    private readonly FakeIndex _index = new();
    private readonly FakeRole _role = new(new StewardAccess("octocat", ContentIndex: true, ContentIndexReleases: false));
    private readonly Dictionary<string, Func<HttpResponseMessage>> _hosts = new(StringComparer.Ordinal);

    public GitHubIndexStatusEditorTests()
    {
        _index.Main["index-status.toml"] = Current;
        _index.Main["listings/MyMod.toml"] = "id = \"MyMod\"\nname = \"My Mod\"\n[releases]\ngithub = \"alice/MyMod\"\n";
        _index.Main["listings/Mine.toml"] = "id = \"Mine\"\nname = \"Mine\"\n[releases]\ngithub = \"octocat/Mine\"\n";
        _index.Main["packs/my-pack/1.0.0.toml"] = "id = \"my-pack\"\nversion = \"1.0.0\"\n";
        _index.Main["packs/my-pack/owner.json"] = """{ "github_login": "bob", "github_id": 7 }""";
        _hosts[Api + "/repos/alice/MyMod"] = () => Json("""{"full_name":"alice/MyMod","fork":false,"owner":{"id":5,"login":"alice","type":"User"}}""");
        _hosts[Api + "/repos/octocat/Mine"] = () => Json("""{"full_name":"octocat/Mine","fork":false,"owner":{"id":1,"login":"octocat","type":"User"}}""");
    }

    [Fact]
    public async Task OpenAsync_Delist_CommitsToAStewardBranchFromMainAndMentionsTheOwner()
    {
        var editor = await SignedInAsync();
        var main = _index.MainSha;

        var pull = await editor.OpenAsync(IndexStatusChange.Delist("mymod", "The author asked for it."));

        var expected = IndexStatusDocument.Parse(Current).Apply(IndexStatusChange.Delist("MyMod", "The author asked for it."), Contents(), Now).Text;
        Assert.Equal(new IndexStatusPullRequest(1, new Uri("https://github.com/KSAModding/content-index/pull/1"), "Delist MyMod", "octocat"), pull);
        Assert.Equal(main, _index.Branches["steward/delisted-mymod"].Base);
        Assert.Equal(expected, _index.Branches["steward/delisted-mymod"].Files["index-status.toml"]);
        Assert.Equal(Current, _index.Main["index-status.toml"]);
        var put = _index.Puts.Single();
        Assert.Equal(("Delist MyMod", "steward/delisted-mymod", _index.BlobSha(Current)), ((string)put["message"]!, (string)put["branch"]!, (string)put["sha"]!));
        var opened = _index.Pulls.Single();
        Assert.Equal(("Delist MyMod", "steward/delisted-mymod", "main"), (opened.Title, opened.Head, opened.Base));
        Assert.Equal("Sets `delisted` on `MyMod` in `index-status.toml`.\n\nReason: The author asked for it.\n\n@alice owns `MyMod`.", opened.Body);
        Assert.All(_sent.Where(sent => sent.Url.StartsWith(Upstream, StringComparison.Ordinal)), sent => Assert.Equal("Bearer " + Token, sent.Authorization));
        Assert.All(_sent.Where(sent => sent.Url == Api + "/repos/alice/MyMod"), sent => Assert.Null(sent.Authorization));
    }

    [Fact]
    public async Task OpenAsync_OwnListing_MentionsNobodyAndTheCheckWarns()
    {
        var editor = await SignedInAsync();

        var check = await editor.CheckAsync(IndexStatusChange.Dispute("Mine", "Claimed twice."));
        await editor.OpenAsync(IndexStatusChange.Dispute("Mine", "Claimed twice."));

        Assert.Null(check.Refusal);
        Assert.True(check.IsOwner);
        Assert.Empty(check.Owners);
        Assert.Empty(check.OpenPullRequests!);
        Assert.DoesNotContain("@", _index.Pulls.Single().Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(7L, "bob", true)]
    [InlineData(1L, "octocat-before-rename", true)]
    [InlineData(9L, "octocat", false)]
    public async Task CheckAsync_Pack_TakesTheOwnerFromItsOwnerRecord(long ownerId, string ownerLogin, bool isOwner)
    {
        _index.Main["packs/my-pack/owner.json"] = JsonSerializer.Serialize(new { github_login = ownerLogin, github_id = ownerId });
        if (ownerId == 7)
            _hosts[Api + "/user"] = () => Json("""{"login":"bob","id":7}""");
        var editor = await SignedInAsync(login: ownerId == 7 ? "bob" : "octocat");

        var check = await editor.CheckAsync(IndexStatusChange.Retract("my-pack", "1.0.0", "Broken."));

        Assert.Null(check.Refusal);
        Assert.Equal(isOwner, check.IsOwner);
        Assert.Equal(isOwner || ownerLogin == "octocat" ? [] : new[] { ownerLogin }, check.Owners);
    }

    [Fact]
    public async Task OwnedAsync_NamesTheIdsTheStewardOwns_AtOneCommit_AndWritesNothing()
    {
        _index.Main["packs/mine-pack/1.0.0.toml"] = "id = \"mine-pack\"\nversion = \"1.0.0\"\n";
        _index.Main["packs/mine-pack/owner.json"] = """{ "github_login": "octocat-before-rename", "github_id": 1 }""";
        var editor = await SignedInAsync();

        var owned = await editor.OwnedAsync(["mine", "MyMod", "my-pack", "mine-pack", "Missing", "MINE"]);

        Assert.Equal(["mine", "mine-pack"], owned);
        Assert.Single(_sent, sent => sent.Url == Upstream + "/git/ref/heads/main");
        Assert.All(_sent, sent => Assert.Equal("GET", sent.Method));
    }

    [Fact]
    public async Task OwnedAsync_SignedOut_IsRefusedBeforeAnyRequest()
    {
        var editor = Editor(new GitHubSession(Http(), "Iv1.testclient", "borea-test", new FixedTime(Now)));

        var failure = await Assert.ThrowsAsync<StewardException>(() => editor.OwnedAsync(["Mine"]));

        Assert.Equal(StewardFailure.SignedOut, failure.Failure);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task OpenAsync_PackOfSomebodyElse_MentionsTheOwnerOfItsRecord()
    {
        var editor = await SignedInAsync();

        await editor.OpenAsync(IndexStatusChange.Retract("my-pack", "1.0.0", "It installs a broken build."));

        Assert.Equal("Sets `retracted` on version 1.0.0 of `my-pack` in `index-status.toml`.\n\nReason: It installs a broken build.\n\n@bob owns `my-pack`.", _index.Pulls.Single().Body);
        Assert.Contains("steward/retracted-my-pack-1.0.0", _index.Branches.Keys);
    }

    [Theory]
    [InlineData("delisted", "Missing", null, IndexStatusRefusal.UnknownId)]
    [InlineData("retracted", "MyMod", "1.0.0", IndexStatusRefusal.NotAPack)]
    [InlineData("retracted", "my-pack", "9.9.9", IndexStatusRefusal.UnknownVersion)]
    [InlineData("retracted", "my-pack", null, IndexStatusRefusal.MissingVersion)]
    [InlineData("disputed", "MYMOD", null, IndexStatusRefusal.Duplicate)]
    public async Task OpenAsync_WhatTheStatusCheckRefuses_SendsNoWrite(string state, string id, string? version, IndexStatusRefusal refusal)
    {
        _index.Main["index-status.toml"] = IndexStatusDocument.Parse(Current).Apply(IndexStatusChange.Delist("MyMod", "Taken down."), Contents(), Now).Text;
        var editor = await SignedInAsync();
        var change = state switch
        {
            "delisted" => IndexStatusChange.Delist(id, "A reason."),
            "disputed" => IndexStatusChange.Dispute(id, "A reason."),
            _ => IndexStatusChange.Retract(id, version, "A reason."),
        };

        var check = await editor.CheckAsync(change);
        var refused = await Assert.ThrowsAsync<IndexStatusRefusedException>(() => editor.OpenAsync(change));

        Assert.Equal(refusal, check.Refusal);
        Assert.Equal(refusal, refused.Refusal);
        Assert.All(_sent, sent => Assert.Equal("GET", sent.Method));
        Assert.Empty(_index.Branches);
    }

    [Fact]
    public async Task CheckAsync_BeforeTheReasonIsTyped_ChecksAllButTheReason_AndOpenRefusesAnEmptyReason()
    {
        var editor = await SignedInAsync();

        var check = await editor.CheckAsync(IndexStatusChange.Delist("MyMod", string.Empty));
        var unknown = await editor.CheckAsync(IndexStatusChange.Delist("Missing", string.Empty));
        var refused = await Assert.ThrowsAsync<IndexStatusRefusedException>(() => editor.OpenAsync(IndexStatusChange.Delist("MyMod", " ")));

        Assert.Null(check.Refusal);
        Assert.Equal(["alice"], check.Owners);
        Assert.Equal(IndexStatusRefusal.UnknownId, unknown.Refusal);
        Assert.Equal(IndexStatusRefusal.InvalidReason, refused.Refusal);
        Assert.All(_sent, sent => Assert.Equal("GET", sent.Method));
        Assert.Empty(_index.Branches);
    }

    [Fact]
    public async Task OpenAsync_RetractOfAVersionShownWithoutItsBuildMetadata_NamesTheVersionAsItsFileSpellsIt()
    {
        _index.Main["packs/my-pack/1.2.0+build.5.toml"] = "id = \"my-pack\"\nversion = \"1.2.0+build.5\"\n";
        var editor = await SignedInAsync();

        var check = await editor.CheckAsync(IndexStatusChange.Retract("my-pack", "1.2.0", string.Empty));
        var pull = await editor.OpenAsync(IndexStatusChange.Retract("my-pack", "1.2.0", "Broken."));

        Assert.Null(check.Refusal);
        Assert.Equal("Retract my-pack 1.2.0+build.5", pull.Title);
        var branch = _index.Branches["steward/retracted-my-pack-1.2.0+build.5"];
        Assert.Equal("1.2.0+build.5", IndexStatusDocument.Parse(branch.Files["index-status.toml"]).Entries.Single().Version);
        Assert.StartsWith("Sets `retracted` on version 1.2.0+build.5 of `my-pack`", _index.Pulls.Single().Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAsync_NoSteward_IsRefusedBeforeAnyRequest()
    {
        _role.Access = new StewardAccess("octocat", ContentIndex: false, ContentIndexReleases: true);
        var editor = await SignedInAsync();

        var failure = await Assert.ThrowsAsync<StewardException>(() => editor.OpenAsync(IndexStatusChange.Delist("MyMod", "Taken down.")));

        Assert.Equal(StewardFailure.NotSteward, failure.Failure);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task OpenLeaveAsync_PackWithThreeVersionsOfWhichTwoPinTheMod_RetractsBothInOnePullRequest()
    {
        ThreeVersions();
        var editor = await SignedInAsync();
        var leave = new PackLeave("MY-PACK", "mymod", 12) { Reporter = new GitHubAccount("alice", 5) };

        var check = await editor.CheckLeaveAsync(leave);
        var pull = await editor.OpenLeaveAsync(leave);

        Assert.Equal((null, true), (check.Refusal, check.IsProven));
        Assert.Equal(["1.0.0", "1.2.0"], check.Versions);
        Assert.Empty(check.Retracted);
        Assert.Equal((ListingOwnershipProof.Owner, "alice/MyMod"), (check.Ownership.Proof, check.Ownership.Repository));
        Assert.Equal(["bob"], check.Owners);
        Assert.Equal("Retract every version of my-pack that pins MyMod", pull.Title);
        var branch = _index.Branches["steward/retracted-my-pack-without-mymod"];
        var listed = leave with { PackId = "my-pack", ModId = "MyMod" };
        var expected = IndexStatusDocument.Parse(Current)
            .Apply(listed.ChangeOf("1.0.0"), Contents(), Now)
            .Apply(listed.ChangeOf("1.2.0"), Contents(), Now);
        Assert.Equal(expected.Text, branch.Files["index-status.toml"]);
        Assert.Equal(
            [("my-pack", "1.0.0"), ("my-pack", "1.2.0")],
            IndexStatusDocument.Parse(branch.Files["index-status.toml"]).Entries.Select(entry => (entry.Id, entry.Version)));
        Assert.All(expected.Entries, entry => Assert.Equal("The author of MyMod asked to take it out of this pack in https://github.com/KSAModding/content-index/issues/12", entry.Reason));
        var opened = _index.Pulls.Single();
        Assert.Equal(("Retract every version of my-pack that pins MyMod", "steward/retracted-my-pack-without-mymod", "main"), (opened.Title, opened.Head, opened.Base));
        Assert.Equal(
            "Sets `retracted` on versions `1.0.0`, `1.2.0` of `my-pack` in `index-status.toml`, because each of them pins `MyMod`.\n\n"
            + "Reason: The author of MyMod asked to take it out of this pack in https://github.com/KSAModding/content-index/issues/12\n\n"
            + "@alice owns `MyMod` by the ownership proof of its listing.\n\nCloses #12\n\n@bob owns `my-pack`.",
            opened.Body);
        Assert.Single(_index.Puts);
    }

    [Fact]
    public async Task LeaveOfAReporterWhoDoesNotOwnTheListing_NeedsTheStewardToConfirmItByTheForumsAccount()
    {
        ThreeVersions();
        var editor = await SignedInAsync();
        var leave = new PackLeave("my-pack", "MyMod", 12) { Reporter = new GitHubAccount("mallory", 9) };

        var check = await editor.CheckLeaveAsync(leave);
        var refused = await Assert.ThrowsAsync<IndexStatusRefusedException>(() => editor.OpenLeaveAsync(leave));
        var writesBefore = _sent.Count(sent => sent.Method != "GET");
        await editor.OpenLeaveAsync(leave with { ConfirmedByForums = true });

        Assert.Null(check.Refusal);
        Assert.False(check.IsProven);
        Assert.Equal((ListingOwnershipState.NotVerified, ListingOwnershipProblem.NoProof), (check.Ownership.State, check.Ownership.Problem));
        Assert.Equal(new Uri("https://forums.ahwoo.com/threads/my-mod.42/"), check.Ownership.ForumsThread);
        Assert.Equal(["1.0.0", "1.2.0"], check.Versions);
        Assert.Equal(IndexStatusRefusal.NotConfirmed, refused.Refusal);
        Assert.Equal(0, writesBefore);
        Assert.Contains("A steward confirmed by the forums account of its listing that @mallory owns `MyMod`.", _index.Pulls.Single().Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LeaveOfAPackThatNoLongerPinsTheMod_IsRefused_AndWritesNothing()
    {
        _index.Main["packs/my-pack/1.0.0.toml"] = "id = \"my-pack\"\nversion = \"1.0.0\"\n[[mods]]\nid = \"MyMod\"\nversion = \"1.0.0\"\n";
        _index.Main["packs/my-pack/1.1.0.toml"] = "id = \"my-pack\"\nversion = \"1.1.0\"\n[[mods]]\nid = \"Other\"\nversion = \"2.0.0\"\n";
        _index.Main["index-status.toml"] = IndexStatusDocument.Parse(Current).Apply(IndexStatusChange.Retract("my-pack", "1.0.0", "Broken."), Contents(), Now).Text;
        var editor = await SignedInAsync();
        var leave = new PackLeave("my-pack", "MyMod", 12) { Reporter = new GitHubAccount("alice", 5) };

        var check = await editor.CheckLeaveAsync(leave);
        var refused = await Assert.ThrowsAsync<IndexStatusRefusedException>(() => editor.OpenLeaveAsync(leave with { ConfirmedByForums = true }));
        var unknown = await editor.CheckLeaveAsync(leave with { PackId = "missing-pack" });

        Assert.Equal(IndexStatusRefusal.NotPinned, check.Refusal);
        Assert.Empty(check.Versions);
        Assert.Equal(["1.0.0"], check.Retracted);
        Assert.Equal(IndexStatusRefusal.NotPinned, refused.Refusal);
        Assert.Equal(IndexStatusRefusal.NotAPack, unknown.Refusal);
        Assert.All(_sent, sent => Assert.Equal("GET", sent.Method));
        Assert.Empty(_index.Branches);
    }

    [Fact]
    public async Task PackMembersAsync_NamesEachPinnedModOnce_AndAPackThatIsNotThereNone()
    {
        ThreeVersions();
        var editor = await SignedInAsync();

        var members = await editor.PackMembersAsync("my-pack");
        var none = await editor.PackMembersAsync("missing-pack");

        Assert.Equal(["MyMod", "Other"], members);
        Assert.Empty(none);
        Assert.All(_sent, sent => Assert.Equal("GET", sent.Method));
    }

    [Fact]
    public async Task OpenAsync_BranchTaken_TakesTheNextFreeName()
    {
        _index.Branches["steward/delisted-mymod"] = new FakeBranch("old", new Dictionary<string, string>(_index.Main));
        var editor = await SignedInAsync();

        await editor.OpenAsync(IndexStatusChange.Delist("MyMod", "Taken down."));

        Assert.Equal("steward/delisted-mymod-2", _index.Pulls.Single().Head);
    }

    [Fact]
    public async Task ReadAsync_ListsTheEntriesOnMain_AndALiftOpensItsPullRequest()
    {
        _index.Main["index-status.toml"] = IndexStatusDocument.Parse(Current).Apply(IndexStatusChange.Delist("MyMod", "Taken down."), Contents(), Now).Text;
        var editor = await SignedInAsync();

        var overview = await editor.ReadAsync();
        await editor.OpenAsync(IndexStatusChange.Lift(overview.Entries.Single(), "The takedown did not hold."));

        Assert.Equal([new IndexStatusEntry("MyMod", "delisted", null, "2026-09-25T12:00:00Z", "Taken down.")], overview.Entries);
        Assert.Empty(overview.OpenPullRequests);
        Assert.Equal(Current, _index.Branches["steward/lift-mymod"].Files["index-status.toml"]);
        Assert.Equal(("Lift MyMod", "Lifts `delisted` from `MyMod` in `index-status.toml`.\n\nReason: The takedown did not hold.\n\n@alice owns `MyMod`."), (_index.Pulls.Single().Title, _index.Pulls.Single().Body));
    }

    [Fact]
    public async Task TwoStatusPullRequests_TheSecondIsWarnedAndConflictsOnceTheFirstMerges()
    {
        var editor = await SignedInAsync();
        _index.Pulls.Add(new FakePull(90, "List Other", "listing-other", "main", "alice", ["listings/Other.toml"]));

        var first = await editor.OpenAsync(IndexStatusChange.Delist("MyMod", "Taken down."));
        var check = await editor.CheckAsync(IndexStatusChange.Retract("my-pack", "1.0.0", "Broken."));
        var second = await editor.OpenAsync(IndexStatusChange.Retract("my-pack", "1.0.0", "Broken."));
        var beforeMerge = await editor.ReadAsync();
        _index.Merge(first.Number);
        var afterMerge = await editor.ReadAsync();

        Assert.Equal([first], check.OpenPullRequests);
        Assert.Equal([first with { Conflicts = false }, second with { Conflicts = false }], beforeMerge.OpenPullRequests);
        Assert.Equal([second with { Conflicts = true }], afterMerge.OpenPullRequests);
        Assert.Equal("MyMod", afterMerge.Entries.Single().Id);
    }

    [Fact]
    public async Task CheckAsync_OpenPullRequestsThatCannotBeRead_AreUnknownAndTheCheckStillAnswers()
    {
        _hosts[Upstream + "/pulls?state=open&base=main&per_page=100&page=1"] = () => Json("""{"message":"Server Error"}""", HttpStatusCode.BadGateway);
        var editor = await SignedInAsync();

        var check = await editor.CheckAsync(IndexStatusChange.Delist("MyMod", "Taken down."));

        Assert.Null(check.OpenPullRequests);
        Assert.Equal(["alice"], check.Owners);
    }

    [Fact]
    public async Task ReadAsync_FileInAFormBoreaDoesNotEdit_SaysSo()
    {
        _index.Main["index-status.toml"] = "entries = [{ id = \"MyMod\", state = \"delisted\" }]\n";
        var editor = await SignedInAsync();

        var failure = await Assert.ThrowsAsync<StewardException>(() => editor.ReadAsync());

        Assert.Equal(StewardFailure.UnreadableFile, failure.Failure);
    }

    [Fact]
    public async Task OpenAsync_Unauthorized_SignsOutAndWritesNothing()
    {
        _hosts[Upstream + "/git/ref/heads/main"] = () => Json("""{"message":"Bad credentials"}""", HttpStatusCode.Unauthorized);
        var session = await SignInAsync();
        var editor = Editor(session);

        var failure = await Assert.ThrowsAsync<StewardException>(() => editor.OpenAsync(IndexStatusChange.Delist("MyMod", "Taken down.")));

        Assert.Equal(StewardFailure.SignedOut, failure.Failure);
        Assert.Equal(GitHubSessionStatus.SignedOut, session.State.Status);
        Assert.Empty(_index.Branches);
    }

    private IndexContents Contents() => IndexContents.FromPaths(_index.Main.Keys);

    /// <summary>my-pack in three versions, of which 1.0.0 and 1.2.0 pin MyMod, whose listing has a forums thread.</summary>
    private void ThreeVersions()
    {
        _index.Main["listings/MyMod.toml"] = "id = \"MyMod\"\nname = \"My Mod\"\n[releases]\ngithub = \"alice/MyMod\"\n[links]\nforums = \"https://forums.ahwoo.com/threads/my-mod.42/\"\n";
        _index.Main["packs/my-pack/1.0.0.toml"] = "id = \"my-pack\"\nversion = \"1.0.0\"\n[[mods]]\nid = \"MyMod\"\nversion = \"1.0.0\"\n[[mods]]\nid = \"Other\"\nversion = \"2.0.0\"\n";
        _index.Main["packs/my-pack/1.1.0.toml"] = "id = \"my-pack\"\nversion = \"1.1.0\"\n[[mods]]\nid = \"Other\"\nversion = \"2.0.0\"\n";
        _index.Main["packs/my-pack/1.2.0.toml"] = "id = \"my-pack\"\nversion = \"1.2.0\"\n[[mods]]\nid = \"MyMod\"\nversion = \"1.1.0\"\n";
    }

    private async Task<GitHubIndexStatusEditor> SignedInAsync(string login = "octocat")
    {
        _role.Access = _role.Access with { Login = login };
        return Editor(await SignInAsync(login));
    }

    private GitHubIndexStatusEditor Editor(IGitHubSession session) => new(session, _role, Http(), new TableFormat(), new FixedTime(Now));

    private async Task<GitHubSession> SignInAsync(string login = "octocat")
    {
        _hosts.TryAdd(Api + "/user", () => Json($$"""{"login":"{{login}}","id":1}"""));
        var session = new GitHubSession(Http(), "Iv1.testclient", "borea-test", new FixedTime(Now));
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
            _sent.Add(new Sent(request.Method.Method, url, request.Headers.Authorization?.ToString()));

        if (url == "https://github.com/login/device/code")
            return Json("""{"device_code":"d","user_code":"WDJB-MJHT","verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}""");
        if (url == "https://github.com/login/oauth/access_token")
            return Json($$"""{"access_token":"{{Token}}","token_type":"bearer"}""");
        if (request.Method == HttpMethod.Get && _hosts.TryGetValue(url, out var answer))
            return answer();

        return url.StartsWith(Upstream + "/", StringComparison.Ordinal)
            ? _index.Respond(request.Method.Method, url[(Upstream.Length + 1)..], body)
            : Json("""{"message":"Not Found"}""", HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed record Sent(string Method, string Url, string? Authorization);

    private sealed record FakeBranch(string Base, Dictionary<string, string> Files);

    private sealed record FakePull(int Number, string Title, string Head, string Base, string Author, List<string> Changed)
    {
        public string Body { get; init; } = string.Empty;

        public bool Open { get; set; } = true;

        public bool? Mergeable { get; set; } = true;
    }

    /// <summary>The main branch, the steward branches and the pull requests of content-index, as the REST API answers them.</summary>
    private sealed partial class FakeIndex
    {
        public Dictionary<string, string> Main { get; private set; } = new(StringComparer.Ordinal);

        public Dictionary<string, FakeBranch> Branches { get; } = new(StringComparer.Ordinal);

        public List<FakePull> Pulls { get; } = [];

        public List<JsonObject> Puts { get; } = [];

        public string MainSha => Sha(string.Join('\0', Main.OrderBy(file => file.Key, StringComparer.Ordinal).Select(file => file.Key + "=" + file.Value)));

        public string BlobSha(string text) => Sha("blob " + text);

        /// <summary>Merges the pull request into main, after which every other open one that changes one of its files conflicts.</summary>
        public void Merge(int number)
        {
            var pull = Pulls.Single(item => item.Number == number);
            Main = new Dictionary<string, string>(Branches[pull.Head].Files, StringComparer.Ordinal);
            pull.Open = false;
            foreach (var other in Pulls.Where(item => item.Open && item.Changed.Intersect(pull.Changed).Any()))
                other.Mergeable = false;
        }

        public HttpResponseMessage Respond(string method, string path, string? body)
        {
            if (method == "GET" && path == "git/ref/heads/main")
                return Json(JsonSerializer.Serialize(new { @ref = "refs/heads/main", @object = new { sha = MainSha } }));
            if (method == "GET" && path.StartsWith("git/ref/heads/", StringComparison.Ordinal))
                return Branches.ContainsKey(path["git/ref/heads/".Length..]) ? Json("{}") : Json("""{"message":"Not Found"}""", HttpStatusCode.NotFound);
            if (method == "GET" && path == $"git/trees/{MainSha}?recursive=1")
                return Json(JsonSerializer.Serialize(new { truncated = false, tree = Main.Keys.Select(file => new { path = file, type = "blob" }) }));
            if (method == "GET" && ContentPath().Match(path) is { Success: true } content && content.Groups["ref"].Value == MainSha)
            {
                var file = Uri.UnescapeDataString(content.Groups["path"].Value);
                return Main.TryGetValue(file, out var text)
                    ? Json(JsonSerializer.Serialize(new { sha = BlobSha(text), encoding = "base64", content = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) }))
                    : Json("""{"message":"Not Found"}""", HttpStatusCode.NotFound);
            }

            if (method == "POST" && path == "git/refs")
            {
                var created = JsonNode.Parse(body!)!;
                var name = ((string)created["ref"]!)["refs/heads/".Length..];
                Assert.Equal(MainSha, (string?)created["sha"]);
                Branches.Add(name, new FakeBranch(MainSha, new Dictionary<string, string>(Main, StringComparer.Ordinal)));
                return Json("{}", HttpStatusCode.Created);
            }

            if (method == "PUT" && path == "contents/index-status.toml")
            {
                var put = JsonNode.Parse(body!)!.AsObject();
                Puts.Add(put);
                var branch = Branches[(string)put["branch"]!];
                Assert.Equal(BlobSha(branch.Files["index-status.toml"]), (string?)put["sha"]);
                branch.Files["index-status.toml"] = Encoding.UTF8.GetString(Convert.FromBase64String((string)put["content"]!));
                return Json("{}");
            }

            if (method == "POST" && path == "pulls")
            {
                var request = JsonNode.Parse(body!)!;
                var pull = new FakePull(Pulls.Count(item => item.Number < 90) + 1, (string)request["title"]!, (string)request["head"]!, (string)request["base"]!, "octocat", ["index-status.toml"])
                {
                    Body = (string)request["body"]!,
                };
                Pulls.Add(pull);
                return Json(Pull(pull), HttpStatusCode.Created);
            }

            if (method == "GET" && path == "pulls?state=open&base=main&per_page=100&page=1")
                return Json("[" + string.Join(',', Pulls.Where(item => item.Open).Select(item => Pull(item, withMergeable: false))) + "]");
            if (method == "GET" && PullPath().Match(path) is { Success: true } single)
            {
                var pull = Pulls.Single(item => item.Number == int.Parse(single.Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture));
                return single.Groups["files"].Success
                    ? Json(JsonSerializer.Serialize(pull.Changed.Select(file => new { filename = file })))
                    : Json(Pull(pull));
            }

            return Json("""{"message":"Not Found"}""", HttpStatusCode.NotFound);
        }

        private static string Pull(FakePull pull, bool withMergeable = true) => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["number"] = pull.Number,
            ["title"] = pull.Title,
            ["html_url"] = $"https://github.com/KSAModding/content-index/pull/{pull.Number}",
            ["user"] = new { login = pull.Author, id = 1 },
            ["mergeable"] = withMergeable ? pull.Mergeable : null,
        });

        private static string Sha(string text) => Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(text)));

        [GeneratedRegex(@"^contents/(?<path>[^?]+)\?ref=(?<ref>[0-9a-f]+)$")]
        private static partial Regex ContentPath();

        [GeneratedRegex(@"^pulls/(?<number>[0-9]+)(?<files>/files\?per_page=100&page=1)?$")]
        private static partial Regex PullPath();
    }

    private sealed class FakeRole(StewardAccess access) : IStewardRole
    {
        public StewardAccess Access { get; set; } = access;

        public StewardAccess? Current => Access;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public Task<StewardAccess?> CheckAsync(CancellationToken cancellationToken = default) => Task.FromResult<StewardAccess?>(Access);
    }

    /// <summary>Reads <c>key = "value"</c> lines under <c>[table]</c> and <c>[[array]]</c> headers.</summary>
    private sealed class TableFormat : IListingFormat
    {
        public string Write(AuthoredTable document, string? original = null) => throw new NotSupportedException();

        public AuthoredTable Read(string text)
        {
            var root = new AuthoredTable();
            var table = root;
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("[[", StringComparison.Ordinal))
                {
                    table = new AuthoredTable();
                    var key = line[2..^2];
                    var list = root.GetList(key) is List<object> existing ? existing : [];
                    list.Add(table);
                    root.Set(key, list);
                    continue;
                }

                if (line.StartsWith('['))
                {
                    table = new AuthoredTable();
                    root.Set(line[1..^1], table);
                    continue;
                }

                var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                table.Set(parts[0], parts[1].Trim('"'));
            }

            return root;
        }
    }

    /// <summary>A clock that stands still and fires every delay at once, so the sign-in does not wait for its poll interval.</summary>
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ThreadPool.QueueUserWorkItem(_ => callback(state));
            return TimeProvider.System.CreateTimer(_ => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }
}
