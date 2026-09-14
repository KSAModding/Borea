using System.Net;
using System.Text;
using Borea.Core.Mods;
using Borea.Core.Updates;
using Borea.Network.GitHub;

namespace Borea.Network.Tests;

public sealed class BoreaReleaseCheckTests
{
    /// <summary>The body GitHub answers while no release exists, captured from the real endpoint.</summary>
    private const string NotFoundJson = """{"message":"Not Found","documentation_url":"https://docs.github.com/rest/releases/releases#get-the-latest-release","status":"404"}""";

    /// <summary>The fields the check reads, in the shape of a real answer.</summary>
    private static string ReleaseJson(string tag, bool draft = false, bool prerelease = false, string? htmlUrl = null) =>
        $$"""{"html_url":"{{htmlUrl ?? "https://github.com/KSAModding/Borea/releases/tag/" + tag}}","tag_name":"{{tag}}","name":"Borea {{tag}}","draft":{{(draft ? "true" : "false")}},"prerelease":{{(prerelease ? "true" : "false")}},"published_at":"2026-09-13T12:00:00Z"}""";

    private static string ReleasesJson(params string[] releases) => "[" + string.Join(",", releases) + "]";

    private static BoreaReleaseCheck CheckAnswering(string json, out FakeHttpMessageHandler handler) =>
        new(FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out handler));

    [Fact]
    public async Task GetLatestReleaseAsync_Release_HandsBackVersionTagAndPage()
    {
        var check = CheckAnswering(ReleaseJson("v0.4.0"), out var handler);

        var release = await check.GetLatestReleaseAsync();

        Assert.NotNull(release);
        Assert.Equal(ModVersion.Parse("0.4.0"), release.Version);
        Assert.Equal("v0.4.0", release.Tag);
        Assert.Equal("https://github.com/KSAModding/Borea/releases/tag/v0.4.0", release.PageUrl);

        var request = handler.LastRequest!;
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.github.com/repos/KSAModding/Borea/releases/latest", request.RequestUri!.AbsoluteUri);
        Assert.Contains(request.Headers.Accept, accept => accept.MediaType == "application/vnd.github+json");
        Assert.Equal(["2026-03-10"], request.Headers.GetValues("X-GitHub-Api-Version"));
    }

    [Theory]
    [InlineData("v0.5.0", "0.4.0", true)]
    [InlineData("v0.4.0", "0.4.0", false)]
    [InlineData("v0.4.0", "0.4.0+abc123", false)]
    [InlineData("v0.3.9", "0.4.0", false)]
    [InlineData("v0.4.0", "0.4.0-beta.1", true)]
    public async Task GetLatestReleaseAsync_ComparedWithTheRunningVersion_IsNewerOnlyWhenItRanksHigher(string tag, string running, bool newer)
    {
        var check = CheckAnswering(ReleaseJson(tag), out _);

        var release = await check.GetLatestReleaseAsync();

        Assert.NotNull(release);
        Assert.Equal(newer, release.IsNewerThan(running));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetLatestReleaseAsync_UnsuccessfulAnswer_IsNoRelease(HttpStatusCode status)
    {
        var client = FakeHttpMessageHandler.BuildClient(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(NotFoundJson, Encoding.UTF8, "application/json"),
        }, out _);

        Assert.Null(await new BoreaReleaseCheck(client).GetLatestReleaseAsync());
    }

    [Fact]
    public async Task GetLatestReleaseAsync_FailedRequest_IsNoRelease()
    {
        var client = FakeHttpMessageHandler.BuildClient(
            (Func<HttpRequestMessage, HttpResponseMessage>)(_ => throw new HttpRequestException("No route to host.")),
            out _);

        Assert.Null(await new BoreaReleaseCheck(client).GetLatestReleaseAsync());
    }

    [Fact]
    public async Task GetLatestReleaseAsync_ClientTimeout_IsNoRelease()
    {
        var client = FakeHttpMessageHandler.BuildClient(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, out _);
        client.Timeout = TimeSpan.FromMilliseconds(50);

        Assert.Null(await new BoreaReleaseCheck(client).GetLatestReleaseAsync());
    }

    [Fact]
    public async Task GetLatestReleaseAsync_ClientDisposedDuringTheRequest_IsNoRelease()
    {
        var started = new TaskCompletionSource();
        var client = FakeHttpMessageHandler.BuildClient(async (_, token) =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (TaskCanceledException)
            {
                // a plain OperationCanceledException, not a TaskCanceledException
                throw new OperationCanceledException(token);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }, out _);
        var pending = new BoreaReleaseCheck(client).GetLatestReleaseAsync();
        await started.Task;

        client.Dispose();

        Assert.Null(await pending);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_CallerCancelsDuringTheRequest_Throws()
    {
        var client = FakeHttpMessageHandler.BuildClient(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return FakeHttpMessageHandler.JsonResponse(ReleaseJson("v0.4.0"));
        }, out _);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new BoreaReleaseCheck(client).GetLatestReleaseAsync(BoreaUpdateChannel.Stable, cancellation.Token));
    }

    [Theory]
    [InlineData("nightly")]
    [InlineData("v0.4")]
    [InlineData("vv0.4.0")]
    [InlineData("")]
    public async Task GetLatestReleaseAsync_UnparseableTag_IsNoRelease(string tag)
    {
        var check = CheckAnswering(ReleaseJson(tag), out _);

        Assert.Null(await check.GetLatestReleaseAsync());
    }

    [Theory]
    [InlineData("<html>sign in to continue</html>")]
    [InlineData("null")]
    [InlineData("""{"message":"no tag here"}""")]
    public async Task GetLatestReleaseAsync_UnusableBody_IsNoRelease(string body)
    {
        var check = CheckAnswering(body, out _);

        Assert.Null(await check.GetLatestReleaseAsync());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task GetLatestReleaseAsync_DraftOrPreRelease_IsNoRelease(bool draft, bool prerelease)
    {
        var check = CheckAnswering(ReleaseJson("v0.5.0-beta.1", draft, prerelease), out _);

        Assert.Null(await check.GetLatestReleaseAsync());
    }

    [Theory]
    [InlineData("http://github.com/KSAModding/Borea/releases/tag/v0.4.0")]
    [InlineData("https://example.com/KSAModding/Borea/releases/tag/v0.4.0")]
    [InlineData("releases/tag/v0.4.0")]
    [InlineData("https://github.com.example.com/KSAModding/Borea/releases/tag/v0.4.0")]
    [InlineData("https://github.com/someone/else/releases/tag/v0.4.0")]
    [InlineData("https://github.com/KSAModding/Borea/releases/../../../someone/else")]
    public async Task GetLatestReleaseAsync_PageNotABoreaReleasePage_IsNoRelease(string htmlUrl)
    {
        var check = CheckAnswering(ReleaseJson("v0.4.0", htmlUrl: htmlUrl), out _);

        Assert.Null(await check.GetLatestReleaseAsync());
    }

    [Fact]
    public async Task GetLatestReleaseAsync_Dev_AsksTheReleasesListOnce()
    {
        var calls = 0;
        var client = FakeHttpMessageHandler.BuildClient(_ =>
        {
            calls++;
            return FakeHttpMessageHandler.JsonResponse(ReleasesJson(ReleaseJson("v0.5.0-beta.1", prerelease: true)));
        }, out var handler);

        var release = await new BoreaReleaseCheck(client).GetLatestReleaseAsync(BoreaUpdateChannel.Dev);

        Assert.NotNull(release);
        Assert.Equal(ModVersion.Parse("0.5.0-beta.1"), release.Version);
        Assert.Equal("https://github.com/KSAModding/Borea/releases/tag/v0.5.0-beta.1", release.PageUrl);
        Assert.Equal(1, calls);
        var request = handler.LastRequest!;
        Assert.Equal("https://api.github.com/repos/KSAModding/Borea/releases?per_page=30", request.RequestUri!.AbsoluteUri);
        Assert.Equal(["2026-03-10"], request.Headers.GetValues("X-GitHub-Api-Version"));
    }

    [Theory]
    [InlineData(BoreaUpdateChannel.Testing, "v0.5.0-beta.2")]
    [InlineData(BoreaUpdateChannel.Dev, "v0.5.1-dev.1")]
    public async Task GetLatestReleaseAsync_List_PicksTheHighestVersionInTheChannel(BoreaUpdateChannel channel, string expectedTag)
    {
        var check = CheckAnswering(
            ReleasesJson(
                ReleaseJson("v0.4.1"),
                ReleaseJson("v0.5.0-beta.2", prerelease: true),
                ReleaseJson("v0.5.1-dev.1", prerelease: true),
                ReleaseJson("v0.6.0-beta.1", draft: true, prerelease: true),
                ReleaseJson("nightly", prerelease: true),
                ReleaseJson("v0.9.0", htmlUrl: "https://github.com/someone/else/releases/tag/v0.9.0"),
                ReleaseJson("v0.5.0-beta.1", prerelease: true),
                "null"),
            out _);

        var release = await check.GetLatestReleaseAsync(channel);

        Assert.NotNull(release);
        Assert.Equal(expectedTag, release.Tag);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_TestingWithAStableReleaseOnTop_HandsBackTheStableRelease()
    {
        var check = CheckAnswering(ReleasesJson(ReleaseJson("v0.5.0-beta.1", prerelease: true), ReleaseJson("v0.5.0")), out _);

        var release = await check.GetLatestReleaseAsync(BoreaUpdateChannel.Testing);

        Assert.NotNull(release);
        Assert.Equal("v0.5.0", release.Tag);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""{"message":"Not Found"}""")]
    [InlineData("null")]
    public async Task GetLatestReleaseAsync_ListWithoutAUsableRelease_IsNoRelease(string body)
    {
        var check = CheckAnswering(body, out _);

        Assert.Null(await check.GetLatestReleaseAsync(BoreaUpdateChannel.Dev));
    }

    [Fact]
    public async Task GetLatestReleaseAsync_ListUnsuccessfulAnswer_IsNoRelease()
    {
        var client = FakeHttpMessageHandler.BuildClient(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(NotFoundJson, Encoding.UTF8, "application/json"),
        }, out _);

        Assert.Null(await new BoreaReleaseCheck(client).GetLatestReleaseAsync(BoreaUpdateChannel.Dev));
    }
}
