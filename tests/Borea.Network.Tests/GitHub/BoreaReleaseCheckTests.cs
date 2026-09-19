using System.Net;
using System.Text;
using System.Text.Json;
using Borea.Core.Mods;
using Borea.Core.Updates;
using Borea.Network.GitHub;

namespace Borea.Network.Tests;

public sealed class BoreaReleaseCheckTests
{
    /// <summary>A GitHub error body, captured from the real endpoint.</summary>
    private const string NotFoundJson = """{"message":"Not Found","documentation_url":"https://docs.github.com/rest/releases/releases#get-the-latest-release","status":"404"}""";

    private const string ReleasesUrl = "https://api.github.com/repos/KSAModding/Borea/releases?per_page=100";

    /// <summary>The fields the check reads, in the shape of a real answer.</summary>
    private static string ReleaseJson(string tag, bool draft = false, bool prerelease = false, string? htmlUrl = null, string body = "") =>
        $$"""{"html_url":"{{htmlUrl ?? "https://github.com/KSAModding/Borea/releases/tag/" + tag}}","tag_name":"{{tag}}","name":"Borea {{tag}}","draft":{{(draft ? "true" : "false")}},"prerelease":{{(prerelease ? "true" : "false")}},"published_at":"2026-09-13T12:00:00Z","body":{{JsonSerializer.Serialize(body)}}}""";

    private static string ReleasesJson(params string[] releases) => "[" + string.Join(",", releases) + "]";

