using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Borea.Core.GitHub;
using Borea.Core.Listings;
using Borea.Core.Secrets;
using Borea.Core.Sharing;
using Borea.Network.Sharing;

namespace Borea.Network.Tests;

public sealed class ShareHostClientTests
{
    private const string Api = "https://ksamods.gg/api/share/v1/";
    private const string DeviceJson = """{"device_code":"dc1","user_code":"ROVR-1234","verification_uri":"https://ksamods.gg/device","expires_in":900,"interval":5}""";
    private const string TokenJson = """{"access_token":"at1","token_type":"Bearer","refresh_token":"rt1"}""";
    private const string UploadJson = """{"upload":"u42","checks":[{"name":"meta.toml","passed":true},{"name":"folder","passed":false,"message":"Taken by OtherRover."}],"version":"1.0.0","listing":"id = \"ExampleRover\"\n"}""";
    private const string PublishedJson = """{"id":"ExampleRover","version":"1.0.0","pull_request":"https://github.com/KSAModding/content-index/pull/250"}""";

    private static readonly ShareHost Host = new("ksamods", "ksamods.gg", new Uri(Api), ["vehicle", "save"], new Dictionary<string, long> { ["vehicle"] = 1024 });

    private readonly StepTimeProvider _time = new();
    private readonly ConcurrentQueue<Sent> _sent = new();
    private readonly Queue<HttpResponseMessage> _tokenAnswers = new();
    private readonly MemorySecretStore _secrets = new();
    private readonly WrittenFormat _format = new();
    private string _deviceJson = DeviceJson;
    private HttpStatusCode _deviceStatus = HttpStatusCode.OK;
    private Func<HttpRequestMessage, HttpResponseMessage>? _apiAnswer;

