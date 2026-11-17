using Borea.Core.Index;
using Borea.Core.Logging;
using Borea.Network.Index;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

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

        private static string Sidecar(string body, string tag) =>
            tag + "\n" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

        [Fact]
        public async Task FetchAsync_BodyStalls_ThrowsNetworkFailureAndKeepsTheCache()
        {
            await WriteCachedIndexAsync();
            using var body = new StalledBodyStream();
            using var client = FakeHttpMessageHandler.BuildClient(
                _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) },
                out _);
            client.Timeout = Timeout.InfiniteTimeSpan;
            var fetcher = new ContentIndexFetcher(
                client,
                new Uri("https://example.test/content-index.json"),
                new CandidateValidator(_ => Task.CompletedTask))
            { FetchTimeout = TimeSpan.FromMilliseconds(100) };

            var pending = fetcher.FetchAsync(_destinationPath);
            await body.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
            var exception = await Assert.ThrowsAsync<HttpRequestException>(() => pending.WaitAsync(TimeSpan.FromSeconds(1)));

            Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
            Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(_destinationPath));
            Assert.Equal("\"index-1\"", await File.ReadAllTextAsync(_etagPath));
            Assert.False(File.Exists(_destinationPath + ".tmp"));
        }

        [Fact]
        public async Task FetchAsync_CallerCancelsStalledBody_ThrowsCancellation()
        {
            using var body = new StalledBodyStream();
            using var client = FakeHttpMessageHandler.BuildClient(
                _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) },
                out _);
            using var cancellation = new CancellationTokenSource();
            var pending = CreateFetcher(client).FetchAsync(_destinationPath, cancellation.Token);
            await body.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(1)));
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
            Assert.Equal(Sidecar(SampleIndexBody, "\"abc123\""), await File.ReadAllTextAsync(_etagPath));
            Assert.False(File.Exists(_destinationPath + ".tmp"));
        }

        [Fact]
        public async Task FetchAsync_ExistingCacheAndSidecar_SendsIfNoneMatch()
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, Sidecar(SampleIndexBody, "\"abc123\""));

            var client = FakeHttpMessageHandler.BuildClient(
                _ => new HttpResponseMessage(HttpStatusCode.NotModified),
                out var handler);

            var result = await CreateFetcher(client).FetchAsync(_destinationPath);

            Assert.Equal(ContentIndexFetchResult.NotModified, result);
            Assert.Contains(handler.LastRequest!.Headers.IfNoneMatch, tag => tag.Tag == "\"abc123\"");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task FetchAsync_UsesUniqueTemporaryFileAndCleansItUp(bool rejectCandidate)
        {
            string fixedTempPath = _destinationPath + ".tmp";
            await File.WriteAllTextAsync(fixedTempPath, "Another fetch owns this file.");
            var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponseWithETag(SampleIndexBody, "\"abc123\""),
                out _);
            string? candidate = null;
            var validator = new CandidateValidator(candidatePath =>
            {
                candidate = candidatePath;
                Assert.StartsWith(_destinationPath + ".", candidatePath);
                Assert.EndsWith(".tmp", candidatePath);
                Assert.NotEqual(fixedTempPath, candidatePath);
                if (rejectCandidate)
                    throw new InvalidOperationException("The candidate is not valid.");
                return Task.CompletedTask;
            });

            var fetch = CreateFetcher(client, validator).FetchAsync(_destinationPath);
            if (rejectCandidate)
                await Assert.ThrowsAsync<HttpRequestException>(() => fetch);
            else
                Assert.Equal(ContentIndexFetchResult.Downloaded, await fetch);

            Assert.NotNull(candidate);
            Assert.False(File.Exists(candidate));
            Assert.Equal(new[] { fixedTempPath }, Directory.GetFiles(_tempDir, "*.tmp"));
            Assert.Equal("Another fetch owns this file.", await File.ReadAllTextAsync(fixedTempPath));
        }

        [Fact]
        public async Task FetchAsync_NotModified_LeavesDestinationAndSidecarUntouched()
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, Sidecar(SampleIndexBody, "\"abc123\""));
            var originalDestinationWrite = File.GetLastWriteTimeUtc(_destinationPath);
            var originalEtagWrite = File.GetLastWriteTimeUtc(_etagPath);

            var client = FakeHttpMessageHandler.BuildClient(
                _ => new HttpResponseMessage(HttpStatusCode.NotModified),
                out _);

            await CreateFetcher(client).FetchAsync(_destinationPath);

            Assert.Equal(originalDestinationWrite, File.GetLastWriteTimeUtc(_destinationPath));
            Assert.Equal(originalEtagWrite, File.GetLastWriteTimeUtc(_etagPath));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task FetchAsync_CacheChangedDuringConditionalRequest_RetriesUnconditionally(bool removeBody)
        {
            await WriteCachedIndexAsync();
            int requests = 0;
            var client = FakeHttpMessageHandler.BuildClient(
                async (request, _) =>
                {
                    requests++;
                    if (requests == 1)
                    {
                        Assert.Contains(request.Headers.IfNoneMatch, tag => tag.Tag == "\"index-1\"");
                        if (removeBody)
                            File.Delete(_destinationPath);
                        else
                            await File.WriteAllTextAsync(_destinationPath, UpdatedIndexBody);
                        return new HttpResponseMessage(HttpStatusCode.NotModified);
                    }

                    Assert.Equal(2, requests);
                    Assert.Empty(request.Headers.IfNoneMatch);
                    return JsonResponseWithETag(UpdatedIndexBody, "\"index-2\"");
                },
                out _);

            Assert.Equal(ContentIndexFetchResult.Downloaded, await CreateFetcher(client).FetchAsync(_destinationPath));

            Assert.Equal(2, requests);
            Assert.Equal(UpdatedIndexBody, await File.ReadAllTextAsync(_destinationPath));
            Assert.Equal(Sidecar(UpdatedIndexBody, "\"index-2\""), await File.ReadAllTextAsync(_etagPath));
            Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
        }

        [Fact]
        public async Task FetchAsync_ConcurrentWriters_KeepSeparateCandidatesAndUseOnlyAMatchingETag()
        {
            var candidateReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resumeCandidate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var firstClient = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponseWithETag(SampleIndexBody, "\"index-1\""), out _);
            using var secondClient = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponseWithETag(UpdatedIndexBody, "\"index-2\""), out _);
            string? firstCandidate = null;
            var firstValidator = new CandidateValidator(async candidate =>
            {
                firstCandidate = candidate;
                candidateReady.SetResult();
                await resumeCandidate.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(candidate));
            });
            var secondValidator = new CandidateValidator(async candidate =>
            {
                Assert.NotEqual(firstCandidate, candidate);
                Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(firstCandidate!));
                Assert.Equal(UpdatedIndexBody, await File.ReadAllTextAsync(candidate));
                resumeCandidate.TrySetResult();
            });

            var first = CreateFetcher(firstClient, firstValidator).FetchAsync(_destinationPath);
            try
            {
                await candidateReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(ContentIndexFetchResult.Downloaded, await CreateFetcher(secondClient, secondValidator).FetchAsync(_destinationPath));
            }
            finally
            {
                resumeCandidate.TrySetResult();
                await first;
            }

            var publishedBody = await File.ReadAllTextAsync(_destinationPath);
            Assert.Contains(publishedBody, new[] { SampleIndexBody, UpdatedIndexBody });
            string publishedTag = publishedBody == SampleIndexBody ? "\"index-1\"" : "\"index-2\"";
            bool matchingSidecar = await File.ReadAllTextAsync(_etagPath) == Sidecar(publishedBody, publishedTag);
            using var checkClient = FakeHttpMessageHandler.BuildClient(request =>
            {
                if (matchingSidecar)
                {
                    Assert.Equal(publishedTag, Assert.Single(request.Headers.IfNoneMatch).Tag);
                    return new HttpResponseMessage(HttpStatusCode.NotModified);
                }

                Assert.Empty(request.Headers.IfNoneMatch);
                return JsonResponseWithETag(publishedBody, publishedTag);
            }, out _);
            Assert.Equal(matchingSidecar ? ContentIndexFetchResult.NotModified : ContentIndexFetchResult.Downloaded,
                await CreateFetcher(checkClient).FetchAsync(_destinationPath));
            Assert.Equal(publishedBody, await File.ReadAllTextAsync(_destinationPath));
            Assert.Equal(Sidecar(publishedBody, publishedTag), await File.ReadAllTextAsync(_etagPath));
            Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
        }

        [Fact]
        public async Task FetchAsync_CanceledCandidate_RemovesOnlyItsOwnTemporaryFiles()
        {
            await WriteCachedIndexAsync();
            using var cancellation = new CancellationTokenSource();
            using var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponseWithETag(UpdatedIndexBody, "\"index-2\""), out _);
            var validator = new CandidateValidator(_ =>
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                CreateFetcher(client, validator).FetchAsync(_destinationPath, cancellation.Token));

            Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(_destinationPath));
            Assert.Equal(Sidecar(SampleIndexBody, "\"index-1\""), await File.ReadAllTextAsync(_etagPath));
            Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
        }

        [Fact]
        public async Task FetchAsync_CacheChangedAndRetryReturns304_StopsAfterOneRetry()
        {
            await WriteCachedIndexAsync();
            int requests = 0;
            using var client = FakeHttpMessageHandler.BuildClient(request =>
            {
                requests++;
                Assert.Equal(requests == 1, request.Headers.IfNoneMatch.Any());
                File.Delete(_destinationPath);
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            }, out _);

            await Assert.ThrowsAsync<HttpRequestException>(() => CreateFetcher(client).FetchAsync(_destinationPath));

            Assert.Equal(2, requests);
            Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
        }

        [Fact]
        public async Task FetchAsync_SidecarPublicationFails_RemovesBothTemporaryFiles()
        {
            Directory.CreateDirectory(_etagPath);
            using var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponseWithETag(SampleIndexBody, "\"index-1\""), out _);

            var exception = await Record.ExceptionAsync(() => CreateFetcher(client).FetchAsync(_destinationPath));

            Assert.True(exception is IOException or UnauthorizedAccessException);
            Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(_destinationPath));
            Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
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
            Assert.Equal(Sidecar(SampleIndexBody, "\"fresh-etag\""), await File.ReadAllTextAsync(_etagPath));
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

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task FetchAsync_SidecarDoesNotIdentifyTheCachedBody_FetchesUnconditionally(bool legacySidecar)
        {
            await File.WriteAllTextAsync(_destinationPath, UpdatedIndexBody);
            await File.WriteAllTextAsync(_etagPath, legacySidecar ? "\"old-etag\"" : Sidecar(SampleIndexBody, "\"old-etag\""));
            var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponseWithETag(UpdatedIndexBody, "\"new-etag\""),
                out var handler);

            await CreateFetcher(client).FetchAsync(_destinationPath);

            Assert.Empty(handler.LastRequest!.Headers.IfNoneMatch);
            Assert.Equal(UpdatedIndexBody, await File.ReadAllTextAsync(_destinationPath));
            Assert.Equal(Sidecar(UpdatedIndexBody, "\"new-etag\""), await File.ReadAllTextAsync(_etagPath));
        }

        [Fact]
        public async Task FetchAsync_ResponseHasNoETag_DeletesExistingSidecar()
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, Sidecar(SampleIndexBody, "\"abc123\""));

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
            await File.WriteAllTextAsync(_etagPath, Sidecar(SampleIndexBody, "\"abc123\""));

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
            await File.WriteAllTextAsync(_etagPath, Sidecar(SampleIndexBody, "\"known-good\""));
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
            Assert.Equal(Sidecar(SampleIndexBody, "\"known-good\""), await File.ReadAllTextAsync(_etagPath));
            Assert.False(File.Exists(_destinationPath + ".tmp"));
        }

        [Fact]
        public async Task FetchAsync_PlainTextContentType_DownloadsSuccessfully()
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, Sidecar(SampleIndexBody, "\"abc123\""));

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
            await File.WriteAllTextAsync(_etagPath, Sidecar(SampleIndexBody, "\"abc123\""));

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
            await File.WriteAllTextAsync(_etagPath, Sidecar(SampleIndexBody, "\"abc123\""));

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
            Assert.Equal(Sidecar(UpdatedIndexBody, "\"new-etag\""), await File.ReadAllTextAsync(_etagPath));
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
            Assert.Equal(Sidecar(SampleIndexBody, "W/\"abc123\""), await File.ReadAllTextAsync(_etagPath));

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
        public async Task FetchAsync_MissingPacks_ThrowsAndLeavesCacheUntouched()
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, Sidecar(SampleIndexBody, "\"abc123\""));

            var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponse("""{ "snapshot_version": 1, "listings": [], "game_versions": {} }"""),
                out _);

            var exception = await Assert.ThrowsAsync<HttpRequestException>(() => CreateFetcher(client).FetchAsync(_destinationPath));
            Assert.Contains("'packs'", exception.Message);
            Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(_destinationPath));
        }

        [Fact]
        public async Task FetchAsync_MissingGameVersions_ThrowsAndLeavesCacheUntouched()
        {
            await File.WriteAllTextAsync(_destinationPath, SampleIndexBody);
            await File.WriteAllTextAsync(_etagPath, Sidecar(SampleIndexBody, "\"abc123\""));

            var client = FakeHttpMessageHandler.BuildClient(
                _ => JsonResponse("""{ "snapshot_version": 1, "listings": [], "packs": [] }"""),
                out _);

            var exception = await Assert.ThrowsAsync<HttpRequestException>(() => CreateFetcher(client).FetchAsync(_destinationPath));
            Assert.Contains("'game_versions'", exception.Message);
            Assert.Equal(SampleIndexBody, await File.ReadAllTextAsync(_destinationPath));
        }

        [Theory]
        [InlineData("""{ "snapshot_version": 1, "listings": [], "packs": [] }""")] // missing game_versions
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
            await File.WriteAllTextAsync(_etagPath, Sidecar(SampleIndexBody, "\"abc123\""));
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
            await File.WriteAllTextAsync(_etagPath, Sidecar(SampleIndexBody, "\"index-1\""));
        }

        private async Task WriteCachedVehiclesAsync()
        {
            await File.WriteAllTextAsync(VehiclesPath, VehiclesBody);
            await File.WriteAllTextAsync(VehiclesPath + ".etag", Sidecar(VehiclesBody, "\"vehicles-1\""));
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
            Assert.Equal(Sidecar(VehiclesBody, "\"vehicles-1\""), await File.ReadAllTextAsync(VehiclesPath + ".etag"));
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
            Assert.Equal(Sidecar(VehiclesBody, "\"vehicles-1\""), await File.ReadAllTextAsync(VehiclesPath + ".etag"));
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
            Assert.Equal(Sidecar(NewVehiclesBody, "\"vehicles-2\""), await File.ReadAllTextAsync(VehiclesPath + ".etag"));
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
