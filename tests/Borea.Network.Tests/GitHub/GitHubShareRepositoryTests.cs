using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Borea.Core.Sharing;
using Borea.Network.GitHub;

namespace Borea.Network.Tests.GitHub;

/// <summary>octocat (id 1) is signed in and shares into octocat/ksa-shares.</summary>
public sealed class GitHubShareRepositoryTests
{
    private const string Api = "https://api.github.com";
    private const string Repository = Api + "/repos/octocat/ksa-shares";
    private const string RepositoryJson = """{"id":77,"full_name":"octocat/ksa-shares"}""";
    private const string ReleaseJson = """{"id":9,"tag_name":"ExampleRover/v1.0.0","html_url":"https://github.com/octocat/ksa-shares/releases/tag/ExampleRover/v1.0.0","upload_url":"https://uploads.github.com/repos/octocat/ksa-shares/releases/9/assets{?name,label}"}""";

    private readonly ConcurrentQueue<Sent> _sent = new();
    private readonly ConcurrentDictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.Ordinal);

    public GitHubShareRepositoryTests()
    {
        On("POST", "https://github.com/login/device/code", () => Json("""{"device_code":"d","user_code":"WDJB-MJHT","verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}"""));
        On("POST", "https://github.com/login/oauth/access_token", () => Json("""{"access_token":"ghu_secret","token_type":"bearer"}"""));
        On("GET", Api + "/user", () => Json("""{"login":"octocat","id":1}"""));
    }

    [Fact]
    public async Task FirstShare_CreatesTheRepositoryTheReleaseAndTheAsset()
    {
        On("POST", Api + "/user/repos", () => Json(RepositoryJson, HttpStatusCode.Created));
        On("GET", Repository + "/releases?per_page=100&page=1", () => Json("[]"));
        On("POST", Repository + "/releases", () => Json(ReleaseJson, HttpStatusCode.Created));
        On("POST", "https://uploads.github.com/repos/octocat/ksa-shares/releases/9/assets?name=ExampleRover-1.0.0.zip", () => Json("{}", HttpStatusCode.Created));
        var shares = await SignedInAsync();

        var repository = await shares.EnsureAsync();
        var version = await shares.NextVersionAsync(repository, "ExampleRover/v");
        var page = await shares.PublishAsync(repository, "ExampleRover/v", version, "ExampleRover-1.0.0.zip", new MemoryStream([1, 2, 3]));

        Assert.Equal(new ShareRepository("octocat/ksa-shares", 77), repository);
        Assert.Equal("1.0.0", version);
        Assert.Equal("https://github.com/octocat/ksa-shares/releases/tag/ExampleRover/v1.0.0", page.AbsoluteUri);
        var sent = _sent.ToArray();
        Assert.Equal(["GET " + Repository, "POST " + Api + "/user/repos", "GET " + Repository + "/releases?per_page=100&page=1", "POST " + Repository + "/releases", "POST https://uploads.github.com/repos/octocat/ksa-shares/releases/9/assets?name=ExampleRover-1.0.0.zip"], sent.Select(request => request.Method + " " + request.Url));
        Assert.Contains("\"auto_init\":true", sent[1].Body, StringComparison.Ordinal);
        Assert.Equal("""{"tag_name":"ExampleRover/v1.0.0","name":"ExampleRover/v1.0.0"}""", sent[3].Body);
        Assert.All(sent, request => Assert.Equal("Bearer ghu_secret", request.Authorization));
    }

    [Fact]
    public async Task SecondShareOfAnotherVehicle_UsesTheSameRepositoryWithItsOwnPrefix()
    {
        On("GET", Repository, () => Json(RepositoryJson));
        On("GET", Repository + "/releases?per_page=100&page=1", () => Json("""[{"id":9,"tag_name":"ExampleRover/v1.0.0"}]"""));
        var shares = await SignedInAsync();

        var repository = await shares.EnsureAsync();

        Assert.Equal("1.0.0", await shares.NextVersionAsync(repository, "MoonBase/v"));
        Assert.DoesNotContain(_sent, request => request.Method == "POST");
    }

    [Fact]
    public async Task LaterVersion_GetsTheNextMinorOfItsPrefix()
    {
        On("GET", Repository + "/releases?per_page=100&page=1", () => Json("""[{"tag_name":"ExampleRover/v1.0.0"},{"tag_name":"ExampleRover/v1.2.0"},{"tag_name":"ExampleRover-Mk2/v4.0.0"},{"tag_name":"ExampleRover/vnext"}]"""));
        var shares = await SignedInAsync();

        Assert.Equal("1.3.0", await shares.NextVersionAsync(new ShareRepository("octocat/ksa-shares", 77), "ExampleRover/v"));
    }

    [Fact]
    public async Task CreateRefused_MayNotCreateAndAsksNothingMore()
    {
        On("POST", Api + "/user/repos", () => Json("""{"message":"Resource not accessible by integration"}""", HttpStatusCode.Forbidden));
        var shares = await SignedInAsync();

        var exception = await Assert.ThrowsAsync<ShareRepositoryException>(() => shares.EnsureAsync());

        Assert.Equal(ShareRepositoryFailure.MayNotCreateRepository, exception.Failure);
        Assert.Equal(["GET " + Repository, "POST " + Api + "/user/repos"], _sent.Select(request => request.Method + " " + request.Url));
    }

    [Fact]
    public async Task AppNotOnTheRepository_NamesItForTheInstallPage()
    {
        On("POST", Repository + "/releases", () => Json("""{"message":"Resource not accessible by integration"}""", HttpStatusCode.Forbidden));
        var shares = await SignedInAsync();

        var exception = await Assert.ThrowsAsync<ShareRepositoryException>(() => shares.PublishAsync(new ShareRepository("octocat/ksa-shares", 77), "ExampleRover/v", "1.0.0", "a.zip", new MemoryStream([1])));

        Assert.Equal((ShareRepositoryFailure.AppNotOnRepository, 77L), (exception.Failure, exception.RepositoryId));
    }

    [Fact]
    public async Task AssetUploadFails_DeletesTheReleaseAgain()
    {
        On("POST", Repository + "/releases", () => Json(ReleaseJson, HttpStatusCode.Created));
        On("DELETE", Repository + "/releases/9", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var shares = await SignedInAsync();

        await Assert.ThrowsAsync<ShareRepositoryException>(() => shares.PublishAsync(new ShareRepository("octocat/ksa-shares", 77), "ExampleRover/v", "1.0.0", "a.zip", new MemoryStream([1])));

        Assert.Equal("DELETE " + Repository + "/releases/9", _sent.Last().Method + " " + _sent.Last().Url);
    }

    private async Task<GitHubShareRepository> SignedInAsync()
    {
        var session = new GitHubSession(Http(), "Iv1.testclient", "borea-test", new InstantTimeProvider());
        Assert.True((await session.SignInAsync()).SignedIn);
        _sent.Clear();
        return new GitHubShareRepository(session, Http());
    }

    private HttpClient Http() => new(new FakeHttpMessageHandler(RespondAsync));

    private void On(string method, string url, Func<HttpResponseMessage> answer) => _routes[method + " " + url] = answer;

    private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request)
    {
        var url = request.RequestUri!.AbsoluteUri;
        var body = request.Content is StringContent text ? await text.ReadAsStringAsync() : null;
        _sent.Enqueue(new Sent(request.Method.Method, url, request.Headers.Authorization?.ToString(), body));
        return _routes.TryGetValue(request.Method.Method + " " + url, out var answer)
            ? answer()
            : Json("""{"message":"Not Found"}""", HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed record Sent(string Method, string Url, string? Authorization, string? Body);

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
