using System.Net;
using Borea.Core.Game;
using Borea.Network.MasterServer;

namespace Borea.Network.Tests;

public sealed class LatestVersionPingTests
{
    private const string RealResponseJson = """{"Version":"2026.8.3.5117","Url":"https://ksa.ahwoo.com"}""";

    [Fact]
    public async Task PingAsync_RealShapedResponse_HandsBackVersionAndUrl()
    {
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(RealResponseJson), out var handler);
        var ping = new LatestVersionPing(client, new FakeTimeProvider());

        var answer = await ping.PingAsync();

        Assert.Equal(GameVersion.Parse("2026.8.3.5117"), answer.Version);
        Assert.Equal("2026.8.3.5117", answer.RawVersion);
        Assert.Equal("https://ksa.ahwoo.com", answer.DownloadUrl);
        Assert.Equal("http://ksa-master1.rocketwerkz.com:8082/version", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task PingAsync_DecoratedVersionString_StillParses()
    {
        var json = """{"Version":"v2026.8.3.5117+6b87889f","Url":"https://ksa.ahwoo.com"}""";
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out _);
        var ping = new LatestVersionPing(client, new FakeTimeProvider());

        var answer = await ping.PingAsync();

        Assert.NotNull(answer.Version);
        Assert.Equal(5117, answer.Version!.Value.Revision);
    }

    [Fact]
    public async Task PingAsync_UnparseableVersion_BecomesUnknownInsteadOfThrowing()
    {
        var json = """{"Version":"soon","Url":"https://ksa.ahwoo.com"}""";
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out _);
        var ping = new LatestVersionPing(client, new FakeTimeProvider());

        var answer = await ping.PingAsync();

        Assert.Null(answer.Version);
        Assert.Equal("soon", answer.RawVersion);
        Assert.Equal("https://ksa.ahwoo.com", answer.DownloadUrl);
    }

    [Fact]
    public async Task PingAsync_NullBody_BecomesUnknownInsteadOfThrowing()
    {
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse("null"), out _);
        var ping = new LatestVersionPing(client, new FakeTimeProvider());

        var answer = await ping.PingAsync();

        Assert.Null(answer.Version);
        Assert.Equal(string.Empty, answer.RawVersion);
        Assert.Equal(string.Empty, answer.DownloadUrl);
    }

    [Fact]
    public async Task PingAsync_WithinAMinute_ReturnsCachedAnswerWithoutASecondRequest()
    {
        var requests = 0;
        var client = FakeHttpMessageHandler.BuildClient(_ =>
        {
            requests++;
            return FakeHttpMessageHandler.JsonResponse(RealResponseJson);
        }, out _);
        var time = new FakeTimeProvider();
        var ping = new LatestVersionPing(client, time);

        var first = await ping.PingAsync();
        time.UtcNow += TimeSpan.FromSeconds(30);
        var second = await ping.PingAsync();

        Assert.Equal(1, requests);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task PingAsync_AfterAMinute_PingsAgainAndHandsBackTheFreshAnswer()
    {
        var requests = 0;
        var client = FakeHttpMessageHandler.BuildClient(_ =>
        {
            requests++;
            return FakeHttpMessageHandler.JsonResponse(requests == 1
                ? RealResponseJson
                : """{"Version":"2026.9.1.5200","Url":"https://ksa.ahwoo.com"}""");
        }, out _);
        var time = new FakeTimeProvider();
        var ping = new LatestVersionPing(client, time);

        await ping.PingAsync();
        time.UtcNow += TimeSpan.FromSeconds(61);
        var second = await ping.PingAsync();

        Assert.Equal(2, requests);
        Assert.Equal(GameVersion.Parse("2026.9.1.5200"), second.Version);
    }

    [Fact]
    public async Task PingAsync_NonJsonBody_BecomesUnknownInsteadOfThrowing()
    {
        var client = FakeHttpMessageHandler.BuildClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>sign in to continue</html>", System.Text.Encoding.UTF8, "text/html"),
        }, out _);
        var ping = new LatestVersionPing(client, new FakeTimeProvider());

        var answer = await ping.PingAsync();

        Assert.Null(answer.Version);
        Assert.Equal(string.Empty, answer.RawVersion);
    }

    [Fact]
    public async Task PingAsync_OverlappingCalls_ShareOneRequest()
    {
        var requests = 0;
        var pending = new TaskCompletionSource<HttpResponseMessage>();
        var client = FakeHttpMessageHandler.BuildClient(_ =>
        {
            requests++;
            return pending.Task;
        }, out _);
        var ping = new LatestVersionPing(client, new FakeTimeProvider());

        var first = ping.PingAsync();
        var second = ping.PingAsync();
        Assert.False(first.IsCompleted);
        pending.SetResult(FakeHttpMessageHandler.JsonResponse(RealResponseJson));

        Assert.Same(await first, await second);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task PingAsync_TransportFailure_ThrowsAndDoesNotStartTheCacheWindow()
    {
        var requests = 0;
        var client = FakeHttpMessageHandler.BuildClient(_ =>
        {
            requests++;
            return requests == 1
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : FakeHttpMessageHandler.JsonResponse(RealResponseJson);
        }, out _);
        var ping = new LatestVersionPing(client, new FakeTimeProvider());

        await Assert.ThrowsAsync<HttpRequestException>(() => ping.PingAsync());
        var answer = await ping.PingAsync();

        Assert.Equal(2, requests);
        Assert.NotNull(answer.Version);
    }
}
