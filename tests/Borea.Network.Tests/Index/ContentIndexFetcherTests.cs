using Borea.Core.Index;
using Borea.Core.Logging;
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
        // present (values unchecked). 'sources' is deliberately absent because it is
        // optional per the class's own doc comment.
        private const string SampleIndexBody =
            """{ "snapshot_version": 1, "listings": [], "packs": [], "game_versions": {} }""";
        private const string UpdatedIndexBody =
            """{ "snapshot_version": 1, "listings": [{"example-mod": {}}], "packs": [], "game_versions": {} }""";

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

        private static ContentIndexFetcher CreateFetcher(
            HttpClient client,
            IContentIndexCandidateValidator? candidateValidator = null) =>
            new(
                client,
                new Uri("https://example.test/content-index.json"),
                candidateValidator ?? new CandidateValidator(_ => Task.CompletedTask));

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
        public async Task FetchAsync_ReaderRejectsRootValidCandidate_KeepsKnownGoodCache()
        {
            const string readerInvalidBody =
                """{ "snapshot_version": 1, "sources": { "authored": "invalid" }, "listings": [], "packs": [], "game_versions": {} }""";
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, "\"known-good\"");
            var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponseWithETag(readerInvalidBody, "\"rejected\""),
                out _);
            var validator = new CandidateValidator(async candidatePath =>
            {
                Assert.Equal(readerInvalidBody, await File.ReadAllTextAsync(candidatePath));
                Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(_destinationPath));
                throw new InvalidOperationException("The candidate does not match the typed snapshot shape.");
            });

            await Assert.ThrowsAsync<HttpRequestException>(() =>
                CreateFetcher(client, validator).FetchAsync(_destinationPath));

            Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(_destinationPath));
            Assert.Equal("\"known-good\"", await File.ReadAllTextAsync(_etagPath));
            Assert.False(File.Exists(_destinationPath + ".tmp"));
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
        [InlineData("""{ "snapshot_version": -1, "listings": {}, "packs": {}, "game_versions": []}""")]
        [InlineData("""{ "snapshot_version": 3000000000, "listings": {}, "packs": {}, "game_versions": []}""")]
        [InlineData("""{ "snapshot_version": 4294967295, "listings": {}, "packs": {}, "game_versions": []}""")]
        [InlineData("""{ "snapshot_version": 1.5, "listings": {}, "packs": {}, "game_versions": []}""")]
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

        private const string VehiclesBody = """{ "snapshot_version": 1, "vehicles": [] }""";
        private const string NewVehiclesBody = """{ "snapshot_version": 1, "vehicles": [{ "id": "ExampleRover" }] }""";

        private string VehiclesPath => Path.Combine(_tempDir, "snapshot-vehicles.json");

        private static ContentIndexFetcher CreateFetcherWithVehicles(HttpClient client, IBoreaLog? log = null) =>
            new(
                client,
                new Uri("https://example.test/v1/content-index.json"),
                new CandidateValidator(_ => Task.CompletedTask),
                [ContentIndexTypeFiles.Vehicles],
                log);

        private static bool IsVehicles(HttpRequestMessage request) =>
            request.RequestUri!.AbsoluteUri == "https://example.test/v1/snapshot-vehicles.json";

        private async Task WriteCachedIndexAsync()
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, "\"index-1\"");
        }

        private async Task WriteCachedVehiclesAsync()
        {
            await File.WriteAllTextAsync(VehiclesPath, VehiclesBody);
            await File.WriteAllTextAsync(VehiclesPath + ".etag", "\"vehicles-1\"");
        }

        [Fact]
        public async Task FetchAsync_TypeFile_IsFetchedAgainstItsOwnETag()
        {
            await WriteCachedIndexAsync();
            await WriteCachedVehiclesAsync();
            var requests = new List<HttpRequestMessage>();
            var client = FakeHttpMessageHandler.BuildClient(
                request =>
                {
                    requests.Add(request);
                    return new HttpResponseMessage(HttpStatusCode.NotModified);
                },
                out _);

            var result = await CreateFetcherWithVehicles(client).FetchAsync(_destinationPath);

            Assert.Equal(ContentIndexFetchResult.NotModified, result);
            Assert.Equal(2, requests.Count);
            Assert.Contains(requests[0].Headers.IfNoneMatch, tag => tag.Tag == "\"index-1\"");
            Assert.True(IsVehicles(requests[1]));
            Assert.Contains(requests[1].Headers.IfNoneMatch, tag => tag.Tag == "\"vehicles-1\"");
        }

        [Fact]
        public async Task FetchAsync_TypeFileNotFoundWithoutACopy_WritesNothing()
        {
            var log = new ListLog();
            var client = FakeHttpMessageHandler.BuildClient(
                request => IsVehicles(request) ? new HttpResponseMessage(HttpStatusCode.NotFound) : JsonResponseWithETag(SampleIndexBody, "\"index-1\""),
                out _);

            var result = await CreateFetcherWithVehicles(client, log).FetchAsync(_destinationPath);

            Assert.Equal(ContentIndexFetchResult.Downloaded, result);
            Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(_destinationPath));
            Assert.False(File.Exists(VehiclesPath));
            Assert.False(File.Exists(VehiclesPath + ".etag"));
            Assert.Empty(log.Lines);
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound, false)]
        [InlineData(HttpStatusCode.InternalServerError, true)]
        public async Task FetchAsync_TypeFileFailsWithACopy_KeepsTheCopyAndTheIndexResult(HttpStatusCode status, bool logged)
        {
            await WriteCachedIndexAsync();
            await WriteCachedVehiclesAsync();
            var log = new ListLog();
            var client = FakeHttpMessageHandler.BuildClient(
                request => new HttpResponseMessage(IsVehicles(request) ? status : HttpStatusCode.NotModified),
                out _);

            var result = await CreateFetcherWithVehicles(client, log).FetchAsync(_destinationPath);

            Assert.Equal(ContentIndexFetchResult.NotModified, result);
            Assert.Equal(VehiclesBody, await File.ReadAllTextAsync(VehiclesPath));
            Assert.Equal("\"vehicles-1\"", await File.ReadAllTextAsync(VehiclesPath + ".etag"));
            Assert.Equal(logged, log.Lines.Any(line => line.Contains("snapshot-vehicles.json", StringComparison.Ordinal)));
        }

        [Theory]
        [InlineData("""{ "snapshot_version": 1, "vehicles": {} }""")]
        [InlineData("""{ "snapshot_version": 2, "vehicles": [] }""")]
        [InlineData("not json")]
        public async Task FetchAsync_UnusableTypeFile_IsRefusedAndTheCopyKept(string body)
        {
            await WriteCachedIndexAsync();
            await WriteCachedVehiclesAsync();
            var log = new ListLog();
            var client = FakeHttpMessageHandler.BuildClient(
                request => IsVehicles(request) ? JsonResponseWithETag(body, "\"vehicles-2\"") : new HttpResponseMessage(HttpStatusCode.NotModified),
                out _);

            var result = await CreateFetcherWithVehicles(client, log).FetchAsync(_destinationPath);

            Assert.Equal(ContentIndexFetchResult.NotModified, result);
            Assert.Equal(VehiclesBody, await File.ReadAllTextAsync(VehiclesPath));
            Assert.Equal("\"vehicles-1\"", await File.ReadAllTextAsync(VehiclesPath + ".etag"));
            Assert.False(File.Exists(VehiclesPath + ".tmp"));
            Assert.Contains(log.Lines, line => line.Contains("snapshot-vehicles.json", StringComparison.Ordinal));
        }

        [Fact]
        public async Task FetchAsync_OnlyTheTypeFileChanged_IsDownloaded()
        {
            await WriteCachedIndexAsync();
            await WriteCachedVehiclesAsync();
            var client = FakeHttpMessageHandler.BuildClient(
                request => IsVehicles(request) ? JsonResponseWithETag(NewVehiclesBody, "\"vehicles-2\"") : new HttpResponseMessage(HttpStatusCode.NotModified),
                out _);

            var result = await CreateFetcherWithVehicles(client).FetchAsync(_destinationPath);

            Assert.Equal(ContentIndexFetchResult.Downloaded, result);
            Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(_destinationPath));
            Assert.Equal(NewVehiclesBody, await File.ReadAllTextAsync(VehiclesPath));
            Assert.Equal("\"vehicles-2\"", await File.ReadAllTextAsync(VehiclesPath + ".etag"));
        }

        [Fact]
        public async Task FetchAsync_IndexFails_FetchesNoTypeFile()
        {
            var requests = 0;
            var client = FakeHttpMessageHandler.BuildClient(
                _ =>
                {
                    requests++;
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                },
                out _);

            await Assert.ThrowsAsync<HttpRequestException>(() => CreateFetcherWithVehicles(client).FetchAsync(_destinationPath));

            Assert.Equal(1, requests);
        }

        [Fact]
        public async Task FetchAsync_CanceledWhileATypeFileIsFetched_Throws()
        {
            await WriteCachedIndexAsync();
            using var cancellation = new CancellationTokenSource();
            var client = FakeHttpMessageHandler.BuildClient(
                (request, _) =>
                {
                    if (!IsVehicles(request))
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));

                    cancellation.Cancel();
                    return Task.FromCanceled<HttpResponseMessage>(cancellation.Token);
                },
                out _);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateFetcherWithVehicles(client).FetchAsync(_destinationPath, cancellation.Token));
        }

        private sealed class ListLog : IBoreaLog
        {
            private readonly List<string> _lines = [];

            public IReadOnlyList<string> Lines => _lines;

            public string CurrentFilePath => "borea.log";

            public void Write(string message) => _lines.Add(message);

            public void Write(string message, Exception exception) => _lines.Add(message);

            public IReadOnlyList<string> ReadRecentLines(int maxLines) => _lines;
        }

        private sealed class CandidateValidator : IContentIndexCandidateValidator
        {
            private readonly Func<string, Task> _validate;

            public CandidateValidator(Func<string, Task> validate)
            {
                _validate = validate;
            }

            public Task ValidateAsync(string candidatePath, CancellationToken cancellationToken = default) =>
                _validate(candidatePath);
        }
    }
}