    [Fact]
    public async Task SignInAsync_PendingSlowDownThenConfirmed_ReportsTheCodeAndKeepsTheRefreshToken()
    {
        _tokenAnswers.Enqueue(Json("""{"error":"authorization_pending"}""", HttpStatusCode.BadRequest));
        _tokenAnswers.Enqueue(Json("""{"error":"slow_down"}""", HttpStatusCode.BadRequest));
        _tokenAnswers.Enqueue(Json(TokenJson));
        var client = Client();
        var codes = new List<GitHubDeviceCode>();

        await client.SignInAsync(Host, new ListProgress<GitHubDeviceCode>(codes), keepSignedIn: true);

        Assert.True(client.IsSignedIn(Host));
        Assert.Equal([new GitHubDeviceCode("ROVR-1234", "https://ksamods.gg/device")], codes);
        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)], _time.Delays);
        var sent = _sent.ToArray();
        Assert.Equal(Api + "oauth/device_authorization", sent[0].Url);
        Assert.Equal("client_id=borea", sent[0].Body);
        Assert.Equal("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code&device_code=dc1&client_id=borea", sent[1].Body);
        Assert.All(sent, request => Assert.Null(request.Authorization));
        Assert.Equal("refresh:rt1", _secrets.Read(ShareHostClient.SecretName(Host)));
    }

    [Fact]
    public async Task SignInAsync_NotKept_DeletesNothingAndWritesNothing()
    {
        _tokenAnswers.Enqueue(Json(TokenJson));

        await Client().SignInAsync(Host);

        Assert.Empty(_secrets.Calls);
    }

    [Fact]
    public async Task SignInAsync_KeptRefreshToken_SignsInWithoutACode()
    {
        _secrets.Write(ShareHostClient.SecretName(Host), "refresh:rt0");
        _tokenAnswers.Enqueue(Json(TokenJson));
        var client = Client();

        await client.SignInAsync(Host, keepSignedIn: true);

        Assert.True(client.IsSignedIn(Host));
        Assert.Equal("grant_type=refresh_token&refresh_token=rt0&client_id=borea", Assert.Single(_sent).Body);
        Assert.Equal("refresh:rt1", _secrets.Read(ShareHostClient.SecretName(Host)));
    }

    [Fact]
    public async Task SignInAsync_ApiOfTheKeyChanged_SendsNoKeptToken()
    {
        var moved = Host with { Api = new Uri("https://successor.example/api/share/v1/") };
        _secrets.Write(ShareHostClient.SecretName(Host), "refresh:rt0");
        var client = await SignedInAsync();

        await Assert.ThrowsAsync<ShareHostException>(() => client.SignInAsync(moved, keepSignedIn: true));

        Assert.False(client.IsSignedIn(moved));
        var toMoved = _sent.Where(request => request.Url.StartsWith("https://successor.example/", StringComparison.Ordinal)).ToList();
        Assert.Equal(["client_id=borea"], toMoved.Select(request => request.Body));
        Assert.All(toMoved, request => Assert.Null(request.Authorization));
    }

    [Fact]
    public async Task UploadAsync_ApiOfTheKeyChanged_SendsNoToken()
    {
        var moved = Host with { Api = new Uri("https://successor.example/api/share/v1/") };
        var client = await SignedInAsync();
        var sent = _sent.Count;

        var exception = await Assert.ThrowsAsync<ShareHostException>(() => client.UploadAsync(moved, new MemoryStream([1]), "a.zip", [], null));

        Assert.Equal(ShareHostFailure.SignedOut, exception.Failure);
        Assert.Equal(sent, _sent.Count);
    }

    [Fact]
    public async Task SignInAsync_KeptTokenRefused_IsDeletedAndTheCodeIsAsked()
    {
        _secrets.Write(ShareHostClient.SecretName(Host), "refresh:rt0");
        _tokenAnswers.Enqueue(Json("""{"error":"invalid_grant"}""", HttpStatusCode.BadRequest));
        _tokenAnswers.Enqueue(Json("""{"access_token":"at2"}"""));
        var client = Client();

        await client.SignInAsync(Host, keepSignedIn: true);

        Assert.Equal(Api + "oauth/device_authorization", _sent.ToArray()[1].Url);
        Assert.Equal("access:at2", _secrets.Read(ShareHostClient.SecretName(Host)));
    }

    [Theory]
    [InlineData("access_denied")]
    [InlineData("expired_token")]
    public async Task SignInAsync_PlayerRefusedOrCodeExpired_IsRefused(string error)
    {
        _tokenAnswers.Enqueue(Json($$"""{"error":"{{error}}"}""", HttpStatusCode.BadRequest));
        var client = Client();

        var exception = await Assert.ThrowsAsync<ShareHostException>(() => client.SignInAsync(Host));

        Assert.Equal((ShareHostFailure.SignInRefused, error), (exception.Failure, exception.Error));
        Assert.False(client.IsSignedIn(Host));
    }

    [Fact]
    public async Task SignInAsync_StartRefusedWithAnOAuthError_ShowsTheDescriptionOfTheHost()
    {
        _deviceJson = """{"error":"invalid_request","error_description":"Too many sign-in requests. Try again in a minute."}""";
        _deviceStatus = HttpStatusCode.TooManyRequests;

        var exception = await Assert.ThrowsAsync<ShareHostException>(() => Client().SignInAsync(Host));

        Assert.Equal((ShareHostFailure.Host, "invalid_request", "Too many sign-in requests. Try again in a minute."), (exception.Failure, exception.Error, exception.Message));
        Assert.Single(_sent);
    }

    [Theory]
    [InlineData("http://ksamods.gg/device")]
    [InlineData("https://evil.example/device")]
    public async Task SignInAsync_CodePageOutsideTheHost_IsNotShown(string page)
    {
        _deviceJson = DeviceJson.Replace("https://ksamods.gg/device", page, StringComparison.Ordinal);

        var exception = await Assert.ThrowsAsync<ShareHostException>(() => Client().SignInAsync(Host));

        Assert.Equal(ShareHostFailure.UnexpectedResponse, exception.Failure);
        Assert.Single(_sent);
    }

    [Fact]
    public async Task UploadAsync_SendsTheArchiveTheDependenciesAndTheListing_AndReadsTheChecks()
    {
        _apiAnswer = _ => Json(UploadJson);
        var client = await SignedInAsync();
        var preserved = new AuthoredTable();
        preserved.Set("kind", "required");
        preserved.Set("any_of", new List<object> { "A", "B" });

        var upload = await client.UploadAsync(
            Host,
            new MemoryStream([1, 2, 3]),
            "Example Rover.zip",
            [new ListingDependency("ExamplePartsMod", "required", "1.4.0"), new ListingDependency(string.Empty, string.Empty) { Preserved = preserved }],
            "ExampleRover");

        Assert.Equal(new ShareCheck("folder", false, "Taken by OtherRover."), upload.Checks[1]);
        Assert.Equal(("u42", "1.0.0", "id = \"ExampleRover\"\n"), (upload.Id, upload.ProposedVersion, upload.ListingText));
        var request = _sent.Last();
        Assert.Equal(Api + "uploads", request.Url);
        Assert.Equal("Bearer at1", request.Authorization);
        Assert.Equal(["archive", "dependencies", "listing"], request.Parts.Select(part => part.Name));
        Assert.Equal("Example Rover.zip", request.Parts[0].FileName);
        Assert.Equal((WrittenFormat.Text, "text/plain"), (request.Parts[1].Text, request.Parts[1].MediaType));
        Assert.Equal("ExampleRover", request.Parts[2].Text);
        var document = Assert.Single(_format.Written);
        var tables = Assert.IsType<List<object>>(Assert.Single(document.Entries, entry => entry.Key == "dependencies").Value).Cast<AuthoredTable>().ToList();
        Assert.Equal([KeyValuePair.Create<string, object>("id", "ExamplePartsMod"), new("kind", "required"), new("min", "1.4.0")], tables[0].Entries);
        Assert.Equal(["kind", "any_of"], tables[1].Entries.Select(entry => entry.Key));
    }

    [Fact]
    public async Task UploadAsync_NoDependencies_SendsAnEmptyList()
    {
        _apiAnswer = _ => Json(UploadJson);
        var client = await SignedInAsync();

        await client.UploadAsync(Host, new MemoryStream([1]), "a.zip", [], null);

        Assert.Equal(["archive", "dependencies"], _sent.Last().Parts.Select(part => part.Name));
        Assert.Empty(Assert.Single(_format.Written).Entries);
    }

    [Fact]
    public async Task UploadAsync_LiveAnswer_ReadsEveryCheck()
    {
        _apiAnswer = _ => Json(ShareHostReplies.Upload, HttpStatusCode.Created);
        var client = await SignedInAsync();

        var upload = await client.UploadAsync(Host, new MemoryStream([1]), "a.zip", [], null);

        Assert.Equal(("01M69W4FDJX6ZHS6XEER5V8468", "1.0.0", "type = \"vehicle\"\n"), (upload.Id, upload.ProposedVersion, upload.ListingText));
        Assert.Equal(["archive", "root", "type", "size", "meta.toml", "name", "version", "game file", "folder"], upload.Checks.Select(check => check.Name));
        Assert.All(upload.Checks, check => Assert.True(check.Passed));
    }

    [Theory]
    [MemberData(nameof(ShareHostReplies.Errors), MemberType = typeof(ShareHostReplies))]
    [MemberData(nameof(ShareHostReplies.MalformedExtras), MemberType = typeof(ShareHostReplies))]
    public async Task UploadAsync_ErrorOfTheHost_GivesItsMessageAndExtras(ShareHostReplies.ErrorReply reply)
    {
        _apiAnswer = _ => Json(reply.Body, reply.Status);
        var client = await SignedInAsync();

        var exception = await Assert.ThrowsAsync<ShareHostException>(() => client.UploadAsync(Host, new MemoryStream([1]), "a.zip", [], null));

        Assert.Equal((ShareHostFailure.Host, reply.Code, reply.Message), (exception.Failure, exception.Error, exception.Message));
        Assert.Equal(reply.Checks, exception.Checks);
        Assert.Equal(reply.RetryAfter, exception.RetryAfter);
        Assert.Equal(reply.FieldErrors, exception.FieldErrors);
    }

    [Fact]
    public async Task PublishAsync_NewListing_SendsVersionStatusAndTheListingFields_AndReadsThePullRequest()
    {
        _apiAnswer = _ => Json(PublishedJson);
        var client = await SignedInAsync();
        var listing = new ListingDraft
        {
            Id = "ExampleRover",
            Type = "vehicle",
            Name = "Example Rover",
            Authors = ["Maxi", "Renan"],
            Abstract = "A six-wheel rover for the Moon.",
            Description = "Six wheels.",
            License = "CC-BY-4.0",
            Tags = ["rover"],
            Dependencies = [new ListingDependency("ExamplePartsMod", "required")],
        };

        var published = await client.PublishAsync(Host, "u/42", "1.0.0", "testing", listing);

        Assert.Equal(new SharePublished("ExampleRover", "1.0.0", new Uri("https://github.com/KSAModding/content-index/pull/250")), published);
        var request = _sent.Last();
        Assert.Equal(Api + "uploads/u%2F42/publish", request.Url);
        Assert.Equal(
            JsonNode.Parse("""{"version":"1.0.0","status":"testing","id":"ExampleRover","name":"Example Rover","authors":["Maxi","Renan"],"abstract":"A six-wheel rover for the Moon.","license":"CC-BY-4.0","description":"Six wheels.","tags":["rover"]}""")!.ToJsonString(),
            JsonNode.Parse(request.Body!)!.ToJsonString());
    }

    [Fact]
    public async Task PublishAsync_NewListingWithoutDescriptionOrTags_LeavesThemOut()
    {
        _apiAnswer = _ => Json(PublishedJson);
        var client = await SignedInAsync();
        var listing = new ListingDraft { Id = "ExampleRover", Name = "Example Rover", Authors = ["Maxi"], Abstract = "A rover.", License = "CC0-1.0", Description = string.Empty };

        await client.PublishAsync(Host, "u42", "1.0.0", "stable", listing);

        Assert.Equal(
            JsonNode.Parse("""{"version":"1.0.0","status":"stable","id":"ExampleRover","name":"Example Rover","authors":["Maxi"],"abstract":"A rover.","license":"CC0-1.0"}""")!.ToJsonString(),
            JsonNode.Parse(_sent.Last().Body!)!.ToJsonString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, """{"error":"id_taken","message":"ExampleRover belongs to another account."}""", ShareHostFailure.Host, "ExampleRover belongs to another account.")]
    [InlineData(HttpStatusCode.BadGateway, """{"error":"maintenance","message":"Back in five minutes."}""", ShareHostFailure.Host, "Back in five minutes.")]
    [InlineData(HttpStatusCode.Conflict, "<html>Conflict</html>", ShareHostFailure.UnexpectedResponse, null)]
    [InlineData(HttpStatusCode.BadGateway, "<html>Bad gateway</html>", ShareHostFailure.Unavailable, null)]
    public async Task PublishAsync_HostError_GivesItsMessage(HttpStatusCode status, string body, ShareHostFailure failure, string? message)
    {
        _apiAnswer = _ => Json(body, status);
        var client = await SignedInAsync();

        var exception = await Assert.ThrowsAsync<ShareHostException>(() => client.PublishAsync(Host, "u42", "1.0.0", "stable", null));

        Assert.Equal(failure, exception.Failure);
        if (message is not null)
            Assert.Equal(message, exception.Message);
    }

    [Fact]
    public async Task PublishAsync_NewVersionWithoutPullRequest_GivesTheRelease()
    {
        _apiAnswer = _ => Json("""{"id":"ExampleRover","version":"1.1.0"}""");
        var client = await SignedInAsync();

        var published = await client.PublishAsync(Host, "u42", "1.1.0", "stable", null);

        Assert.Equal(new SharePublished("ExampleRover", "1.1.0", null), published);
        Assert.Equal("""{"version":"1.1.0","status":"stable"}""", JsonNode.Parse(_sent.Last().Body!)!.ToJsonString());
    }

    [Theory]
    [InlineData("""{"version":"1.1.0"}""")]
    [InlineData("""{"id":"ExampleRover"}""")]
    public async Task PublishAsync_WithoutIdOrVersion_IsUnexpected(string body)
    {
        _apiAnswer = _ => Json(body);
        var client = await SignedInAsync();

        var exception = await Assert.ThrowsAsync<ShareHostException>(() => client.PublishAsync(Host, "u42", "1.1.0", "stable", null));

        Assert.Equal(ShareHostFailure.UnexpectedResponse, exception.Failure);
    }

    [Fact]
    public async Task PublishAsync_PullRequestOutsideGitHub_IsUnexpected()
    {
        _apiAnswer = _ => Json(PublishedJson.Replace("https://github.com/", "https://evil.example/", StringComparison.Ordinal));
        var client = await SignedInAsync();

        var exception = await Assert.ThrowsAsync<ShareHostException>(() => client.PublishAsync(Host, "u42", "1.0.0", "stable", null));

        Assert.Equal(ShareHostFailure.UnexpectedResponse, exception.Failure);
    }

    [Fact]
    public async Task UploadAsync_TokenRefused_SignsOutAndDeletesTheKeptToken()
    {
        _apiAnswer = _ => Json(ShareHostReplies.Unauthorized, HttpStatusCode.Unauthorized);
        var client = await SignedInAsync(keep: true);

        var exception = await Assert.ThrowsAsync<ShareHostException>(() => client.UploadAsync(Host, new MemoryStream([1]), "a.zip", [], null));

        Assert.Equal(ShareHostFailure.SignedOut, exception.Failure);
        Assert.False(client.IsSignedIn(Host));
        Assert.Null(_secrets.Read(ShareHostClient.SecretName(Host)));
    }

    [Fact]
    public async Task UploadAsync_SignedOut_SendsNothing()
    {
        var exception = await Assert.ThrowsAsync<ShareHostException>(() => Client().UploadAsync(Host, new MemoryStream([1]), "a.zip", [], null));

        Assert.Equal(ShareHostFailure.SignedOut, exception.Failure);
        Assert.Empty(_sent);
    }

    private async Task<ShareHostClient> SignedInAsync(bool keep = false)
    {
        _tokenAnswers.Enqueue(Json(TokenJson));
        var client = Client();
        await client.SignInAsync(Host, keepSignedIn: keep);
        return client;
    }

    private ShareHostClient Client() => new(FakeHttpMessageHandler.BuildClient(Answer, out _), _format, _time, _secrets);

    private async Task<HttpResponseMessage> Answer(HttpRequestMessage request)
    {
        var parts = new List<Part>();
        string? body = null;
        if (request.Content is MultipartFormDataContent multipart)
        {
            foreach (var part in multipart)
                parts.Add(new Part(part.Headers.ContentDisposition?.Name?.Trim('"') ?? string.Empty, part.Headers.ContentDisposition?.FileName?.Trim('"'), part.Headers.ContentType?.MediaType, await part.ReadAsStringAsync()));
        }
        else if (request.Content is not null)
        {
            body = await request.Content.ReadAsStringAsync();
        }

        _sent.Enqueue(new Sent(request.RequestUri!.AbsoluteUri, body, request.Headers.Authorization?.ToString(), parts));
        return request.RequestUri.AbsoluteUri switch
        {
            Api + "oauth/device_authorization" => Json(_deviceJson, _deviceStatus),
            Api + "oauth/token" => _tokenAnswers.Dequeue(),
            _ => _apiAnswer?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed record Part(string Name, string? FileName, string? MediaType, string Text);

    private sealed record Sent(string Url, string? Body, string? Authorization, IReadOnlyList<Part> Parts);

    private sealed class MemorySecretStore : ISecretStore
    {
        private readonly ConcurrentDictionary<string, string> _secrets = new();

        public ConcurrentQueue<string> Calls { get; } = new();

        public string? Read(string name) => _secrets.GetValueOrDefault(name);

        public void Write(string name, string secret)
        {
            Calls.Enqueue("write");
            _secrets[name] = secret;
        }

        public void Delete(string name)
        {
            Calls.Enqueue("delete");
            _secrets.TryRemove(name, out _);
        }
    }

    /// <summary>Keeps every document it writes and gives a marker text, because Borea.Storage writes the real TOML.</summary>
    private sealed class WrittenFormat : IListingFormat
    {
        public const string Text = "[[dependencies]]\n";

        public ConcurrentQueue<AuthoredTable> Written { get; } = new();

        public string Write(AuthoredTable document, string? original = null)
        {
            Written.Enqueue(document);
            return Text;
        }

        public AuthoredTable Read(string text) => throw new NotSupportedException();
    }

    private sealed class ListProgress<T>(List<T> reports) : IProgress<T>
    {
        public void Report(T value) => reports.Add(value);
    }

    /// <summary>A clock whose timers fire at once and move the clock by their due time, so a polling loop runs without waiting.</summary>
    private sealed class StepTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<TimeSpan> _delays = [];
        private DateTimeOffset _now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

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
