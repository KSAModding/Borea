using System.Buffers.Binary;
using System.Net;
using System.Text;
using Borea.Core.Listings;
using Borea.Network.Listings;

namespace Borea.Network.Tests.Listings;

public sealed class ListingHostClientTests
{
    private const string Repository = """
        {
          "name": "KSA-MyMod",
          "full_name": "Owner/KSA-MyMod",
          "html_url": "https://github.com/Owner/KSA-MyMod",
          "description": "Does a thing.",
          "homepage": "https://forums.ahwoo.com/threads/my-mod.42/",
          "has_issues": true,
          "owner": { "login": "Owner", "type": "User" },
          "license": { "spdx_id": "MIT" }
        }
        """;

    [Fact]
    public async Task ReadAsync_GitHubRepository_MapsItsFactsAndTheNewestRelease()
    {
        var requests = new List<Uri>();
        var client = new ListingHostClient(FakeHttpMessageHandler.BuildClient(request =>
        {
            requests.Add(request.RequestUri!);
            return request.RequestUri!.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal)
                ? FakeHttpMessageHandler.JsonResponse("""
                    [
                      { "tag_name": "v2.0.0", "draft": true, "published_at": "2026-09-10T00:00:00Z", "assets": [] },
                      { "tag_name": "nightly", "draft": false, "published_at": "2026-09-09T00:00:00Z", "assets": [] },
                      { "tag_name": "v1.1.0", "draft": false, "published_at": "2026-09-08T00:00:00Z", "assets": [
                        { "name": "MyMod.zip", "state": "uploaded", "content_type": "application/zip", "size": 1234, "browser_download_url": "https://github.com/Owner/KSA-MyMod/releases/download/v1.1.0/MyMod.zip" }
                      ] },
                      { "tag_name": "v1.0.0", "draft": false, "published_at": "2026-09-01T00:00:00Z", "assets": [] }
                    ]
                    """)
                : FakeHttpMessageHandler.JsonResponse(Repository);
        }, out _));

        var facts = await client.ReadAsync(new ListingSourceReference.GitHub("owner", "ksa-mymod"));

