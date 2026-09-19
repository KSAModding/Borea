using System.Net;
using System.Text;
using Borea.Core.Game;
using Borea.Network.GitHub;

namespace Borea.Network.Tests.GitHub;

public sealed class GamePatchNotesFetcherTests
{
    private const string VersionsUrl = "https://raw.githubusercontent.com/KSAModding/ksa-versions/main/Content/Versions/";

    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(30);

    private static readonly GameVersion Build5438 = GameVersion.Parse("2026.9.10.5438");

    private static readonly GameVersion Build5402 = GameVersion.Parse("2026.9.7.5402");

    private static readonly GameVersion Build5348 = GameVersion.Parse("2026.8.22.5348");

    private static string ChangeLog(GameVersion build, int fromRevision) =>
        $$"""{ "build": "{{build}}", "date": "2026-09-15", "fromRevision": {{fromRevision}}, "toRevision": {{build.Revision}}, "commits": [ { "rev": {{build.Revision}}, "lines": ["Change of {{build.Revision}}."] } ] }""";

    private static string ChangeLog(string build, int fromRevision) => ChangeLog(GameVersion.Parse(build), fromRevision);

    private static List<string> FileNames(List<Uri> requested) => requested.Select(uri => uri.AbsoluteUri[VersionsUrl.Length..]).ToList();

