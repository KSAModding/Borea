using System.Buffers.Binary;
using System.Net;
using System.Text;
using Borea.Core.Listings;
using Borea.Network.GitHub;
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

    private const string ModReleasesThread = "https://forums.ahwoo.com/forums/kitten-space-agency/mod-releases/my-mod.1/";

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

    [Theory]
    [InlineData("0.5", "0.5.0")]
    [InlineData("v1-rc.1", "1.0.0-rc.1")]
    public async Task ReadAsync_ShortTag_IsTheReleaseWithItsVersionFilled(string tag, string version)
    {
        var releases = $$"""
            [ { "tag_name": "{{tag}}", "draft": false, "published_at": "2026-09-08T00:00:00Z", "assets": [] },
              { "tag_name": "0.4.0.1", "draft": false, "published_at": "2026-09-09T00:00:00Z", "assets": [] } ]
            """;
        var client = new ListingHostClient(FakeHttpMessageHandler.BuildClient(request =>
            FakeHttpMessageHandler.JsonResponse(request.RequestUri!.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal) ? releases : Repository), out _));

        var latest = (await client.ReadAsync(new ListingSourceReference.GitHub("Owner", "KSA-MyMod"))).Latest!;

        Assert.Equal(tag, latest.Tag);
        Assert.Equal(version, latest.Version);
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

        Assert.Equal([new ForumPrefix("Gameplay")], ForumThreadReader.ParsePrefixes(html));
    }

    [Fact]
    public void ParsePrefixes_SavedThreadPageWithSimilarThreads_TakesTheIdTheyGiveThePrefix()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Listings", "Fixtures", "forum-thread-similar-threads.html"));

        Assert.Equal([new ForumPrefix("User Interface", 8)], ForumThreadReader.ParsePrefixes(html));
    }

    [Theory]
    [InlineData("https://forums.ahwoo.com/forums/kitten-space-agency/mod-releases/my-mod.1/")]
    [InlineData("https://forums.ahwoo.com/forums/kitten-space-agency/mod-releases/my-mod.1/page-2")]
    public void ParsePrefixes_ListedThreadsOfAnotherForum_TakesTheIdOfTheOwnForumOnly(string canonical)
    {
        var html = ThreadPage(canonical,
            ListedThread("is-prefix15", "User Interface", "/forums/kitten-space-agency/bug-reports/other.3/"),
            ListedThread("is-prefix8", "User Interface", "/forums/kitten-space-agency/mod-releases/other.2/"));

        Assert.Equal([new ForumPrefix("User Interface", 8)], ForumThreadReader.ParsePrefixes(html));
    }

    public static TheoryData<string, string> ListedThreadsWithoutOneId => new()
    {
        { ModReleasesThread, ListedThread("is-prefix8", "Tools") },
        { ModReleasesThread, ListedThread("is-prefix8", "User Interface</span><span class=\"label\">Tools") },
        { ModReleasesThread, ListedThread("", "User Interface") },
        { ModReleasesThread, ListedThread("is-prefix12", "User Interface") + ListedThread("is-prefix8", "User Interface") },
        { ModReleasesThread, ListedThread("is-prefix15", "User Interface", "/forums/kitten-space-agency/bug-reports/other.3/") },
        { ModReleasesThread, ListedThread("is-prefix8", "User Interface", "/forums/kitten-space-agency/mod-releases/") },
        { "", ListedThread("is-prefix8", "User Interface") },
    };

    [Theory]
    [MemberData(nameof(ListedThreadsWithoutOneId))]
    public void ParsePrefixes_NoListedThreadOfTheForumGivesTheTextOneId_GivesNoId(string canonical, string listedThreads)
    {
        Assert.Equal([new ForumPrefix("User Interface")], ForumThreadReader.ParsePrefixes(ThreadPage(canonical, listedThreads)));
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

        Assert.Equal([new ForumPrefix("Gameplay")], await reader.GetPrefixesAsync("https://forums.ahwoo.com/threads/advanced-flight-computer.783/"));
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
    public async Task GetPackVersionAsync_ReadsTheFileOfThatVersion()
    {
        var fetcher = new ListedDocumentFetcher(FakeHttpMessageHandler.BuildClient(request => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.AbsolutePath) }, out _));

        Assert.Equal("/KSAModding/content-index/main/packs/my-pack/1.0.1.toml", await fetcher.GetPackVersionAsync("my-pack", "1.0.1"));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task HasPackVersionAsync_ReadsTheRawFile(HttpStatusCode status, bool exists)
    {
        Uri? asked = null;
        var fetcher = new ListedDocumentFetcher(FakeHttpMessageHandler.BuildClient(request =>
        {
            asked = request.RequestUri;
            return new HttpResponseMessage(status);
        }, out _));

        Assert.Equal(exists, await fetcher.HasPackVersionAsync("my-pack", "1.0.2"));
        Assert.Equal("https://raw.githubusercontent.com/KSAModding/content-index/main/packs/my-pack/1.0.2.toml", asked!.AbsoluteUri);
    }

    [Fact]
    public async Task HasPackVersionAsync_ServerError_Throws()
    {
        var fetcher = new ListedDocumentFetcher(FakeHttpMessageHandler.BuildClient(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable), out _));

        await Assert.ThrowsAsync<HttpRequestException>(() => fetcher.HasPackVersionAsync("my-pack", "1.0.2"));
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
        using var measurer = Measurer(_ => FakeHttpMessageHandler.ByteResponse(bytes), out _);

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
        using var measurer = Measurer(_ => new HttpResponseMessage(HttpStatusCode.NotFound), out _);

        var measurement = await measurer.MeasureAsync(url, ListingImageRole.Icon);

        Assert.False(measurement.IsMeasured);
        Assert.False(measurement.IsUnavailable);
        Assert.Contains(expected, measurement.Problem);
    }

    [Fact]
    public async Task MeasureAsync_HostThatFails_IsUnavailable()
    {
        using var measurer = Measurer(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable), out _);

        var measurement = await measurer.MeasureAsync("https://example.com/icon.png", ListingImageRole.Icon);

        Assert.True(measurement.IsUnavailable);
        Assert.Contains("answered HTTP 503", measurement.Problem);
    }

    [Theory]
    [InlineData("https://raw.githubusercontent.com/alice/KSA-Mod/refs/heads/main/images/hero.png")]
    [InlineData("https://raw.githubusercontent.com/alice/KSA-Mod/main/images/hero.png")]
    public async Task CheckAsync_BranchAddress_ReadsOnlyTheFilesAtTheCommitAndAsksForTheBranchOnce(string url)
    {
        var bytes = Png(1280, 720);
        var otherUrl = url.Replace("hero.png", "other.png", StringComparison.Ordinal);
        var otherCommitImage = CommitImage.Replace("hero.png", "other.png", StringComparison.Ordinal);
        using var measurer = Measurer(request => request.RequestUri!.AbsoluteUri switch
        {
            BranchApi => FakeHttpMessageHandler.JsonResponse($$$"""{"name":"main","commit":{"sha":"{{{Commit}}}"}}"""),
            CommitImage => FakeHttpMessageHandler.ByteResponse(bytes),
            _ when request.RequestUri!.AbsoluteUri == otherCommitImage => FakeHttpMessageHandler.ByteResponse(bytes),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        }, out var sent);

        var hero = await measurer.CheckAsync(url, ListingImageRole.Description);
        var other = await measurer.CheckAsync(otherUrl, ListingImageRole.Description);

        Assert.Equal(ListingImageMeasurement.Of(bytes, ListingImageRole.Description), hero.Measurement);
        Assert.Equal(new ListingImageBranch("main", CommitImage), hero.Branch);
        Assert.Equal(new ListingImageBranch("main", otherCommitImage), other.Branch);
        Assert.Equal([BranchApi, CommitImage, otherCommitImage], sent.Select(request => request.Url));
        Assert.All(sent, request => Assert.Null(request.Authorization));
    }

    [Theory]
    [InlineData("https://raw.githubusercontent.com/alice/KSA-Mod/main/images/hero.png", false)]
    [InlineData("https://raw.githubusercontent.com/alice/KSA-Mod/refs/heads/main/hero.png", true)]
    public async Task CheckAsync_BranchThatGitHubDoesNotConfirm_ReadsTheAddressItself(string url, bool isSure)
    {
        var bytes = Png(1280, 720);
        using var measurer = Measurer(request => request.RequestUri!.AbsoluteUri == url
            ? FakeHttpMessageHandler.ByteResponse(bytes)
            : new HttpResponseMessage(HttpStatusCode.NotFound), out var sent);

        var check = await measurer.CheckAsync(url, ListingImageRole.Description);

        Assert.True(check.Measurement.IsMeasured);
        Assert.Equal(isSure ? new ListingImageBranch("main", null) : null, check.Branch);
        Assert.Equal([BranchApi, url], sent.Select(request => request.Url));
    }

    [Fact]
    public async Task CheckAsync_AddressOnNoBranch_ReadsOnlyTheAddress()
    {
        const string Url = "https://example.com/images/hero.png";
        using var measurer = Measurer(_ => new HttpResponseMessage(HttpStatusCode.NotFound), out var sent);

        var check = await measurer.CheckAsync(Url, ListingImageRole.Description);

        Assert.Null(check.Branch);
        Assert.False(check.Measurement.IsUnavailable);
        Assert.Equal([Url], sent.Select(request => request.Url));
    }

    [Fact]
    public async Task FindCommitAddressAsync_BranchMovedWithinTheMinuteOfACheck_FindsTheNewCommit()
    {
        const string NewCommit = "fedcba9876543210fedcba9876543210fedcba98";
        var newCommitImage = CommitImage.Replace(Commit, NewCommit, StringComparison.Ordinal);
        var oldBytes = Png(1920, 1080);
        var newBytes = Png(1280, 720);
        var pushed = false;
        using var measurer = Measurer(request => request.RequestUri!.AbsoluteUri switch
        {
            BranchApi => FakeHttpMessageHandler.JsonResponse($$$"""{"name":"main","commit":{"sha":"{{{(pushed ? NewCommit : Commit)}}}"}}"""),
            CommitImage => FakeHttpMessageHandler.ByteResponse(oldBytes),
            _ when request.RequestUri!.AbsoluteUri == newCommitImage => FakeHttpMessageHandler.ByteResponse(newBytes),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        }, out var sent, new FakeTimeProvider());

        await measurer.CheckAsync(BranchImage, ListingImageRole.Description);
        pushed = true;
        var branch = await measurer.FindCommitAddressAsync(BranchImage, ListingImageRole.Description, ListingImageMeasurement.Of(newBytes, ListingImageRole.Description).Sha256!);

        Assert.Equal(new ListingImageBranch("main", newCommitImage), branch);
        Assert.Equal([BranchApi, CommitImage, BranchApi, newCommitImage], sent.Select(request => request.Url));
    }

    [Theory]
    [InlineData("https://raw.githubusercontent.com/alice/KSA-Mod/refs/heads/main/images/hero.png")]
    [InlineData("https://raw.githubusercontent.com/alice/KSA-Mod/main/images/hero.png")]
    public async Task FindCommitAddressAsync_BranchAddress_GivesTheCommitAddressOfTheSameBytes(string url)
    {
        var bytes = Png(1280, 720);
        var sha256 = ListingImageMeasurement.Of(bytes, ListingImageRole.Description).Sha256!;
        using var measurer = Measurer(request => request.RequestUri!.AbsoluteUri switch
        {
            BranchApi => FakeHttpMessageHandler.JsonResponse($$$"""{"name":"main","commit":{"sha":"{{{Commit}}}"}}"""),
            CommitImage => FakeHttpMessageHandler.ByteResponse(bytes),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        }, out var sent);

        var first = await measurer.FindCommitAddressAsync(url, ListingImageRole.Description, sha256);
        var second = await measurer.FindCommitAddressAsync(url, ListingImageRole.Description, sha256.ToUpperInvariant());

        Assert.Equal(new ListingImageBranch("main", CommitImage), first);
        Assert.Equal(first, second);
        Assert.Equal([BranchApi, CommitImage, BranchApi, CommitImage], sent.Select(request => request.Url));
        Assert.All(sent, request => Assert.Null(request.Authorization));
    }

    [Fact]
    public async Task FindCommitAddressAsync_OtherBytesAtTheCommit_NamesTheBranchWithoutAnAddress()
    {
        using var measurer = Measurer(request => request.RequestUri!.AbsoluteUri == BranchApi
            ? FakeHttpMessageHandler.JsonResponse($$$"""{"name":"main","commit":{"sha":"{{{Commit}}}"}}""")
            : FakeHttpMessageHandler.ByteResponse(Png(1920, 1080)), out _);

        var branch = await measurer.FindCommitAddressAsync(BranchImage, ListingImageRole.Description, ListingImageMeasurement.Of(Png(1280, 720), ListingImageRole.Description).Sha256!);

        Assert.Equal(new ListingImageBranch("main", null), branch);
    }

    [Theory]
    [InlineData("https://raw.githubusercontent.com/alice/KSA-Mod/v1.0/images/hero.png", false, null, new[] { "v1.0" })]
    [InlineData("https://raw.githubusercontent.com/alice/KSA-Mod/refs/heads/v1.0/hero.png", true, "v1.0", new[] { "v1.0" })]
    [InlineData("https://raw.githubusercontent.com/alice/KSA-Mod/refs/heads/v1.0/images/hero.png", true, null, new[] { "v1.0", "v1.0/images" })]
    [InlineData("https://raw.githubusercontent.com/alice/KSA-Mod/refs/heads/a/b/c/d/e/hero.png", true, null, new[] { "a", "a/b", "a/b/c" })]
    public async Task FindCommitAddressAsync_NoSuchBranch_NamesOnlyACertainBranch(string url, bool isBranch, string? branch, string[] asked)
    {
        using var measurer = Measurer(_ => new HttpResponseMessage(HttpStatusCode.NotFound), out var sent);

        var found = await measurer.FindCommitAddressAsync(url, ListingImageRole.Description, new string('a', 64));

        Assert.Equal(isBranch ? new ListingImageBranch(branch, null) : null, found);
        Assert.Equal(asked.Select(name => $"https://api.github.com/repos/alice/KSA-Mod/branches/{name}"), sent.Select(request => request.Url));
    }

    [Fact]
    public async Task FindCommitAddressAsync_BranchNameWithASlash_IsFoundByTheLongerName()
    {
        const string Url = "https://raw.githubusercontent.com/alice/KSA-Mod/refs/heads/release/1.0/images/hero.png";
        const string ReleaseApi = "https://api.github.com/repos/alice/KSA-Mod/branches/release/1.0";
        var bytes = Png(1280, 720);
        using var measurer = Measurer(request => request.RequestUri!.AbsoluteUri switch
        {
            ReleaseApi => FakeHttpMessageHandler.JsonResponse($$$"""{"name":"release/1.0","commit":{"sha":"{{{Commit}}}"}}"""),
            CommitImage => FakeHttpMessageHandler.ByteResponse(bytes),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        }, out var sent);

        var found = await measurer.FindCommitAddressAsync(Url, ListingImageRole.Description, ListingImageMeasurement.Of(bytes, ListingImageRole.Description).Sha256!);

        Assert.Equal(new ListingImageBranch("release/1.0", CommitImage), found);
        Assert.Equal(["https://api.github.com/repos/alice/KSA-Mod/branches/release", ReleaseApi, CommitImage], sent.Select(request => request.Url));
    }

    [Fact]
    public async Task FindCommitAddressAsync_RateLimited_AsksGitHubAgainOnlyAfterTheReset()
    {
        var time = new FakeTimeProvider();
        var reset = time.UtcNow.AddMinutes(10);
        using var measurer = Measurer(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("""{"message":"API rate limit exceeded."}""") };
            response.Headers.Add("x-ratelimit-remaining", "0");
            response.Headers.Add("x-ratelimit-reset", reset.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
            return response;
        }, out var sent, time);

        var first = await measurer.FindCommitAddressAsync(BranchImage, ListingImageRole.Description, new string('a', 64));
        time.UtcNow = time.UtcNow.AddMinutes(5);
        var second = await measurer.FindCommitAddressAsync(BranchImage.Replace("/main/", "/dev/", StringComparison.Ordinal), ListingImageRole.Description, new string('a', 64));
        time.UtcNow = reset.AddSeconds(1);
        await measurer.FindCommitAddressAsync(BranchImage, ListingImageRole.Description, new string('a', 64));

        Assert.Equal(new ListingImageBranch(null, null), first);
        Assert.Equal(new ListingImageBranch(null, null), second);
        Assert.Equal([BranchApi, BranchApi], sent.Select(request => request.Url));
    }

    [Theory]
    [InlineData("https://example.com/images/hero.png")]
    [InlineData("https://raw.githubusercontent.com/alice/KSA-Mod/0123456789abcdef0123456789abcdef01234567/images/hero.png")]
    [InlineData("https://raw.githubusercontent.com/alice/KSA-Mod/refs/tags/v1.0/images/hero.png")]
    public async Task FindCommitAddressAsync_AddressOnNoBranch_AsksNothing(string url)
    {
        using var measurer = Measurer(_ => new HttpResponseMessage(HttpStatusCode.NotFound), out var sent);

        Assert.Null(await measurer.FindCommitAddressAsync(url, ListingImageRole.Description, new string('a', 64)));
        Assert.Empty(sent);
    }

    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    private const string BranchImage = "https://raw.githubusercontent.com/alice/KSA-Mod/refs/heads/main/images/hero.png";

    private const string BranchApi = "https://api.github.com/repos/alice/KSA-Mod/branches/main";

    private const string CommitImage = $"https://raw.githubusercontent.com/alice/KSA-Mod/{Commit}/images/hero.png";

    /// <summary>A measurer whose image fetches and GitHub requests both go to <paramref name="respond"/>, which records them.</summary>
    private static ListingImageMeasurer Measurer(Func<HttpRequestMessage, HttpResponseMessage> respond, out List<(string Url, string? Authorization)> sent, TimeProvider? time = null)
    {
        var requests = new List<(string Url, string? Authorization)>();
        sent = requests;
        HttpResponseMessage Record(HttpRequestMessage request)
        {
            lock (requests)
                requests.Add((request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString()));
            return respond(request);
        }

        var api = FakeHttpMessageHandler.BuildClient(Record, out _);
        return new ListingImageMeasurer(new FakeHttpMessageHandler(request => Task.FromResult(Record(request))), TimeSpan.FromSeconds(30), new GitHubSession(api, "client", "slug"), api, time);
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

    private static string ThreadPage(string canonical, params string[] listedThreads) =>
        (canonical.Length > 0 ? $"<link rel=\"canonical\" href=\"{canonical}\" />" : string.Empty)
        + "<h1 class=\"p-title-value\"><span class=\"label\">User Interface</span>My Mod</h1>"
        + string.Concat(listedThreads);

    private static string ListedThread(string prefixClass, string labels, string link = "/forums/kitten-space-agency/mod-releases/other.2/") =>
        $"<div class=\"structItem structItem--thread {prefixClass}\"><div class=\"structItem-title\"><span class=\"label\">{labels}</span><a href=\"{link}\">Other</a></div></div>";
}
