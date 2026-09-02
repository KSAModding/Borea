using Borea.Core.Index;
using Borea.Network.Index;
using System.Net;

namespace Borea.Network.Tests.Index
{
    public sealed class ContentIndexFetcherTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _destinationPath;
        private readonly string _etagPath;

        // Minimal fixture satisfying VerifyIndexBasics: a JSON object with
        // "snapshot_version" and "sources" present. Nothing else is checked.
        private const string ValidIndexJson = """{ "snapshot_version": 1, "sources": {} }""";
        private const string UpdatedIndexJson = """{ "snapshot_version": 2, "sources": {} }""";

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

        private static HttpResponseMessage JsonResponseWithETag(string json, string etag)
        {
            var response = FakeHttpMessageHandler.JsonResponse(json);
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
            return response;
        }

        [Fact]
        public async Task FetchAsync_NoExistingCache_DownloadsAndWritesSidecar()
        {
            var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponseWithETag(ValidIndexJson, "\"abc123\""),
                out var handler);

            var result = await CreateFetcher(client).FetchAsync(_destinationPath);

            Assert.Equal(ContentIndexFetchResult.Downloaded, result);
            Assert.Empty(handler.LastRequest!.Headers.IfNoneMatch);
            Assert.Equal(ValidIndexJson, await File.ReadAllTextAsync(_destinationPath));
            Assert.Equal("\"abc123\"", (await File.ReadAllTextAsync(_etagPath)).Trim());
            Assert.False(File.Exists(_destinationPath + ".tmp"));
        }

        [Fact]
        public async Task FetchAsync_ExistingCacheAndSidecar_SendsIfNoneMatch()
        {
            await File.WriteAllTextAsync(_destinationPath, ValidIndexJson);
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
            await File.WriteAllTextAsync(_destinationPath, ValidIndexJson);
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
            // Simulates someone deleting the cached file but leaving a stale sidecar behind.
            await File.WriteAllTextAsync(_etagPath, "\"stale-etag\"");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponseWithETag(ValidIndexJson, "\"fresh-etag\""),
                out var handler);

            var result = await CreateFetcher(client).FetchAsync(_destinationPath);

            Assert.Equal(ContentIndexFetchResult.Downloaded, result);
            Assert.Empty(handler.LastRequest!.Headers.IfNoneMatch);
            Assert.Equal("\"fresh-etag\"", (await File.ReadAllTextAsync(_etagPath)).Trim());
        }

        [Fact]
        public async Task FetchAsync_SidecarMissingButDestinationPresent_ForcesUnconditionalFetch()
        {
            // Simulates deleting only the sidecar, or upgrading from a Borea version that predates it.
            await File.WriteAllTextAsync(_destinationPath, ValidIndexJson);

            var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponseWithETag(UpdatedIndexJson, "\"new-etag\""),
                out var handler);

            var result = await CreateFetcher(client).FetchAsync(_destinationPath);

            Assert.Equal(ContentIndexFetchResult.Downloaded, result);
            Assert.Empty(handler.LastRequest!.Headers.IfNoneMatch);
            Assert.Equal(UpdatedIndexJson, await File.ReadAllTextAsync(_destinationPath));
        }

        [Fact]
        public async Task FetchAsync_ResponseHasNoETag_DeletesExistingSidecar()
        {
            await File.WriteAllTextAsync(_destinationPath, ValidIndexJson);
            await File.WriteAllTextAsync(_etagPath, "\"abc123\"");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => FakeHttpMessageHandler.JsonResponse(UpdatedIndexJson),
                out _);

            var result = await CreateFetcher(client).FetchAsync(_destinationPath);

            Assert.Equal(ContentIndexFetchResult.Downloaded, result);
            Assert.False(File.Exists(_etagPath));
        }

        [Fact]
        public async Task FetchAsync_NonSuccessStatusCode_ThrowsAndLeavesCacheUntouched()
        {
            await File.WriteAllTextAsync(_destinationPath, ValidIndexJson);
            await File.WriteAllTextAsync(_etagPath, "\"abc123\"");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
                out _);

            await Assert.ThrowsAsync<HttpRequestException>(() => CreateFetcher(client).FetchAsync(_destinationPath));

            Assert.Equal(ValidIndexJson, await File.ReadAllTextAsync(_destinationPath));
            Assert.False(File.Exists(_destinationPath + ".tmp"));
        }

        [Fact]
        public async Task FetchAsync_MissingSnapshotVersion_ThrowsAndLeavesCacheUntouched()
        {
            await File.WriteAllTextAsync(_destinationPath, ValidIndexJson);
            await File.WriteAllTextAsync(_etagPath, "\"abc123\"");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => FakeHttpMessageHandler.JsonResponse("""{ "sources": {} }"""),
                out _);

            await Assert.ThrowsAsync<HttpRequestException>(() => CreateFetcher(client).FetchAsync(_destinationPath));

            Assert.Equal(ValidIndexJson, await File.ReadAllTextAsync(_destinationPath));
            Assert.False(File.Exists(_destinationPath + ".tmp"));
        }

        [Fact]
        public async Task FetchAsync_MissingSources_ThrowsAndLeavesCacheUntouched()
        {
            await File.WriteAllTextAsync(_destinationPath, ValidIndexJson);
            await File.WriteAllTextAsync(_etagPath, "\"abc123\"");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => FakeHttpMessageHandler.JsonResponse("""{ "snapshot_version": 1 }"""),
                out _);

            await Assert.ThrowsAsync<HttpRequestException>(() => CreateFetcher(client).FetchAsync(_destinationPath));

            Assert.Equal(ValidIndexJson, await File.ReadAllTextAsync(_destinationPath));
            Assert.False(File.Exists(_destinationPath + ".tmp"));
        }

        [Fact]
        public async Task FetchAsync_MalformedJson_ThrowsAndLeavesCacheUntouched()
        {
            await File.WriteAllTextAsync(_destinationPath, ValidIndexJson);
            await File.WriteAllTextAsync(_etagPath, "\"abc123\"");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => FakeHttpMessageHandler.JsonResponse("{ not valid json"),
                out _);

            await Assert.ThrowsAsync<HttpRequestException>(() => CreateFetcher(client).FetchAsync(_destinationPath));

            Assert.Equal(ValidIndexJson, await File.ReadAllTextAsync(_destinationPath));
            Assert.False(File.Exists(_destinationPath + ".tmp"));
        }

        [Fact]
        public async Task FetchAsync_JsonRootIsArray_ThrowsAndLeavesCacheUntouched()
        {
            await File.WriteAllTextAsync(_destinationPath, ValidIndexJson);
            await File.WriteAllTextAsync(_etagPath, "\"abc123\"");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => FakeHttpMessageHandler.JsonResponse("[]"),
                out _);

            await Assert.ThrowsAsync<HttpRequestException>(() => CreateFetcher(client).FetchAsync(_destinationPath));

            Assert.Equal(ValidIndexJson, await File.ReadAllTextAsync(_destinationPath));
            Assert.False(File.Exists(_destinationPath + ".tmp"));
        }

        [Fact]
        public async Task FetchAsync_WrongContentType_ThrowsAndLeavesCacheUntouched()
        {
            await File.WriteAllTextAsync(_destinationPath, ValidIndexJson);
            await File.WriteAllTextAsync(_etagPath, "\"abc123\"");

            var client = FakeHttpMessageHandler.BuildClient(
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(ValidIndexJson, System.Text.Encoding.UTF8, "text/plain")
                },
                out _);

            await Assert.ThrowsAsync<HttpRequestException>(() => CreateFetcher(client).FetchAsync(_destinationPath));

            Assert.Equal(ValidIndexJson, await File.ReadAllTextAsync(_destinationPath));
            Assert.False(File.Exists(_destinationPath + ".tmp"));
        }
    }
}