    private static BoreaReleaseCheck CheckAnswering(string json, out FakeHttpMessageHandler handler) =>
        new(FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out handler));

    [Fact]
    public async Task GetReleasesAsync_Release_HandsBackVersionTagPageNotesAndDate()
    {
        var check = CheckAnswering(ReleasesJson(ReleaseJson("v0.4.0", body: "## Changes\n\n- Faster start")), out var handler);

        var release = Assert.Single(await check.GetReleasesAsync());

        Assert.Equal(ModVersion.Parse("0.4.0"), release.Version);
        Assert.Equal("v0.4.0", release.Tag);
        Assert.Equal("https://github.com/KSAModding/Borea/releases/tag/v0.4.0", release.PageUrl);
        Assert.Equal("## Changes\n\n- Faster start", release.Notes);
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero), release.PublishedAt);

        var request = handler.LastRequest!;
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(ReleasesUrl, request.RequestUri!.AbsoluteUri);
        Assert.Contains(request.Headers.Accept, accept => accept.MediaType == "application/vnd.github+json");
        Assert.Equal(["2026-03-10"], request.Headers.GetValues("X-GitHub-Api-Version"));
    }

    [Fact]
    public async Task GetReleasesAsync_BlankBody_HasNoNotes()
    {
        var check = CheckAnswering(ReleasesJson(ReleaseJson("v0.4.0", body: "  ")), out _);

        Assert.Null(Assert.Single(await check.GetReleasesAsync()).Notes);
    }

    [Theory]
    [InlineData("v0.5.0", "0.4.0", true)]
    [InlineData("v0.4.0", "0.4.0", false)]
    [InlineData("v0.4.0", "0.4.0+abc123", false)]
    [InlineData("v0.3.9", "0.4.0", false)]
    [InlineData("v0.4.0", "0.4.0-beta.1", true)]
    public async Task GetReleasesAsync_ComparedWithTheRunningVersion_IsNewerOnlyWhenItRanksHigher(string tag, string running, bool newer)
    {
        var check = CheckAnswering(ReleasesJson(ReleaseJson(tag)), out _);

        var release = Assert.Single(await check.GetReleasesAsync());

        Assert.Equal(newer, release.IsNewerThan(running));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetReleasesAsync_UnsuccessfulAnswer_IsNoRelease(HttpStatusCode status)
    {
        var client = FakeHttpMessageHandler.BuildClient(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(NotFoundJson, Encoding.UTF8, "application/json"),
        }, out _);

        Assert.Empty(await new BoreaReleaseCheck(client).GetReleasesAsync(BoreaUpdateChannel.Dev));
    }

    [Fact]
    public async Task GetReleasesAsync_FailedRequest_IsNoRelease()
    {
        var client = FakeHttpMessageHandler.BuildClient(
            (Func<HttpRequestMessage, HttpResponseMessage>)(_ => throw new HttpRequestException("No route to host.")),
            out _);

        Assert.Empty(await new BoreaReleaseCheck(client).GetReleasesAsync());
    }

    [Fact]
    public async Task GetReleasesAsync_ClientTimeout_IsNoRelease()
    {
        var client = FakeHttpMessageHandler.BuildClient(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, out _);
        client.Timeout = TimeSpan.FromMilliseconds(50);

        Assert.Empty(await new BoreaReleaseCheck(client).GetReleasesAsync());
    }

    [Fact]
    public async Task GetReleasesAsync_ClientDisposedDuringTheRequest_IsNoRelease()
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
        var pending = new BoreaReleaseCheck(client).GetReleasesAsync();
        await started.Task;

        client.Dispose();

        Assert.Empty(await pending);
    }

    [Fact]
    public async Task GetReleasesAsync_CallerCancelsDuringTheRequest_Throws()
    {
        var client = FakeHttpMessageHandler.BuildClient(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return FakeHttpMessageHandler.JsonResponse(ReleasesJson(ReleaseJson("v0.4.0")));
        }, out _);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new BoreaReleaseCheck(client).GetReleasesAsync(BoreaUpdateChannel.Stable, cancellation.Token));
    }

    [Theory]
    [InlineData("nightly")]
    [InlineData("v0.4")]
    [InlineData("vv0.4.0")]
    [InlineData("")]
    public async Task GetReleasesAsync_UnparseableTag_IsNoRelease(string tag)
    {
        var check = CheckAnswering(ReleasesJson(ReleaseJson(tag)), out _);

        Assert.Empty(await check.GetReleasesAsync(BoreaUpdateChannel.Dev));
    }

    [Theory]
    [InlineData("<html>sign in to continue</html>")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("""{"message":"no tag here"}""")]
    [InlineData("""[{"message":"no tag here"}]""")]
    public async Task GetReleasesAsync_UnusableBody_IsNoRelease(string body)
    {
        var check = CheckAnswering(body, out _);

        Assert.Empty(await check.GetReleasesAsync(BoreaUpdateChannel.Dev));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task GetReleasesAsync_StableWithADraftOrPreRelease_IsNoRelease(bool draft, bool prerelease)
    {
        var check = CheckAnswering(ReleasesJson(ReleaseJson("v0.5.0-beta.1", draft, prerelease)), out _);

        Assert.Empty(await check.GetReleasesAsync());
    }

    [Fact]
    public async Task GetReleasesAsync_StableWithAReleaseMarkedAsPreRelease_LeavesItOut()
    {
        var check = CheckAnswering(ReleasesJson(ReleaseJson("v0.5.0", prerelease: true), ReleaseJson("v0.4.0")), out _);

        var releases = await check.GetReleasesAsync();

        Assert.Equal(["v0.4.0"], releases.Select(release => release.Tag));
    }

    [Theory]
    [InlineData("http://github.com/KSAModding/Borea/releases/tag/v0.4.0")]
    [InlineData("https://example.com/KSAModding/Borea/releases/tag/v0.4.0")]
    [InlineData("releases/tag/v0.4.0")]
    [InlineData("https://github.com.example.com/KSAModding/Borea/releases/tag/v0.4.0")]
    [InlineData("https://github.com/someone/else/releases/tag/v0.4.0")]
    [InlineData("https://github.com/KSAModding/Borea/releases/../../../someone/else")]
    public async Task GetReleasesAsync_PageNotABoreaReleasePage_IsNoRelease(string htmlUrl)
    {
        var check = CheckAnswering(ReleasesJson(ReleaseJson("v0.4.0", htmlUrl: htmlUrl)), out _);

        Assert.Empty(await check.GetReleasesAsync());
    }

    [Theory]
    [InlineData(BoreaUpdateChannel.Stable)]
    [InlineData(BoreaUpdateChannel.Testing)]
    [InlineData(BoreaUpdateChannel.Dev)]
    public async Task GetReleasesAsync_EveryChannel_AsksTheReleasesListOnce(BoreaUpdateChannel channel)
    {
        var calls = 0;
        var client = FakeHttpMessageHandler.BuildClient(_ =>
        {
            calls++;
            return FakeHttpMessageHandler.JsonResponse(ReleasesJson(ReleaseJson("v0.4.0")));
        }, out var handler);

        await new BoreaReleaseCheck(client).GetReleasesAsync(channel);

        Assert.Equal(1, calls);
        Assert.Equal(ReleasesUrl, handler.LastRequest!.RequestUri!.AbsoluteUri);
        Assert.Equal(["2026-03-10"], handler.LastRequest.Headers.GetValues("X-GitHub-Api-Version"));
    }

    [Theory]
    [InlineData(BoreaUpdateChannel.Stable, new[] { "v0.4.1" })]
    [InlineData(BoreaUpdateChannel.Testing, new[] { "v0.5.0-beta.2", "v0.5.0-beta.1", "v0.4.1" })]
    [InlineData(BoreaUpdateChannel.Dev, new[] { "v0.5.1-dev.1", "v0.5.0-beta.2", "v0.5.0-beta.1", "v0.4.1" })]
    public async Task GetReleasesAsync_List_KeepsTheReleasesOfTheChannelNewestFirst(BoreaUpdateChannel channel, string[] expectedTags)
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

        var releases = await check.GetReleasesAsync(channel);

        Assert.Equal(expectedTags, releases.Select(release => release.Tag));
    }

    [Fact]
    public async Task GetReleasesAsync_TestingWithAStableReleaseBelowABeta_PutsTheStableReleaseFirst()
    {
        var check = CheckAnswering(ReleasesJson(ReleaseJson("v0.5.0-beta.1", prerelease: true), ReleaseJson("v0.5.0")), out _);

        var releases = await check.GetReleasesAsync(BoreaUpdateChannel.Testing);

        Assert.Equal(["v0.5.0", "v0.5.0-beta.1"], releases.Select(release => release.Tag));
    }
}
