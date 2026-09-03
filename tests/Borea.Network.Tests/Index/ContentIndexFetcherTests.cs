using Borea.Core.Index;
using Borea.Network.Index;
using System.Net;
using System.Net.Http.Headers;

namespace Borea.Network.Tests.Index
{
    public sealed class ContentIndexFetcherTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _destinationPath;
        private readonly string _etagPath;

        // Matches BasicIndexFormatCheckAsync's required shape: object root,
        // integer snapshot_version <= 1, and 'listings'/'packs'/'game_versions'
        // present (values unchecked). 'sources' is deliberately absent — it's
        // optional per the class's own doc comment.
        private const string SampleIndexBody =
            """{ "snapshot_version": 1, "listings": {}, "packs": {}, "game_versions": {} }""";
        private const string UpdatedIndexBody =
            """{ "snapshot_version": 1, "listings": { "example-mod": {} }, "packs": {}, "game_versions": {} }""";

        public ContentIndexFetcherTests()
        {
            _tempDir = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), "borea-fetcher-tests-" + Guid.NewGuid())).FullName;
            _destinationPath = Path.Combine(_tempDir, "content-index.json");
            _etagPath = _destinationPath + ".etag";
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        private static ContentIndexFetcher CreateFetcher(HttpClient client) => new(client, new Uri("https://example.test/content-index.json"));

        private static HttpResponseMessage JsonResponseWithETag(string json, string tag, bool isWeak = false)
        {
            var response = FakeHttpMessageHandler.JsonResponse(json);
            response.Headers.ETag = new EntityTagHeaderValue(tag, isWeak);
            return response;
        }

        private static HttpResponseMessage JsonResponse(string json) => FakeHttpMessageHandler.JsonResponse(json);

        [Fact]
        public async Task FetchAsync_NoExistingCache_DownloadsAndWritesSidecar()
        {
            var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponseWithETag(SampleIndexBody, "\"abc123\""),
                out var handler);

            var result = await CreateFetcher(client).FetchAsync(_destinationPath);

            Assert.Equal(ContentIndexFetchResult.Downloaded, result);
            Assert.Empty(handler.LastRequest!.Headers.IfNoneMatch);
            Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(_destinationPath));
            Assert.Equal("\"abc123\"", (await File.ReadAllTextAsync(_etagPath)).Trim());
            Assert.False(File.Exists(_destinationPath + ".tmp"));
        }

        [Fact]
        public async Task FetchAsync_ExistingCacheAndSidecar_SendsIfNoneMatch()
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, "\"abc123\"");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => new HttpResponseMessage(HttpStatusCode.NotModified),
                out var handler);

            var result = await CreateFetcher(client).FetchAsync(_destinationPath);

            Assert.Equal(ContentIndexFetchResult.NotModified, result);
            Assert.Contains(handler.LastRequest!.Headers.IfNoneMatch, tag => tag.Tag == "\"abc123\"");
        }

        [Fact]
        public async Task FetchAsync_NotModified_LeavesDestinationAndSidecarUntouched()
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, "\"abc123\"");
            var originalDestinationWrite = File.GetLastWriteTimeUtc(_destinationPath);
            var originalEtagWrite = File.GetLastWriteTimeUtc(_etagPath);

            var client = FakeHttpMessageHandler.BuildClient(
                _ => new HttpResponseMessage(HttpStatusCode.NotModified),
                out _);

            await CreateFetcher(client).FetchAsync(_destinationPath);

            Assert.Equal(originalDestinationWrite, File.GetLastWriteTimeUtc(_destinationPath));
            Assert.Equal(originalEtagWrite, File.GetLastWriteTimeUtc(_etagPath));
        }

        [Fact]
        public async Task FetchAsync_DestinationMissingButSidecarPresent_ForcesUnconditionalFetch()
        {
            await File.WriteAllTextAsync(_etagPath, "\"stale-etag\"");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponseWithETag(SampleIndexBody, "\"fresh-etag\""),
                out var handler);

            var result = await CreateFetcher(client).FetchAsync(_destinationPath);

            Assert.Equal(ContentIndexFetchResult.Downloaded, result);
            Assert.Empty(handler.LastRequest!.Headers.IfNoneMatch);
            Assert.Equal("\"fresh-etag\"", (await File.ReadAllTextAsync(_etagPath)).Trim());
        }

        [Fact]
        public async Task FetchAsync_SidecarMissingButDestinationPresent_ForcesUnconditionalFetch()
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);

            var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponseWithETag(UpdatedIndexBody, "\"new-etag\""),
                out var handler);

            var result = await CreateFetcher(client).FetchAsync(_destinationPath);

            Assert.Equal(ContentIndexFetchResult.Downloaded, result);
            Assert.Empty(handler.LastRequest!.Headers.IfNoneMatch);
            Assert.Equal(UpdatedIndexBody, await File.ReadAllTextAsync(_destinationPath));
        }

        [Fact]
        public async Task FetchAsync_ResponseHasNoETag_DeletesExistingSidecar()
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, "\"abc123\"");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponse(UpdatedIndexBody),
                out _);

            var result = await CreateFetcher(client).FetchAsync(_destinationPath);

            Assert.Equal(ContentIndexFetchResult.Downloaded, result);
            Assert.False(File.Exists(_etagPath));
        }

        [Fact]
        public async Task FetchAsync_NonSuccessStatusCode_ThrowsAndLeavesCacheUntouched()
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, "\"abc123\"");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
                out _);

            await Assert.ThrowsAsync<HttpRequestException>(() => CreateFetcher(client).FetchAsync(_destinationPath));

            // No .tmp assertion: EnsureSuccessStatusCode() throws before any file
            // I/O runs, so it would pass regardless of whether cleanup works.
            Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(_destinationPath));
        }

        [Fact]
        public async Task FetchAsync_PlainTextContentType_DownloadsSuccessfully()
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, "\"abc123\"");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(UpdatedIndexBody, System.Text.Encoding.UTF8, "text/plain")
                },
                out _);

            var result = await CreateFetcher(client).FetchAsync(_destinationPath);

            Assert.Equal(ContentIndexFetchResult.Downloaded, result);
            Assert.Equal(UpdatedIndexBody, await File.ReadAllTextAsync(_destinationPath));
        }

        [Fact]
        public async Task FetchAsync_RejectedContentType_ThrowsAndLeavesCacheUntouched()
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, "\"abc123\"");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(UpdatedIndexBody, System.Text.Encoding.UTF8, "text/html")
                },
                out _);

            await Assert.ThrowsAsync<HttpRequestException>(() => CreateFetcher(client).FetchAsync(_destinationPath));

            Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(_destinationPath));
        }

        // This test is technically checking if the index format checker can detect a half downloaded index and throw
        [Fact]
        public async Task FetchAsync_HalfDownloadedFileWithInvalidFormat_ThrowsAndLeavesCacheUntouched()
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, "\"abc123\"");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new FaultyContent(
                        System.Text.Encoding.UTF8.GetBytes("""{ "snapshot_"""),
                        "application/json")
                },
                out _);

            await Assert.ThrowsAsync<HttpRequestException>(() => CreateFetcher(client).FetchAsync(_destinationPath));

            Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(_destinationPath));
            Assert.False(File.Exists(_destinationPath + ".tmp"));
        }

        [Fact]
        public async Task FetchAsync_DestinationDirectoryDoesNotExist_CreatesDirectoryAndDownloads()
        {
            string nestedDestination = Path.Combine(_tempDir, "nested", "sub", "content-index.json");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponseWithETag(SampleIndexBody, "\"abc123\""),
                out _);

            var result = await CreateFetcher(client).FetchAsync(nestedDestination);

            Assert.Equal(ContentIndexFetchResult.Downloaded, result);
            Assert.True(Directory.Exists(Path.Combine(_tempDir, "nested", "sub")));
            Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(nestedDestination));
        }

        [Fact]
        public async Task FetchAsync_InvalidSidecarETag_IgnoresSidecarAndFetchesUnconditionally()
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, "not-a-valid-etag");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponseWithETag(UpdatedIndexBody, "\"new-etag\""),
                out var handler);

            var result = await CreateFetcher(client).FetchAsync(_destinationPath);

            Assert.Equal(ContentIndexFetchResult.Downloaded, result);
            Assert.Empty(handler.LastRequest!.Headers.IfNoneMatch);
            Assert.Equal(UpdatedIndexBody, await File.ReadAllTextAsync(_destinationPath));
            Assert.Equal("\"new-etag\"", (await File.ReadAllTextAsync(_etagPath)).Trim());
        }

        [Fact]
        public async Task FetchAsync_WeakETag_RoundTripsAndIsSentOnNextRequest()
        {
            int callCount = 0;
            var client = FakeHttpMessageHandler.BuildClient(
                _ =>
                {
                    callCount++;
                    return callCount == 1
                        ? JsonResponseWithETag(SampleIndexBody, "\"abc123\"", isWeak: true)
                        : new HttpResponseMessage(HttpStatusCode.NotModified);
                },
                out var handler);

            var fetcher = CreateFetcher(client);

            var firstResult = await fetcher.FetchAsync(_destinationPath);
            Assert.Equal(ContentIndexFetchResult.Downloaded, firstResult);
            Assert.Equal("W/\"abc123\"", (await File.ReadAllTextAsync(_etagPath)).Trim());

            var secondResult = await fetcher.FetchAsync(_destinationPath);
            Assert.Equal(ContentIndexFetchResult.NotModified, secondResult);
            Assert.Contains(handler.LastRequest!.Headers.IfNoneMatch, tag => tag.Tag == "\"abc123\"" && tag.IsWeak);
        }

        [Fact]
        public async Task FetchAsync_ThrowException_WhenNoETagBut304Response()
        {
            var client = FakeHttpMessageHandler.BuildClient(
                _ => new HttpResponseMessage(HttpStatusCode.NotModified),
                out _);

            await Assert.ThrowsAsync<HttpRequestException>(() => CreateFetcher(client).FetchAsync(_destinationPath));
            Assert.False(File.Exists(_destinationPath));
        }

        [Fact]
        public async Task FetchAsync_MissingSnapshotVersion_ThrowsAndLeavesCacheUntouched()
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, "\"abc123\"");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponse("""{ "listings": {}, "packs": {}, "game_versions": {} }"""),
                out _);

            await Assert.ThrowsAsync<HttpRequestException>(() => CreateFetcher(client).FetchAsync(_destinationPath));
            Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(_destinationPath));
        }

        [Fact]
        public async Task FetchAsync_NonIntegerSnapshotVersion_ThrowsAndLeavesCacheUntouched()
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, "\"abc123\"");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponse("""{ "snapshot_version": "one", "listings": {}, "packs": {}, "game_versions": {} }"""),
                out _);

            await Assert.ThrowsAsync<HttpRequestException>(() => CreateFetcher(client).FetchAsync(_destinationPath));
            Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(_destinationPath));
        }

        [Fact]
        public async Task FetchAsync_SnapshotVersionTooHigh_ThrowsAndLeavesCacheUntouched()
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, "\"abc123\"");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponse("""{ "snapshot_version": 2, "listings": {}, "packs": {}, "game_versions": {} }"""),
                out _);

            await Assert.ThrowsAsync<HttpRequestException>(() => CreateFetcher(client).FetchAsync(_destinationPath));
            Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(_destinationPath));
        }

        [Fact]
        public async Task FetchAsync_MissingListings_ThrowsAndLeavesCacheUntouched()
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, "\"abc123\"");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponse("""{ "snapshot_version": 1, "packs": {}, "game_versions": {} }"""),
                out _);

            await Assert.ThrowsAsync<HttpRequestException>(() => CreateFetcher(client).FetchAsync(_destinationPath));
            Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(_destinationPath));
        }

        [Fact]
        public async Task FetchAsync_MissingPacks_ThrowsAndLeavesCacheUntouched()
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, "\"abc123\"");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponse("""{ "snapshot_version": 1, "listings": {}, "game_versions": {} }"""),
                out _);

            await Assert.ThrowsAsync<HttpRequestException>(() => CreateFetcher(client).FetchAsync(_destinationPath));
            Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(_destinationPath));
        }

        [Fact]
        public async Task FetchAsync_MissingGameVersions_ThrowsAndLeavesCacheUntouched()
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, "\"abc123\"");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponse("""{ "snapshot_version": 1, "listings": {}, "packs": {} }"""),
                out _);

            await Assert.ThrowsAsync<HttpRequestException>(() => CreateFetcher(client).FetchAsync(_destinationPath));
            Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(_destinationPath));
        }

        [Fact]
        public async Task FetchAsync_MalformedJson_ThrowsAndLeavesCacheUntouched()
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, "\"abc123\"");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponse("{ not valid json"),
                out _);

            await Assert.ThrowsAsync<HttpRequestException>(() => CreateFetcher(client).FetchAsync(_destinationPath));
            Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(_destinationPath));
        }

        [Fact]
        public async Task FetchAsync_JsonRootIsArray_ThrowsAndLeavesCacheUntouched()
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, "\"abc123\"");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponse("[]"),
                out _);

            await Assert.ThrowsAsync<HttpRequestException>(() => CreateFetcher(client).FetchAsync(_destinationPath));
            Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(_destinationPath));
        }

        [Fact]
        public async Task FetchAsync_SourcesFieldAbsent_StillDownloads()
        {
            // Regression guard for the class's own comment: "Not checking for
            // 'sources' since it is optional." A future accidental re-add of a
            // sources check should fail this test, not a validation test above.
            var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponseWithETag(SampleIndexBody, "\"abc123\""),
                out _);

            var result = await CreateFetcher(client).FetchAsync(_destinationPath);

            Assert.Equal(ContentIndexFetchResult.Downloaded, result);
            Assert.DoesNotContain("sources", await File.ReadAllTextAsync(_destinationPath));
        }

        [Theory]
        [InlineData("""{ "snapshot_version": 1, "listings": {}, "packs": {} }""")] // missing game_versions
        [InlineData("<Html>Please Log In</Html>")]
        [InlineData("{\"error\": \"Could not log you in\"}")]
        [InlineData("Plain Text")]
        [InlineData("{}")]
        [InlineData("")]
        [InlineData("""{ "snapshot_version": 999, "listings": {}, "packs": {}, "game_versions": []}""")]
        public async Task FetchAsync_RejectsInvalidIndexes_ThrowsAndLeavesCacheUntouched(string invalidJson)
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, "\"abc123\"");
            var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponseWithETag(invalidJson, "\"abc123\""),
                out _);
            await Assert.ThrowsAsync<HttpRequestException>(() => CreateFetcher(client).FetchAsync(_destinationPath));
            Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(_destinationPath));
        }
    }
}
