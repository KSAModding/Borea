namespace Borea.Network.Tests;

/// <summary>
/// Routes outgoing HttpClient requests to a caller-supplied responder
/// instead of hitting the network. Captures the last request for assertion.
/// The responder gets the request's cancellation token, so it can stall the
/// way a silent host does and still end when HttpClient.Timeout fires.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

    public HttpRequestMessage? LastRequest { get; private set; }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        : this((request, _) => responder(request))
    {
    }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return _responder(request, cancellationToken);
    }

    public static HttpClient BuildClient(Func<HttpRequestMessage, HttpResponseMessage> responder, out FakeHttpMessageHandler handler) =>
        BuildClient((request, _) => Task.FromResult(responder(request)), out handler);

    public static HttpClient BuildClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder, out FakeHttpMessageHandler handler) =>
        BuildClient((request, _) => responder(request), out handler);

    public static HttpClient BuildClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder, out FakeHttpMessageHandler handler)
    {
        handler = new FakeHttpMessageHandler(responder);
        return new HttpClient(handler) { BaseAddress = new Uri("https://spacedock.info/") };
    }

    public static HttpResponseMessage JsonResponse(string json) => new(System.Net.HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };

    public static HttpResponseMessage ByteResponse(byte[] bytes) => new(System.Net.HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(bytes),
    };
}
