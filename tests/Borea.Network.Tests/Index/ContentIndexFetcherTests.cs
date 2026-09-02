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

        private const string SampleIndexBody = """{ "snapshot_version": 1, "sources": {} }""";
        private const string UpdatedIndexBody = """{ "snapshot_version": 2, "sources": {} }""";

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
                _ => FakeHttpMessageHandler.JsonResponse(UpdatedIndexBody),
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

            // No .tmp assertion here: EnsureSuccessStatusCode() throws before any
            // file I/O runs, so a .tmp check would pass regardless of whether
            // cleanup logic works. See FetchAsync_DownloadInterruptedMidStream_
            // ThrowsAndLeavesCacheUntouched below for a case that actually exercises it.
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

            // Same reasoning as the 500 test above: the content-type check throws
            // before Directory.CreateDirectory or any write, so a .tmp assertion
            // here wouldn't exercise anything either.
            Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(_destinationPath));
        }

        [Fact]
        public async Task FetchAsync_DownloadInterruptedMidStream_ThrowsAndLeavesCacheUntouched()
        {
            // Unlike the 500/rejected-content-type cases above, this failure happens
            // *during* ReadAsByteArrayAsync — after the content-type check passes,
            // before Directory.CreateDirectory or the write block run. This is the
            // scenario that actually proves no partial file gets left behind.
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
            // Unquoted, no W/ prefix — not a well-formed entity-tag per RFC 7232,
            // so EntityTagHeaderValue.TryParse should reject it.
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
    }
}