        Assert.Equal("KSA-MyMod", facts.Name);
        Assert.Equal("Does a thing.", facts.Abstract);
        Assert.Equal("MIT", facts.License);
        Assert.Equal(["Owner"], facts.Authors);
        Assert.Equal(
            [
                new ListingLink("forums", "https://forums.ahwoo.com/threads/my-mod.42/"),
                new ListingLink("repository", "https://github.com/Owner/KSA-MyMod"),
                new ListingLink("bugtracker", "https://github.com/Owner/KSA-MyMod/issues"),
            ],
            facts.Links);
        Assert.Equal(new ListingReleases("Owner/KSA-MyMod", null), facts.Releases);
        Assert.Equal("v1.1.0", facts.Latest!.Tag);
        Assert.Equal("1.1.0", facts.Latest.Version);
        Assert.Equal("https://github.com/Owner/KSA-MyMod/releases/download/v1.1.0/MyMod.zip", facts.Latest.DownloadUrl);
        Assert.Equal(1234, facts.Latest.SizeBytes);
        Assert.Equal("https://api.github.com/repos/owner/ksa-mymod", requests[0].AbsoluteUri);
        Assert.Equal("https://api.github.com/repos/Owner/KSA-MyMod/releases?per_page=100", requests[1].AbsoluteUri);
    }

    [Theory]
    [InlineData("KSA-MyMod.zip", "KSA-MyMod.zip")]
    [InlineData("ksa-mymod-1.0.0.zip", "ksa-mymod-1.0.0.zip")]
    [InlineData("other.zip", null)]
    public async Task ReadAsync_SeveralArchives_TakesTheOneNamedAfterTheListing(string named, string? chosen)
    {
        var releases = $$"""
            [ { "tag_name": "v1.0.0", "draft": false, "published_at": "2026-09-08T00:00:00Z", "assets": [
              { "name": "{{named}}", "state": "uploaded", "size": 1, "browser_download_url": "https://example.com/{{named}}" },
              { "name": "Sources.zip", "state": "uploaded", "size": 1, "browser_download_url": "https://example.com/Sources.zip" },
              { "name": "notes.txt", "state": "uploaded", "size": 1, "browser_download_url": "https://example.com/notes.txt" }
            ] } ]
            """;
        var client = new ListingHostClient(FakeHttpMessageHandler.BuildClient(request =>
            FakeHttpMessageHandler.JsonResponse(request.RequestUri!.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal) ? releases : Repository), out _));

        var latest = (await client.ReadAsync(new ListingSourceReference.GitHub("Owner", "KSA-MyMod"))).Latest!;

        Assert.Equal(chosen is null ? null : $"https://example.com/{chosen}", latest.DownloadUrl);
        Assert.Equal(chosen is null ? [named, "Sources.zip"] : [], latest.Candidates);
    }

    [Fact]
    public async Task ReadAsync_UnknownRepository_ThrowsListingSourceException()
    {
        var client = new ListingHostClient(FakeHttpMessageHandler.BuildClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound), out _));

        var error = await Assert.ThrowsAsync<ListingSourceException>(() => client.ReadAsync(new ListingSourceReference.GitHub("owner", "gone")));

        Assert.Contains("owner/gone", error.Message);
    }

    [Fact]
    public async Task ReadAsync_RateLimitedGitHub_ThrowsHttpRequestException()
    {
        var client = new ListingHostClient(FakeHttpMessageHandler.BuildClient(_ => new HttpResponseMessage(HttpStatusCode.Forbidden), out _));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.ReadAsync(new ListingSourceReference.GitHub("owner", "repo")));
    }

    [Fact]
    public async Task ReadAsync_SpaceDockMod_MapsItsFactsAndTheNewestVersion()
    {
        var client = new ListingHostClient(FakeHttpMessageHandler.BuildClient(request =>
        {
            Assert.Equal("https://spacedock.info/api/mod/4253", request.RequestUri!.AbsoluteUri);
            return FakeHttpMessageHandler.JsonResponse("""
                {
                  "id": 4253, "name": "Advanced Flight Computer", "author": "Maxi", "short_description": "Planning tools.",
                  "license": "MIT", "website": "https://forums.ahwoo.com/threads/advanced-flight-computer.783/",
                  "source_code": "https://github.com/Maximilian-Nesslauer/KSA-AdvancedFlightComputer",
                  "url": "/mod/4253/AdvancedFlightComputer", "game_id": 22409,
                  "versions": [
                    { "id": 2, "friendly_version": "0.7.5", "download_path": "/mod/4253/AdvancedFlightComputer/download/0.7.5", "created": "2026-09-10T00:00:00+00:00", "game_version": "2026.9.10.5438" },
                    { "id": 1, "friendly_version": "0.7.4", "download_path": "/mod/4253/AdvancedFlightComputer/download/0.7.4", "created": "2026-09-01T00:00:00+00:00", "game_version": "2026.9.7.5402" }
                  ]
                }
                """);
        }, out _));

        var facts = await client.ReadAsync(new ListingSourceReference.SpaceDock(4253));

        Assert.Equal("Advanced Flight Computer", facts.Name);
        Assert.Equal(["Maxi"], facts.Authors);
        Assert.Equal("Planning tools.", facts.Abstract);
        Assert.Equal("https://forums.ahwoo.com/threads/advanced-flight-computer.783/", facts.Links.Single(link => link.Key == "forums").Url);
        Assert.Equal("https://github.com/Maximilian-Nesslauer/KSA-AdvancedFlightComputer", facts.Links.Single(link => link.Key == "repository").Url);
        Assert.Equal("https://spacedock.info/mod/4253/AdvancedFlightComputer", facts.Links.Single(link => link.Key == "spacedock").Url);
        Assert.Equal(new ListingReleases(null, 4253), facts.Releases);
        Assert.Equal("0.7.5", facts.Latest!.Version);
        Assert.Equal("https://spacedock.info/mod/4253/AdvancedFlightComputer/download/0.7.5", facts.Latest.DownloadUrl);
    }

    [Theory]
    [InlineData("@evil.example/mod.zip")]
    [InlineData("//evil.example/mod.zip")]
    [InlineData("https://evil.example/mod.zip")]
    public async Task ReadAsync_SpaceDockPathsThatLeaveSpaceDock_AreNotUsed(string path)
    {
        var client = new ListingHostClient(FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse($$"""
            {
              "id": 4253, "name": "Mod", "url": "{{path}}", "game_id": 22409,
              "versions": [ { "id": 1, "friendly_version": "1.0.0", "download_path": "{{path}}", "created": "2026-09-01T00:00:00+00:00" } ]
            }
            """), out _));

        var facts = await client.ReadAsync(new ListingSourceReference.SpaceDock(4253));

        Assert.Equal("https://spacedock.info/mod/4253", facts.Links.Single(link => link.Key == "spacedock").Url);
        Assert.Null(facts.Latest!.DownloadUrl);
    }

    [Fact]
    public async Task ReadAsync_SpaceDockModOfAnotherGame_ThrowsListingSourceException()
    {
        var client = new ListingHostClient(FakeHttpMessageHandler.BuildClient(_ =>
            FakeHttpMessageHandler.JsonResponse("""{ "id": 7, "name": "Other", "game_id": 3102, "versions": [] }"""), out _));

        await Assert.ThrowsAsync<ListingSourceException>(() => client.ReadAsync(new ListingSourceReference.SpaceDock(7)));
    }

    [Fact]
    public void ParsePrefixes_SavedThreadPage_FindsThePrefixOfTheTitleOnly()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Listings", "Fixtures", "forum-thread.html"));

        Assert.Equal(["Gameplay"], ForumThreadReader.ParsePrefixes(html));
    }

    [Theory]
    [InlineData("<h1 class=\"p-title-value\">No prefix</h1>")]
    [InlineData("<html>no title at all</html>")]
    public void ParsePrefixes_TitleWithoutPrefix_FindsNone(string html) =>
        Assert.Empty(ForumThreadReader.ParsePrefixes(html));

    [Fact]
    public async Task GetPrefixesAsync_ReadsTheThreadPage()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Listings", "Fixtures", "forum-thread.html"));
        var reader = new ForumThreadReader(FakeHttpMessageHandler.BuildClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) }, out _));

        Assert.Equal(["Gameplay"], await reader.GetPrefixesAsync("https://forums.ahwoo.com/threads/advanced-flight-computer.783/"));
    }

    [Theory]
    [InlineData("https://example.com/threads/x.1/", HttpStatusCode.OK)]
    [InlineData("https://forums.ahwoo.com/threads/x.1/", HttpStatusCode.Forbidden)]
    public async Task GetPrefixesAsync_OtherHostOrFailedPage_GivesNoPrefix(string url, HttpStatusCode status)
    {
        var reader = new ForumThreadReader(FakeHttpMessageHandler.BuildClient(_ => new HttpResponseMessage(status) { Content = new StringContent("<h1 class=\"p-title-value\"><span class=\"label\">Gameplay</span>x</h1>") }, out _));

        Assert.Empty(await reader.GetPrefixesAsync(url));
    }

    [Fact]
    public async Task GetPrefixesAsync_HostDoesNotAnswer_GivesNoPrefix()
    {
        var reader = new ForumThreadReader(FakeHttpMessageHandler.BuildClient(new Func<HttpRequestMessage, HttpResponseMessage>(_ => throw new HttpRequestException("offline")), out _));

        Assert.Empty(await reader.GetPrefixesAsync("https://forums.ahwoo.com/threads/x.1/"));
    }

    [Fact]
    public async Task GetListingAsync_ReadsTheListedFile()
    {
        var fetcher = new ListedDocumentFetcher(FakeHttpMessageHandler.BuildClient(request => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.AbsolutePath) }, out _));

        Assert.Equal("/KSAModding/content-index/main/listings/MyMod.toml", await fetcher.GetListingAsync("MyMod"));
    }

    [Fact]
    public async Task GetListingAsync_LargeBodyWithoutLength_Throws()
    {
        var fetcher = new ListedDocumentFetcher(FakeHttpMessageHandler.BuildClient(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('a', (1024 * 1024) + 1)) };
            response.Content.Headers.ContentLength = null;
            return response;
        }, out _));

        await Assert.ThrowsAsync<HttpRequestException>(() => fetcher.GetListingAsync("MyMod"));
    }

    [Fact]
    public async Task MeasureAsync_HostedImage_IsMeasuredFromItsBytes()
    {
        var bytes = Png(512, 512);
        using var measurer = new ListingImageMeasurer(new FakeHttpMessageHandler(_ => Task.FromResult(FakeHttpMessageHandler.ByteResponse(bytes))), TimeSpan.FromSeconds(30));

        var measurement = await measurer.MeasureAsync("https://example.com/icon.png", ListingImageRole.Icon);

        Assert.True(measurement.IsMeasured, measurement.Problem);
        Assert.Equal(512, measurement.Width);
        Assert.Equal(bytes.Length, measurement.Size);
    }

    [Theory]
    [InlineData("http://example.com/icon.png", "is not an https URL")]
    [InlineData("https://example.com/missing.png", "answered HTTP 404")]
    public async Task MeasureAsync_UrlThatGivesNoImage_SaysWhy(string url, string expected)
    {
        using var measurer = new ListingImageMeasurer(new FakeHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))), TimeSpan.FromSeconds(30));

        var measurement = await measurer.MeasureAsync(url, ListingImageRole.Icon);

        Assert.False(measurement.IsMeasured);
        Assert.Contains(expected, measurement.Problem);
    }

    private static byte[] Png(int width, int height)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        List<byte> bytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        foreach (var (kind, body) in new[] { ("IHDR", header), ("IDAT", new byte[] { 0x78, 0x9C, 0x63, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01 }), ("IEND", Array.Empty<byte>()) })
        {
            var length = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(length, (uint)body.Length);
            bytes.AddRange(length);
            bytes.AddRange(Encoding.ASCII.GetBytes(kind));
            bytes.AddRange(body);
            bytes.AddRange(new byte[4]);
        }

        return [.. bytes];
    }
}
