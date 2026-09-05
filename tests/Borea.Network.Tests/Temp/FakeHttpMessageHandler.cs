namespace Borea.Network.Tests;

/// <summary>
/// Routes outgoing HttpClient requests to a caller-supplied responder
/// instead of hitting the network. Captures the last request for assertion.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder;

    public HttpRequestMessage? LastRequest { get; private set; }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return _responder(request);
    }

    public static HttpClient BuildClient(Func<HttpRequestMessage, HttpResponseMessage> responder, out FakeHttpMessageHandler handler) =>
        BuildClient(request => Task.FromResult(responder(request)), out handler);

    public static HttpClient BuildClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder, out FakeHttpMessageHandler handler)
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
