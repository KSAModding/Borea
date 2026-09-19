using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Borea.Core.Announcements;
using Borea.Network.Announcements;

namespace Borea.Network.Tests.Announcements;

public sealed class AnnouncementFetcherTests : IDisposable
{
    private const string Cached = "spec_version = 1\n";

    private const string Updated = "spec_version = 1\n# a new post\n";

    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "borea-announcement-tests-" + Guid.NewGuid())).FullName;

    private string CachePath => Path.Combine(_root, "announcements.toml");

    private string EtagPath => CachePath + ".etag";

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static AnnouncementFetcher Fetcher(HttpClient client) => new(client, AnnouncementFetcher.DefaultUri, new FakeReader());

    private static HttpResponseMessage Toml(string text, string? etag = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/plain") };
        if (etag is not null)
            response.Headers.ETag = new EntityTagHeaderValue(etag);
        return response;
    }

    private async Task WriteCacheAsync()
    {
        await File.WriteAllTextAsync(CachePath, Cached);
        await File.WriteAllTextAsync(EtagPath, "\"v1\"");
    }

    [Fact]
    public async Task FetchAsync_NoCache_DownloadsTheFileAndItsEtag()
    {
        var client = FakeHttpMessageHandler.BuildClient(_ => Toml(Cached, "\"v1\""), out var handler);

        Assert.True(await Fetcher(client).FetchAsync(CachePath));

        Assert.Equal(AnnouncementFetcher.DefaultUri, handler.LastRequest!.RequestUri);
        Assert.Empty(handler.LastRequest.Headers.IfNoneMatch);
        Assert.Equal(Cached, await File.ReadAllTextAsync(CachePath));
        Assert.Equal("\"v1\"", await File.ReadAllTextAsync(EtagPath));
        Assert.False(File.Exists(CachePath + ".tmp"));
    }

    [Fact]
    public async Task FetchAsync_EtagMatches_KeepsTheCache()
    {
        await WriteCacheAsync();
        var client = FakeHttpMessageHandler.BuildClient(_ => new HttpResponseMessage(HttpStatusCode.NotModified), out var handler);

        Assert.False(await Fetcher(client).FetchAsync(CachePath));

        Assert.Contains(handler.LastRequest!.Headers.IfNoneMatch, tag => tag.Tag == "\"v1\"");
        Assert.Equal(Cached, await File.ReadAllTextAsync(CachePath));
        Assert.Equal("\"v1\"", await File.ReadAllTextAsync(EtagPath));
    }

    [Fact]
    public async Task FetchAsync_NewFile_ReplacesTheCacheAndTheEtag()
    {
        await WriteCacheAsync();
        var client = FakeHttpMessageHandler.BuildClient(_ => Toml(Updated, "\"v2\""), out _);

        Assert.True(await Fetcher(client).FetchAsync(CachePath));

        Assert.Equal(Updated, await File.ReadAllTextAsync(CachePath));
        Assert.Equal("\"v2\"", await File.ReadAllTextAsync(EtagPath));
    }

    [Fact]
    public async Task FetchAsync_BrokenFile_KeepsTheCacheAndTheEtag()
    {
        await WriteCacheAsync();
        var client = FakeHttpMessageHandler.BuildClient(_ => Toml("broken", "\"v2\""), out _);

        await Assert.ThrowsAsync<InvalidDataException>(() => Fetcher(client).FetchAsync(CachePath));

        Assert.Equal(Cached, await File.ReadAllTextAsync(CachePath));
        Assert.Equal("\"v1\"", await File.ReadAllTextAsync(EtagPath));
        Assert.False(File.Exists(CachePath + ".tmp"));
    }

    [Fact]
    public async Task FetchAsync_NotModifiedWithoutACache_Throws()
    {
        var client = FakeHttpMessageHandler.BuildClient(_ => new HttpResponseMessage(HttpStatusCode.NotModified), out _);

        await Assert.ThrowsAsync<HttpRequestException>(() => Fetcher(client).FetchAsync(CachePath));

        Assert.False(File.Exists(CachePath));
    }

    [Fact]
    public async Task FetchAsync_ServerError_KeepsTheCache()
    {
        await WriteCacheAsync();
        var client = FakeHttpMessageHandler.BuildClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError), out _);

        await Assert.ThrowsAsync<HttpRequestException>(() => Fetcher(client).FetchAsync(CachePath));

        Assert.Equal(Cached, await File.ReadAllTextAsync(CachePath));
    }

    /// <summary>Rejects a file that holds "broken", like the real reader rejects a file that is not an announcements file.</summary>
    private sealed class FakeReader : IAnnouncementReader
    {
        public async Task<AnnouncementFile> ReadAsync(string path, CancellationToken cancellationToken = default)
        {
            if ((await File.ReadAllTextAsync(path, cancellationToken)).Contains("broken", StringComparison.Ordinal))
                throw new InvalidDataException("The file is broken.");

            return new AnnouncementFile([], []);
        }
    }
}