    /// <summary>A fetcher whose host serves <paramref name="files"/> by name, answers 404 for every other file, and records each request.</summary>
    private static GamePatchNotesFetcher Fetcher(Dictionary<string, string> files, List<Uri> requested, MemoryPatchNotesCache? cache = null, TimeSpan? timeout = null) =>
        Fetcher(
            (request, _) =>
            {
                requested.Add(request.RequestUri!);
                var name = request.RequestUri!.AbsoluteUri[VersionsUrl.Length..];
                return Task.FromResult(files.TryGetValue(name, out var json)
                    ? FakeHttpMessageHandler.JsonResponse(json)
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
            },
            cache,
            timeout);

    private static GamePatchNotesFetcher Fetcher(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, MemoryPatchNotesCache? cache = null, TimeSpan? timeout = null) =>
        new(FakeHttpMessageHandler.BuildClient(respond, out _), cache ?? new MemoryPatchNotesCache(), timeout ?? FetchTimeout);

    [Fact]
    public async Task FetchAsync_PublishedFiles_ReturnsThemNewestFirstAndCachesThem()
    {
        var requested = new List<Uri>();
        var cache = new MemoryPatchNotesCache();
        var fetcher = Fetcher(new() { ["v2026.9.X.5438.json"] = ChangeLog(Build5438, 5402), ["v2026.9.X.5402.json"] = ChangeLog(Build5402, 5400) }, requested, cache);

        var fetch = await fetcher.FetchAsync([Build5402, Build5438], 5400, 20);

        Assert.True(fetch.Complete);
        Assert.Equal(["2026.9.10.5438", "2026.9.7.5402"], fetch.Notes.Select(entry => entry.Build));
        Assert.Equal(["Change of 5438."], fetch.Notes[0].Lines);
        Assert.Equal([VersionsUrl + "v2026.9.X.5438.json", VersionsUrl + "v2026.9.X.5402.json"], requested.Select(uri => uri.AbsoluteUri));
        Assert.Equal(["v2026.9.X.5402.json", "v2026.9.X.5438.json"], cache.Entries.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task FetchAsync_CachedFile_IsNotFetchedAgain()
    {
        var requested = new List<Uri>();
        var cache = new MemoryPatchNotesCache();
        var files = new Dictionary<string, string> { ["v2026.9.X.5438.json"] = ChangeLog(Build5438, 5402) };
        await Fetcher(files, requested, cache).FetchAsync([Build5438], 5402, 20);

        var fetch = await Fetcher([], requested, cache).FetchAsync([Build5438], 5402, 20);

        Assert.True(fetch.Complete);
        Assert.Equal(["2026.9.10.5438"], fetch.Notes.Select(entry => entry.Build));
        Assert.Single(requested);
    }

    [Fact]
    public async Task FetchAsync_BrokenDownload_IsSkippedAndNotCached()
    {
        var cache = new MemoryPatchNotesCache();
        var fetcher = Fetcher(new() { ["v2026.9.X.5438.json"] = "<html>Rate limited</html>", ["v2026.8.X.5348.json"] = ChangeLog(Build5348, 5261) }, [], cache);

        var fetch = await fetcher.FetchAsync([Build5438, Build5348], 5261, 20);

        Assert.True(fetch.Complete);
        Assert.Equal(["2026.8.22.5348"], fetch.Notes.Select(entry => entry.Build));
        Assert.DoesNotContain("v2026.9.X.5438.json", cache.Entries.Keys);
    }

    [Fact]
    public async Task FetchAsync_BrokenCachedFile_IsDownloadedAgainAndReplaced()
    {
        var requested = new List<Uri>();
        var cache = new MemoryPatchNotesCache();
        cache.Entries["v2026.9.X.5438.json"] = Encoding.UTF8.GetBytes("""{ "build": "2026.9.10.5438", "commits": """);
        var fetcher = Fetcher(new() { ["v2026.9.X.5438.json"] = ChangeLog(Build5438, 5402) }, requested, cache);

        var fetch = await fetcher.FetchAsync([Build5438], 5402, 20);

        Assert.True(fetch.Complete);
        Assert.Equal(["2026.9.10.5438"], fetch.Notes.Select(entry => entry.Build));
        Assert.Equal(["v2026.9.X.5438.json"], FileNames(requested));
        Assert.Equal(ChangeLog(Build5438, 5402), Encoding.UTF8.GetString(cache.Entries["v2026.9.X.5438.json"]));
    }

    [Fact]
    public async Task FetchAsync_MissingFile_IsIncompleteAndLoadsTheOthers()
    {
        var requested = new List<Uri>();
        var fetcher = Fetcher(new() { ["v2026.9.X.5402.json"] = ChangeLog(Build5402, 5400) }, requested);

        var fetch = await fetcher.FetchAsync([Build5438, Build5402], 5400, 20);

        Assert.False(fetch.Complete);
        Assert.Equal(["2026.9.7.5402"], fetch.Notes.Select(entry => entry.Build));
        Assert.Equal(2, requested.Count);
    }

    [Fact]
    public async Task FetchAsync_BuildWhoseChangesANewerFileLists_IsNotRequested()
    {
        var requested = new List<Uri>();
        var build4082 = GameVersion.Parse("2026.4.13.4082");
        var fetcher = Fetcher(new() { ["v2026.4.X.4082.json"] = ChangeLog(build4082, 4057) }, requested);

        var fetch = await fetcher.FetchAsync([build4082, GameVersion.Parse("2026.4.12.4081")], 4057, 20);

        Assert.True(fetch.Complete);
        Assert.Equal(["2026.4.13.4082"], fetch.Notes.Select(entry => entry.Build));
        Assert.Equal([VersionsUrl + "v2026.4.X.4082.json"], requested.Select(uri => uri.AbsoluteUri));
    }

    [Fact]
    public async Task FetchAsync_BuildMissingFromTheList_IsLoadedThroughTheFromRevision()
    {
        var requested = new List<Uri>();
        var fetcher = Fetcher(
            new()
            {
                ["v2026.9.X.5438.json"] = ChangeLog(Build5438, 5402),
                ["v2026.9.X.5402.json"] = ChangeLog(Build5402, 5400),
                ["v2026.9.X.5400.json"] = ChangeLog("2026.9.4.5400", 5348),
                ["v2026.8.X.5348.json"] = ChangeLog(Build5348, 5261),
            },
            requested);

        var fetch = await fetcher.FetchAsync([Build5348, Build5402, Build5438], 5261, 20);

        Assert.True(fetch.Complete);
        Assert.False(fetch.Capped);
        Assert.Equal(["2026.9.10.5438", "2026.9.7.5402", "2026.9.4.5400", "2026.8.22.5348"], fetch.Notes.Select(entry => entry.Build));
        Assert.Equal(["v2026.9.X.5438.json", "v2026.9.X.5402.json", "v2026.9.X.5400.json", "v2026.8.X.5348.json"], FileNames(requested));
    }

    [Fact]
    public async Task FetchAsync_FromRevisionInTheMonthBefore_IsFoundThere()
    {
        var requested = new List<Uri>();
        var fetcher = Fetcher(
            new()
            {
                ["v2026.9.X.5402.json"] = ChangeLog(Build5402, 5390),
                ["v2026.8.X.5390.json"] = ChangeLog("2026.8.30.5390", 5348),
            },
            requested);

        var fetch = await fetcher.FetchAsync([Build5402], 5348, 20);

        Assert.True(fetch.Complete);
        Assert.Equal(["2026.9.7.5402", "2026.8.30.5390"], fetch.Notes.Select(entry => entry.Build));
        Assert.Equal(["v2026.9.X.5402.json", "v2026.9.X.5390.json", "v2026.8.X.5390.json"], FileNames(requested));
    }

    [Fact]
    public async Task FetchAsync_FromRevisionWithoutAFile_IsIncomplete()
    {
        var fetcher = Fetcher(new() { ["v2026.9.X.5402.json"] = ChangeLog(Build5402, 5400) }, []);

        var fetch = await fetcher.FetchAsync([Build5402], 5348, 20);

        Assert.False(fetch.Complete);
        Assert.Equal(["2026.9.7.5402"], fetch.Notes.Select(entry => entry.Build));
    }

    [Fact]
    public async Task FetchAsync_FromRevisionOfTheInstalledBuild_IsNotRequested()
    {
        var requested = new List<Uri>();
        var fetcher = Fetcher(new() { ["v2026.9.X.5402.json"] = ChangeLog(Build5402, 5400) }, requested);

        var fetch = await fetcher.FetchAsync([Build5402], 5400, 20);

        Assert.True(fetch.Complete);
        Assert.Equal(["v2026.9.X.5402.json"], FileNames(requested));
    }

    [Fact]
    public async Task FetchAsync_MoreBuildsThanFiles_ReadsTheNewestAndIsCapped()
    {
        var requested = new List<Uri>();
        var fetcher = Fetcher(new() { ["v2026.9.X.5438.json"] = ChangeLog(Build5438, 5402), ["v2026.9.X.5402.json"] = ChangeLog(Build5402, 5348) }, requested);

        var fetch = await fetcher.FetchAsync([Build5348, Build5402, Build5438], 5261, 2);

        Assert.True(fetch.Capped);
        Assert.True(fetch.Complete);
        Assert.Equal(["2026.9.10.5438", "2026.9.7.5402"], fetch.Notes.Select(entry => entry.Build));
        Assert.Equal(2, requested.Count);
    }

    [Fact]
    public async Task FetchAsync_BuildWithoutAFileOfItsOwn_DoesNotCountTowardTheCap()
    {
        var requested = new List<Uri>();
        var build4082 = GameVersion.Parse("2026.4.13.4082");
        var build4057 = GameVersion.Parse("2026.4.9.4057");
        var fetcher = Fetcher(new() { ["v2026.4.X.4082.json"] = ChangeLog(build4082, 4057), ["v2026.4.X.4057.json"] = ChangeLog(build4057, 4036) }, requested);

        var fetch = await fetcher.FetchAsync([build4082, GameVersion.Parse("2026.4.12.4081"), build4057], 4036, 2);

        Assert.False(fetch.Capped);
        Assert.True(fetch.Complete);
        Assert.Equal(["2026.4.13.4082", "2026.4.9.4057"], fetch.Notes.Select(entry => entry.Build));
        Assert.Equal(["v2026.4.X.4082.json", "v2026.4.X.4057.json"], FileNames(requested));
    }

    [Fact]
    public async Task FetchAsync_Timeout_IsIncompleteAndStopsAskingTheHost()
    {
        var requests = 0;
        var cache = new MemoryPatchNotesCache();
        cache.Entries["v2026.8.X.5348.json"] = Encoding.UTF8.GetBytes(ChangeLog(Build5348, 5261));
        var fetcher = Fetcher(
            async (_, token) =>
            {
                Interlocked.Increment(ref requests);
                await Task.Delay(Timeout.Infinite, token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            },
            cache,
            TimeSpan.FromMilliseconds(100));

        var fetch = await fetcher.FetchAsync([Build5438, Build5402, Build5348], 5261, 20);

        Assert.False(fetch.Complete);
        Assert.Equal(["2026.8.22.5348"], fetch.Notes.Select(entry => entry.Build));
        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FetchAsync_FileAboveTheCap_IsIncompleteAndNotCached(bool announced)
    {
        var cache = new MemoryPatchNotesCache();
        var body = ChangeLog(Build5438, 5402) + new string(' ', GamePatchNotesFile.MaxDownloadBytes);
        var fetcher = Fetcher(
            (_, _) =>
            {
                HttpContent content = announced
                    ? new StringContent(body)
                    : new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body)));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            },
            cache);

        var fetch = await fetcher.FetchAsync([Build5438], 5402, 20);

        Assert.False(fetch.Complete);
        Assert.Empty(fetch.Notes);
        Assert.Empty(cache.Entries);
    }

    [Fact]
    public async Task FetchAsync_CallerCancels_Throws()
    {
        using var cancel = new CancellationTokenSource();
        var fetcher = Fetcher(
            async (_, token) =>
            {
                await cancel.CancelAsync();
                await Task.Delay(Timeout.Infinite, token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetcher.FetchAsync([Build5438], 5402, 20, cancel.Token));
    }

    private sealed class MemoryPatchNotesCache : IGamePatchNotesCache
    {
        public Dictionary<string, byte[]> Entries { get; } = new(StringComparer.Ordinal);

        public Task<byte[]?> ReadAsync(string fileName, CancellationToken cancellationToken = default) =>
            Task.FromResult(Entries.TryGetValue(fileName, out var bytes) ? bytes : null);

        public Task WriteAsync(string fileName, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
        {
            Entries[fileName] = bytes.ToArray();
            return Task.CompletedTask;
        }
    }
}
