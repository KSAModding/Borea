using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Borea.Core.GitHub;
using Borea.Core.Stewardship;
using Borea.Network.GitHub;
using Borea.Network.Tests.Listings;

namespace Borea.Network.Tests.GitHub;

/// <summary>
/// The steward octocat (id 1) amends release files of a content-index-releases that this test keeps in memory,
/// with the cases of tools/amendment-vectors.json, whose written text is the byte for byte result of tools/amend.py.
/// </summary>
public sealed partial class GitHubReleaseAmendmentsTests
{
    private const string Api = "https://api.github.com";
    private const string Upstream = Api + "/repos/KSAModding/content-index-releases";
    private const string Token = "ghu_secret";
    private const string Folder = "releases/ExampleMod/";

    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly JsonObject Vectors = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "GitHub", "Fixtures", "amendment-vectors.json")))!.AsObject();

    private readonly List<Sent> _sent = [];
    private readonly FakeReleases _releases = new();
    private readonly FakeRole _role = new(new StewardAccess("octocat", ContentIndex: false, ContentIndexReleases: true));
    private readonly Dictionary<string, Func<HttpResponseMessage>> _hosts = new(StringComparer.Ordinal);

    public GitHubReleaseAmendmentsTests()
    {
        _releases.Main["game-versions.json"] = JsonSerializer.Serialize(new { spec_version = 1, versions = Vectors["game_versions"] });
        _releases.Main["README.md"] = "# Releases\n";
        Listing("alice/ExampleMod");
        _hosts[Api + "/repos/alice/ExampleMod"] = () => Json("""{"full_name":"alice/ExampleMod","fork":false,"owner":{"id":5,"login":"alice","type":"User"}}""");
        _hosts[Api + "/repos/octocat/ExampleMod"] = () => Json("""{"full_name":"octocat/ExampleMod","fork":false,"owner":{"id":1,"login":"octocat","type":"User"}}""");
    }

    [Theory]
    [InlineData("a game_max is added after game_min_revision")]
    [InlineData("a steward yanks a release on a report")]
    public async Task OpenAsync_OneRelease_CommitsTheBytesOfToolsAmendToAStewardBranchFromMain(string name)
    {
        var vector = Vector(name);
        _releases.Main[Folder + "1.2.0.json"] = (string)vector["base"]!;
        _releases.Main[Folder + "1.1.0.json"] = Version((string)vector["base"]!, "1.1.0");
        var amendments = await SignedInAsync();
        var main = _releases.MainSha;
        var options = vector["amendment"]!.AsObject();
        var change = new ReleaseChange { GameMax = (string?)options["game-max"], Yank = options["yank"] is not null };
        var reason = (string?)options["reason"] ?? "It breaks on the new build.";

        var preview = await amendments.PreviewAsync(new ReleaseAmendmentRequest("ExampleMod", ReleaseSelection.Of("1.2.0"), change, reason));
        var pull = await amendments.OpenAsync(preview);

        var written = (string)vector["written"]!;
        var file = Assert.Single(preview.Files);
        Assert.Equal(("1.2.0", Folder + "1.2.0.json", (string)vector["base"]!, written), (file.Version, file.Path, file.Before, file.After));
        Assert.Equal(new ReleaseAmendmentPullRequest(1, new Uri("https://github.com/KSAModding/content-index-releases/pull/1"), "Amend ExampleMod 1.2.0"), pull);
        var branch = _releases.Branches["steward/amend-examplemod"];
        Assert.Equal(main, branch.Parent);
        Assert.Equal(Encoding.UTF8.GetBytes(written), Encoding.UTF8.GetBytes(branch.Files[Folder + "1.2.0.json"]));
        Assert.Equal(
            _releases.Main.Where(item => item.Key != Folder + "1.2.0.json").OrderBy(item => item.Key, StringComparer.Ordinal),
            branch.Files.Where(item => item.Key != Folder + "1.2.0.json").OrderBy(item => item.Key, StringComparer.Ordinal));
        Assert.Equal((string)vector["base"]!, _releases.Main[Folder + "1.2.0.json"]);
        Assert.Equal("Amend ExampleMod 1.2.0", branch.Message);
        var opened = _releases.Pulls.Single();
        Assert.Equal(("Amend ExampleMod 1.2.0", "steward/amend-examplemod", "main", preview.Body), (opened.Title, opened.Head, opened.Base, opened.Body));
        Assert.Contains($"\n\nReason: {reason}\n\n", opened.Body, StringComparison.Ordinal);
        Assert.EndsWith("\n\n@alice owns `ExampleMod`.", opened.Body, StringComparison.Ordinal);
        Assert.All(_sent.Where(sent => sent.Url.StartsWith(Upstream, StringComparison.Ordinal)), sent => Assert.Equal("Bearer " + Token, sent.Authorization));
    }

    [Fact]
    public async Task OpenAsync_UpToAVersion_AmendsEachReleaseAtOrBelowItInOneCommit()
    {
        var vector = Vector("a game_max is added after game_min_revision");
        foreach (var version in (string[])["1.0.0", "1.1.0", "1.2.0", "1.10.0", "2.0.0"])
            _releases.Main[Folder + version + ".json"] = Version((string)vector["base"]!, version);
        _releases.Main[Folder + "1.10.0.json"] = Version((string)vector["written"]!, "1.10.0");
        var amendments = await SignedInAsync();

        var preview = await amendments.PreviewAsync(new ReleaseAmendmentRequest("ExampleMod", ReleaseSelection.UpTo("1.10.0"), new ReleaseChange { GameMax = "2026.8.19.5261" }, "It breaks on 2026.8.22."));
        var pull = await amendments.OpenAsync(preview);

        Assert.Equal(["1.10.0", "1.2.0", "1.1.0", "1.0.0"], preview.Files.Select(file => file.Version));
        Assert.Null(preview.Files[0].After);
        Assert.Equal("Amend ExampleMod 1.0.0, 1.1.0, 1.2.0", pull.Title);
        var branch = _releases.Branches["steward/amend-examplemod"];
        foreach (var version in (string[])["1.0.0", "1.1.0", "1.2.0"])
            Assert.Equal(Version((string)vector["written"]!, version), branch.Files[Folder + version + ".json"]);
        Assert.Equal(_releases.Main[Folder + "2.0.0.json"], branch.Files[Folder + "2.0.0.json"]);
        Assert.Equal(3, _sent.Count(sent => sent.Line == "POST " + Upstream + "/git/blobs"));
        Assert.Single(_sent, sent => sent.Line == "POST " + Upstream + "/git/commits");
        Assert.Contains("--up-to 1.10.0 --game-max 2026.8.19.5261", _releases.Pulls.Single().Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a steward alone cannot raise a game_max", ReleaseAmendmentRefusal.Widens)]
    [InlineData("a steward alone cannot lower a dependency min", ReleaseAmendmentRefusal.Widens)]
    [InlineData("a game_max below the game_min is an empty range", ReleaseAmendmentRefusal.OutsideClass)]
    [InlineData("naming no change is refused", ReleaseAmendmentRefusal.InvalidChange)]
    public async Task PreviewAndOpen_AChangeTheClassRefuses_SendNoWrite(string name, ReleaseAmendmentRefusal refusal)
    {
        var vector = Vector(name);
        _releases.Main[Folder + "1.2.0.json"] = (string)vector["base"]!;
        var amendments = await SignedInAsync();
        var options = vector["amendment"]!.AsObject();
        var change = new ReleaseChange
        {
            GameMin = (string?)options["game-min"],
            GameMax = (string?)options["game-max"],
            DependencyBounds = [.. options["dependency-min"]?.AsArray().Select(bound => ((string)bound!).Split('=')).Select(pair => new ReleaseDependencyBounds(pair[0], pair[1], null)) ?? []],
        };
        var request = new ReleaseAmendmentRequest("ExampleMod", ReleaseSelection.Of("1.2.0"), change, "A reason.");
        var forged = new ReleaseAmendmentPreview(request, [new ReleaseFilePreview("1.2.0", Folder + "1.2.0.json", (string)vector["base"]!, "{}\n")], []);

        var previewed = await Assert.ThrowsAsync<ReleaseAmendmentRefusedException>(() => amendments.PreviewAsync(request));
        var opened = await Assert.ThrowsAsync<ReleaseAmendmentRefusedException>(() => amendments.OpenAsync(forged));

        Assert.Equal((refusal, refusal), (previewed.Refusal, opened.Refusal));
        Assert.All(_sent, sent => Assert.Equal("GET", sent.Method));
        Assert.Empty(_releases.Branches);
        Assert.Empty(_releases.Pulls);
    }

    /// <summary>The widenings of RFC 0079 in tools/amendment-vectors.json, each refused to a steward alone and written for the owner.</summary>
    [Theory]
    [InlineData("the owner lowers a game_min")]
    [InlineData("the owner raises a game_max")]
    [InlineData("the owner lowers a loader min")]
    [InlineData("the owner raises a loader max")]
    [InlineData("the owner lowers a dependency min")]
    [InlineData("the owner raises a dependency max")]
    public async Task OpenAsync_AWideningOnTheAuthorsRequest_CommitsTheBytesOfTheOwner_AndNamesTheRequest(string name)
    {
        const string Link = "https://github.com/KSAModding/content-index/issues/42#issuecomment-7";
        var vector = Vector(name);
        _releases.Main[Folder + "1.2.0.json"] = (string)vector["base"]!;
        var amendments = await SignedInAsync();
        var alone = new ReleaseAmendmentRequest("ExampleMod", ReleaseSelection.Of("1.2.0"), ChangeOf(vector["amendment"]!.AsObject()), "The author tested it.");

        var refused = await Assert.ThrowsAsync<ReleaseAmendmentRefusedException>(() => amendments.PreviewAsync(alone));
        var preview = await amendments.PreviewAsync(alone with { AuthorRequest = Link });
        await amendments.OpenAsync(preview);

        Assert.Equal(ReleaseAmendmentRefusal.Widens, refused.Refusal);
        Assert.Equal((string)vector["written"]!, _releases.Branches["steward/amend-examplemod"].Files[Folder + "1.2.0.json"]);
        var body = _releases.Pulls.Single().Body;
        Assert.Contains($"\n\nReason: The author tested it.\n\nRequested by the author: <{Link}>\n\n", body, StringComparison.Ordinal);
        Assert.Contains("python3 tools/amend.py --listing ExampleMod --version 1.2.0 --owner --", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAsync_ANewKindOfADerivedDependencyOnTheAuthorsRequest_KeepsTheEntry_AndALinkThatIsNoHttpsLinkIsRefused()
    {
        var vector = Vector("the owner raises a game_max");
        _releases.Main[Folder + "1.2.0.json"] = (string)vector["base"]!;
        var amendments = await SignedInAsync();
        var change = new ReleaseChange { DependencyKinds = [new ReleaseDependencyKind("KittenExtensions", "required")] };
        var request = new ReleaseAmendmentRequest("ExampleMod", ReleaseSelection.Of("1.2.0"), change, "The author needs it.", "https://forums.ahwoo.com/threads/example-mod.123/post-9");

        var link = await Assert.ThrowsAsync<ReleaseAmendmentRefusedException>(() => amendments.PreviewAsync(request with { AuthorRequest = "forums.ahwoo.com/threads/example-mod.123" }));
        var alone = await Assert.ThrowsAsync<ReleaseAmendmentRefusedException>(() => amendments.PreviewAsync(request with { AuthorRequest = null }));
        Assert.All(_sent, sent => Assert.Equal("GET", sent.Method));
        var preview = await amendments.PreviewAsync(request);
        await amendments.OpenAsync(preview);

        Assert.Equal((ReleaseAmendmentRefusal.InvalidChange, ReleaseAmendmentRefusal.Widens), (link.Refusal, alone.Refusal));
        var written = JsonNode.Parse(_releases.Branches["steward/amend-examplemod"].Files[Folder + "1.2.0.json"])!;
        Assert.Equal(
            """[{"id":"KittenExtensions","kind":"required","source":"authored"},{"id":"ExampleLibrary","kind":"required","min":"2.0.0","max":"2.9.0","source":"authored"}]""",
            written["dependencies"]!.ToJsonString());
        Assert.Contains("so this amendment has no command: `KittenExtensions` becomes `required`.", _releases.Pulls.Single().Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAsync_ARemovedGameMaxOnTheAuthorsRequest_CommitsTheFileWithoutIt_AndNamesTheChangeThatTheToolHasNoOptionFor()
    {
        const string Link = "https://github.com/KSAModding/content-index/issues/42#issuecomment-7";
        var published = (string)Vector("the owner raises a game_max")["base"]!;
        _releases.Main[Folder + "1.2.0.json"] = published;
        var amendments = await SignedInAsync();
        var alone = new ReleaseAmendmentRequest("ExampleMod", ReleaseSelection.Of("1.2.0"), new ReleaseChange { RemoveGameMax = true }, "The author tested it.");

        var refused = await Assert.ThrowsAsync<ReleaseAmendmentRefusedException>(() => amendments.PreviewAsync(alone));
        var preview = await amendments.PreviewAsync(alone with { AuthorRequest = Link });
        await amendments.OpenAsync(preview);

        Assert.Equal(ReleaseAmendmentRefusal.Widens, refused.Refusal);
        Assert.Equal(
            published.Replace("  \"game_max\": \"2026.8.19.5261\",\n  \"game_max_revision\": 5261,\n", string.Empty, StringComparison.Ordinal),
            _releases.Branches["steward/amend-examplemod"].Files[Folder + "1.2.0.json"]);
        var body = _releases.Pulls.Single().Body;
        Assert.Contains($"Requested by the author: <{Link}>\n\ntools/amend.py has no option for these changes, so this amendment has no command: `game_max` is removed.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("```", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAsync_AReleaseFileThatChangedOnMainSinceThePreview_WritesNothingAndGivesTheNewPreview()
    {
        _releases.Main[Folder + "1.2.0.json"] = (string)Vector("a game_max is added after game_min_revision")["base"]!;
        var amendments = await SignedInAsync();
        var request = new ReleaseAmendmentRequest("ExampleMod", ReleaseSelection.Of("1.2.0"), new ReleaseChange { Yank = true }, "The archive carries malware.");
        var preview = await amendments.PreviewAsync(request);
        var merged = (string)Vector("a game_max is added after game_min_revision")["written"]!;
        _releases.Main[Folder + "1.2.0.json"] = merged;
        lock (_sent)
            _sent.Clear();

        var changed = await Assert.ThrowsAsync<ReleaseAmendmentChangedException>(() => amendments.OpenAsync(preview));

        Assert.All(_sent, sent => Assert.Equal("GET", sent.Method));
        Assert.Empty(_releases.Branches);
        var current = Assert.Single(changed.Current.Files);
        Assert.Equal(merged, current.Before);
        Assert.Contains("\"game_max\": \"2026.8.19.5261\"", current.After, StringComparison.Ordinal);
        Assert.Contains("\"yanked\": true", current.After, StringComparison.Ordinal);

        await amendments.OpenAsync(changed.Current);

        Assert.Equal(_releases.MainSha, _releases.Branches["steward/amend-examplemod"].Parent);
        Assert.Equal(current.After, _releases.Branches["steward/amend-examplemod"].Files[Folder + "1.2.0.json"]);
    }

    [Fact]
    public async Task OpenAsync_AReleaseFileThatChangesOnMainBetweenTheReadAndTheBranch_StaysOnMainAndTheBranchStartsAtTheRead()
    {
        var vector = Vector("a steward yanks a release on a report");
        _releases.Main[Folder + "1.2.0.json"] = (string)vector["base"]!;
        var amendments = await SignedInAsync();
        var preview = await amendments.PreviewAsync(new ReleaseAmendmentRequest("ExampleMod", ReleaseSelection.Of("1.2.0"), new ReleaseChange { Yank = true }, "The archive carries malware."));
        var read = _releases.MainSha;
        var merged = (string)Vector("a game_max is added after game_min_revision")["written"]!;
        _releases.BeforeFirstBlob = () => _releases.Main[Folder + "1.2.0.json"] = merged;

        await amendments.OpenAsync(preview);

        var branch = _releases.Branches["steward/amend-examplemod"];
        Assert.Equal(read, branch.Parent);
        Assert.NotEqual(read, _releases.MainSha);
        Assert.Equal((string)vector["written"]!, branch.Files[Folder + "1.2.0.json"]);
        Assert.Equal(merged, _releases.Main[Folder + "1.2.0.json"]);
    }

    [Fact]
    public async Task ReleasesAsync_ListsTheStampedVersionsNewestFirst_AndALeftOutListingIsRefused()
    {
        foreach (var version in (string[])["1.0.0", "1.10.0", "1.2.0", "1.2.0-beta.1"])
            _releases.Main[Folder + version + ".json"] = "{}\n";
        _releases.Main[Folder + "notes.txt"] = "not a release\n";
        _releases.Main["releases/Other/1.0.0.json"] = "{}\n";
        var amendments = await SignedInAsync();

        var versions = await amendments.ReleasesAsync("ExampleMod");
        var missing = await Assert.ThrowsAsync<ReleaseAmendmentRefusedException>(() => amendments.ReleasesAsync("Missing"));

        Assert.Equal(["1.10.0", "1.2.0", "1.2.0-beta.1", "1.0.0"], versions);
        Assert.Equal(ReleaseAmendmentRefusal.UnknownRelease, missing.Refusal);
        Assert.All(_sent, sent => Assert.Equal("GET", sent.Method));
    }

    [Fact]
    public async Task NoStewardOfContentIndexReleases_OrAnIdThatIsNoContentId_IsRefusedBeforeAnyRequest()
    {
        var amendments = await SignedInAsync();
        var request = new ReleaseAmendmentRequest("ExampleMod", ReleaseSelection.All, new ReleaseChange { Yank = true }, "Malware.");

        var path = await Assert.ThrowsAsync<ReleaseAmendmentRefusedException>(() => amendments.PreviewAsync(request with { ListingId = "../ExampleMod" }));
        _role.Access = new StewardAccess("octocat", ContentIndex: true, ContentIndexReleases: false);
        var failure = await Assert.ThrowsAsync<StewardException>(() => amendments.PreviewAsync(request));

        Assert.Equal(ReleaseAmendmentRefusal.UnknownRelease, path.Refusal);
        Assert.Equal(StewardFailure.NotSteward, failure.Failure);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task OpenAsync_OwnListing_MentionsNobody()
    {
        Listing("octocat/ExampleMod");
        _releases.Main[Folder + "1.2.0.json"] = (string)Vector("a steward yanks a release on a report")["base"]!;
        var amendments = await SignedInAsync();

        var preview = await amendments.PreviewAsync(new ReleaseAmendmentRequest("ExampleMod", ReleaseSelection.All, new ReleaseChange { Yank = true }, "Malware."));
        await amendments.OpenAsync(preview);

        Assert.Empty(preview.Owners);
        Assert.DoesNotContain("@", _releases.Pulls.Single().Body, StringComparison.Ordinal);
    }

    private static JsonObject Vector(string name) =>
        Vectors["vectors"]!.AsArray().Single(vector => (string)vector!["name"]! == name)!.AsObject();

    /// <summary>The options of a vector that bound a game version, the loader or a dependency, as a change.</summary>
    private static ReleaseChange ChangeOf(JsonObject options)
    {
        Assert.All(options, option => Assert.Contains(option.Key, (string[])["game-min", "game-max", "loader-min", "loader-max", "dependency-min", "dependency-max"]));
        IEnumerable<ReleaseDependencyBounds> Bounds(string option, bool min) => options[option]?.AsArray().Select(bound => ((string)bound!).Split('='))
            .Select(pair => new ReleaseDependencyBounds(pair[0], min ? pair[1] : null, min ? null : pair[1])) ?? [];

        return new ReleaseChange
        {
            GameMin = (string?)options["game-min"],
            GameMax = (string?)options["game-max"],
            LoaderMin = (string?)options["loader-min"],
            LoaderMax = (string?)options["loader-max"],
            DependencyBounds = [.. Bounds("dependency-min", min: true), .. Bounds("dependency-max", min: false)],
        };
    }

    /// <summary>The same release file for another version.</summary>
    private static string Version(string text, string version) =>
        text.Replace("\"version\": \"1.2.0\"", $"\"version\": \"{version}\"", StringComparison.Ordinal);

    private void Listing(string repository) =>
        _hosts[Api + "/repos/KSAModding/content-index/contents/listings/ExampleMod.toml?ref=main"] = () =>
            Json(JsonSerializer.Serialize(new { sha = "abc", encoding = "base64", content = Convert.ToBase64String(Encoding.UTF8.GetBytes($"id = \"ExampleMod\"\nname = \"Example Mod\"\n[releases]\ngithub = \"{repository}\"\n")) }));

    private async Task<GitHubReleaseAmendments> SignedInAsync()
    {
        _hosts.TryAdd(Api + "/user", () => Json("""{"login":"octocat","id":1}"""));
        var session = new GitHubSession(Http(), "Iv1.testclient", "borea-test", new FixedTime(Now));
        Assert.True((await session.SignInAsync()).SignedIn);
        lock (_sent)
            _sent.Clear();
        return new GitHubReleaseAmendments(session, _role, Http(), new TableFormat(), new FixedTime(Now));
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
            ? _releases.Respond(request.Method.Method, url[(Upstream.Length + 1)..], body)
            : Json("""{"message":"Not Found"}""", HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed record Sent(string Method, string Url, string? Authorization)
    {
        public string Line => Method + " " + Url;
    }

    /// <param name="Parent">The commit of main that the commit of the branch sits on.</param>
    private sealed record FakeBranch(string Parent, string Message, Dictionary<string, string> Files);

    private sealed record FakePull(string Title, string Head, string Base, string Body);

    /// <summary>
    /// The main branch, the git objects, the steward branches and the pull requests of content-index-releases, as the REST API answers them.
    /// Every commit of main that was read stays readable, as on GitHub.
    /// </summary>
    private sealed partial class FakeReleases
    {
        private readonly Dictionary<string, Dictionary<string, string>> _commits = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _blobs = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (string BaseTree, JsonArray Entries)> _trees = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (string Tree, string Parent, string Message)> _created = new(StringComparer.Ordinal);

        public Dictionary<string, string> Main { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, FakeBranch> Branches { get; } = new(StringComparer.Ordinal);

        public List<FakePull> Pulls { get; } = [];

        /// <summary>Runs once before the first blob is created, as another merge into main would.</summary>
        public Action? BeforeFirstBlob { get; set; }

        public string MainSha => Sha(string.Join('\0', Main.OrderBy(file => file.Key, StringComparer.Ordinal).Select(file => file.Key + "=" + file.Value)));

        public HttpResponseMessage Respond(string method, string path, string? body)
        {
            if (method == "GET" && path == "git/ref/heads/main")
            {
                var head = MainSha;
                _commits[head] = new Dictionary<string, string>(Main, StringComparer.Ordinal);
                return Json(JsonSerializer.Serialize(new { @ref = "refs/heads/main", @object = new { sha = head } }));
            }

            if (method == "GET" && path.StartsWith("git/ref/heads/", StringComparison.Ordinal))
                return Branches.ContainsKey(path["git/ref/heads/".Length..]) ? Json("{}") : NotFound();
            if (method == "GET" && ContentPath().Match(path) is { Success: true } content && _commits.TryGetValue(content.Groups["ref"].Value, out var files))
                return Content(files, Uri.UnescapeDataString(content.Groups["path"].Value));
            if (method == "GET" && path.StartsWith("git/commits/", StringComparison.Ordinal) && _commits.ContainsKey(path["git/commits/".Length..]))
                return Json(JsonSerializer.Serialize(new { tree = new { sha = "tree-" + path["git/commits/".Length..] } }));

            if (method == "POST" && path == "git/blobs")
            {
                if (BeforeFirstBlob is { } merge)
                {
                    BeforeFirstBlob = null;
                    merge();
                }

                var blob = JsonNode.Parse(body!)!;
                Assert.Equal("base64", (string?)blob["encoding"]);
                var text = Encoding.UTF8.GetString(Convert.FromBase64String((string)blob["content"]!));
                var sha = Sha("blob " + text);
                _blobs[sha] = text;
                return Json(JsonSerializer.Serialize(new { sha }), HttpStatusCode.Created);
            }

            if (method == "POST" && path == "git/trees")
            {
                var tree = JsonNode.Parse(body!)!;
                var sha = Sha("tree " + body);
                _trees[sha] = ((string)tree["base_tree"]!, tree["tree"]!.AsArray());
                return Json(JsonSerializer.Serialize(new { sha }), HttpStatusCode.Created);
            }

            if (method == "POST" && path == "git/commits")
            {
                var commit = JsonNode.Parse(body!)!;
                var sha = Sha("commit " + body);
                _created[sha] = ((string)commit["tree"]!, (string)commit["parents"]!.AsArray().Single()!, (string)commit["message"]!);
                return Json(JsonSerializer.Serialize(new { sha }), HttpStatusCode.Created);
            }

            if (method == "POST" && path == "git/refs")
            {
                var created = JsonNode.Parse(body!)!;
                var (tree, parent, message) = _created[(string)created["sha"]!];
                var (baseTree, entries) = _trees[tree];
                Assert.Equal("tree-" + parent, baseTree);
                var branchFiles = new Dictionary<string, string>(_commits[parent], StringComparer.Ordinal);
                foreach (var entry in entries)
                {
                    Assert.Equal(("100644", "blob"), ((string?)entry!["mode"], (string?)entry["type"]));
                    branchFiles[(string)entry["path"]!] = _blobs[(string)entry["sha"]!];
                }

                Branches.Add(((string)created["ref"]!)["refs/heads/".Length..], new FakeBranch(parent, message, branchFiles));
                return Json("{}", HttpStatusCode.Created);
            }

            if (method == "POST" && path == "pulls")
            {
                var request = JsonNode.Parse(body!)!;
                Pulls.Add(new FakePull((string)request["title"]!, (string)request["head"]!, (string)request["base"]!, (string)request["body"]!));
                return Json(JsonSerializer.Serialize(new { number = Pulls.Count, html_url = $"https://github.com/KSAModding/content-index-releases/pull/{Pulls.Count}" }), HttpStatusCode.Created);
            }

            return NotFound();
        }

        /// <summary>A file, or a folder as the list of the files right in it.</summary>
        private static HttpResponseMessage Content(Dictionary<string, string> files, string path)
        {
            if (files.TryGetValue(path, out var text))
                return Json(JsonSerializer.Serialize(new { sha = Sha("blob " + text), encoding = "base64", content = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) }));

            var inside = files.Keys.Where(file => file.StartsWith(path + "/", StringComparison.Ordinal) && !file[(path.Length + 1)..].Contains('/')).ToList();
            return inside.Count == 0
                ? NotFound()
                : Json(JsonSerializer.Serialize(inside.Select(file => new { name = file[(path.Length + 1)..], path = file, type = "file", sha = Sha("blob " + files[file]) })));
        }

        private static HttpResponseMessage NotFound() => Json("""{"message":"Not Found"}""", HttpStatusCode.NotFound);

        private static string Sha(string text) => Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(text)));

        [GeneratedRegex(@"^contents/(?<path>[^?]+)\?ref=(?<ref>[0-9a-f]+)$")]
        private static partial Regex ContentPath();
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
