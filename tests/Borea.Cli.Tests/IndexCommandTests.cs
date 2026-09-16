using Borea.Core.Index;

namespace Borea.Cli.Tests;

public sealed class IndexCommandTests : IDisposable
{
    private readonly CliHost _host = new();

    [Theory]
    [InlineData(ContentIndexFetchResult.Downloaded, "Content index: downloaded.")]
    [InlineData(ContentIndexFetchResult.NotModified, "Content index: not modified.")]
    public async Task Refresh_ReportsTheFetchResult(ContentIndexFetchResult result, string expectedOutput)
    {
        _host.IndexFetcher.Result = result;

        var run = await _host.RunAsync("index", "refresh");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains(expectedOutput, run.Output);
        Assert.Equal(_host.Paths.GetIndexPath(), _host.IndexFetcher.DestinationPath);
    }

    [Fact]
    public async Task ReadingCommand_RefreshFailedWithACache_WarnsWithTheCacheDate()
    {
        _host.Mods.Listings.Add(ContentCommandFixtures.Listing());
        _host.Mods.Releases.Add(ContentCommandFixtures.Release());
        _host.IndexRefresh = new FakeContentIndexRefresh
        {
            Status = new ContentIndexRefreshStatus(ContentIndexRefreshOutcome.Failed, new DateTimeOffset(2026, 9, 12, 8, 30, 0, TimeSpan.Zero), "No such host is known."),
        };

        var run = await _host.RunAsync("search", "Flight");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("flight-tools", run.Output);
        Assert.Equal("warning: using the cached index from 2026-09-12 08:30 UTC, the refresh failed: No such host is known." + Environment.NewLine, run.Error);
    }

    [Theory]
    [InlineData(ContentIndexRefreshOutcome.Failed, false)]
    [InlineData(ContentIndexRefreshOutcome.NotModified, true)]
    [InlineData(ContentIndexRefreshOutcome.NotAttempted, true)]
    public async Task ReadingCommand_NoCacheOrNoFailure_PrintsNoWarning(ContentIndexRefreshOutcome outcome, bool hasCache)
    {
        _host.Mods.Listings.Add(ContentCommandFixtures.Listing());
        _host.IndexRefresh = new FakeContentIndexRefresh
        {
            Status = new ContentIndexRefreshStatus(outcome, hasCache ? DateTimeOffset.UtcNow : null, outcome == ContentIndexRefreshOutcome.Failed ? "offline" : null),
        };

        var run = await _host.RunAsync("search", "Flight");

        Assert.DoesNotContain("warning:", run.Error);
    }

    [Fact]
    public async Task Validate_NoMalformedContent_ReportsCountsAndSucceeds()
    {
        _host.IndexReader.Snapshot = Snapshot(new ContentIndexDiagnostic(
            ContentIndexDiagnosticKind.UnsupportedValue,
            ContentIndexDiagnosticScope.IndexStatus,
            "Index status state 'future-state' is not supported.",
            "active-mod"));

        var run = await _host.RunAsync("index", "validate");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Content index validation, snapshot version 1:", run.Output);
        Assert.Contains("Listings: 1 accepted, 0 unsupported, 0 malformed.", run.Output);
        Assert.Contains("Mod packs: 1 accepted, 0 unsupported, 0 malformed.", run.Output);
        Assert.Contains("Index status: 1 unsupported, 0 malformed.", run.Output);
        Assert.Contains("unsupported-value index-status active-mod", run.Output);
    }

    [Fact]
    public async Task Validate_MalformedContent_ReportsIdentityAndReasonAndFails()
    {
        _host.IndexReader.Snapshot = Snapshot(
            new ContentIndexDiagnostic(
                ContentIndexDiagnosticKind.Malformed,
                ContentIndexDiagnosticScope.Listing,
                "The listing id is invalid.",
                "bad-listing"),
            new ContentIndexDiagnostic(
                ContentIndexDiagnosticKind.UnsupportedVersion,
                ContentIndexDiagnosticScope.Release,
                "The release specification is newer than this client.",
                "future-mod",
                "2.0.0",
                2));

        var run = await _host.RunAsync("index", "validate");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Listings: 1 accepted, 0 unsupported, 1 malformed.", run.Output);
        Assert.Contains("Releases: 0 accepted, 1 unsupported, 0 malformed.", run.Output);
        Assert.Contains("malformed listing bad-listing: The listing id is invalid.", run.Output);
        Assert.Contains("unsupported-version release future-mod 2.0.0 (spec version 2):", run.Output);
    }

    [Fact]
    public async Task Validate_Json_ReturnsCountsAndDiagnosticDetails()
    {
        _host.IndexReader.Snapshot = Snapshot(
            new ContentIndexDiagnostic(
                ContentIndexDiagnosticKind.Malformed,
                ContentIndexDiagnosticScope.PackVersion,
                "The pack version is invalid.",
                "test-pack",
                "bad-version"),
            new ContentIndexDiagnostic(
                ContentIndexDiagnosticKind.UnsupportedVersion,
                ContentIndexDiagnosticScope.Release,
                "The release specification is newer than this client.",
                "future-mod",
                "2.0.0",
                2),
            new ContentIndexDiagnostic(
                ContentIndexDiagnosticKind.UnsupportedValue,
                ContentIndexDiagnosticScope.IndexStatus,
                "Index status state 'future-state' is not supported.",
                "active-mod"));

        var run = await _host.RunAsync("index", "validate", "--json");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(1, run.Json.GetProperty("snapshotVersion").GetInt32());
        var accepted = run.Json.GetProperty("accepted");
        Assert.Equal(1, accepted.GetProperty("listings").GetInt32());
        Assert.Equal(1, accepted.GetProperty("packs").GetInt32());
        Assert.Equal(2, accepted.GetProperty("gameVersions").GetInt32());
        var diagnostics = run.Json.GetProperty("diagnostics");
        Assert.Equal(1, diagnostics.GetProperty("malformed").GetInt32());
        Assert.Equal(1, diagnostics.GetProperty("unsupportedVersions").GetInt32());
        Assert.Equal(1, diagnostics.GetProperty("unsupportedValues").GetInt32());
        var entries = diagnostics.GetProperty("entries");
        Assert.Equal(3, entries.GetArrayLength());
        var unsupported = entries[1];
        Assert.Equal("unsupported-version", unsupported.GetProperty("kind").GetString());
        Assert.Equal("release", unsupported.GetProperty("scope").GetString());
        Assert.Equal("future-mod", unsupported.GetProperty("id").GetString());
        Assert.Equal("2.0.0", unsupported.GetProperty("version").GetString());
        Assert.Equal(2, unsupported.GetProperty("specVersion").GetInt32());
        Assert.Contains("newer", unsupported.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Validate_TagDiagnostic_ReportsScopeWithoutCrashing()
    {
        _host.IndexReader.Snapshot = Snapshot(new ContentIndexDiagnostic(
            ContentIndexDiagnosticKind.UnsupportedVersion,
            ContentIndexDiagnosticScope.Tags,
            "The curated tag vocabulary uses an unsupported version.",
            SpecVersion: 2));

        var run = await _host.RunAsync("index", "validate");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("unsupported-version tags (spec version 2)", run.Output);
    }

    [Fact]
    public async Task Validate_MalformedDownloads_ReportsScopeAndFails()
    {
        _host.IndexReader.Snapshot = Snapshot(new ContentIndexDiagnostic(
            ContentIndexDiagnosticKind.Malformed,
            ContentIndexDiagnosticScope.Downloads,
            "The downloads value is unreadable. The downloads total 1000 is not the sum 1200 of its host values.",
            "active-mod"));

        var run = await _host.RunAsync("index", "validate");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("  Download counts: 0 listings with counts, 1 malformed.", run.Output);
        Assert.Contains("malformed downloads active-mod: The downloads value is unreadable.", run.Output);
    }

    [Theory]
    [InlineData(ContentIndexDiagnosticScope.Images, "The images icon is unreadable.", "  Images: 1 malformed.", "malformed images active-mod: The images icon is unreadable.")]
    [InlineData(ContentIndexDiagnosticScope.Dates, "The updated_at value is unreadable.", "  Dates: 1 malformed.", "malformed dates active-mod: The updated_at value is unreadable.")]
    [InlineData(ContentIndexDiagnosticScope.ChangelogText, "The changelog_text value must be a string.", "  Changelog text: 1 malformed.", "malformed changelog-text active-mod: The changelog_text value must be a string.")]
    public async Task Validate_MalformedFieldValue_ReportsScopeAndFails(
        ContentIndexDiagnosticScope scope,
        string reason,
        string summary,
        string entry)
    {
        _host.IndexReader.Snapshot = Snapshot(new ContentIndexDiagnostic(
            ContentIndexDiagnosticKind.Malformed,
            scope,
            reason,
            "active-mod"));

        var run = await _host.RunAsync("index", "validate");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains(summary, run.Output);
        Assert.Contains(entry, run.Output);
    }

    [Fact]
    public async Task Validate_Cancellation_ReachesReaderAndReportsFailure()
    {
        _host.IndexReader.Read = async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return _host.IndexReader.Snapshot;
        };
        using var cancellation = new CancellationTokenSource();

        var command = _host.RunAsync(cancellation.Token, "index", "validate");
        await _host.IndexReader.Started.Task;
        cancellation.Cancel();
        var run = await command;

        Assert.Equal(1, run.ExitCode);
        Assert.True(_host.IndexReader.CancellationToken.IsCancellationRequested);
        Assert.Contains("The command was cancelled.", run.Error);
    }

    private static ContentIndexSnapshot Snapshot(params ContentIndexDiagnostic[] diagnostics) => new(
        1,
        new[]
        {
            new ContentIndexListing(
                "active-mod",
                null,
                Array.Empty<Borea.Core.Mods.ModVersionMetadata>(),
                new IndexStatus(IndexStatusState.Delisted, "delisted")),
        },
        new[]
        {
            new ContentIndexPack(
                "test-pack",
                Array.Empty<ContentIndexPackVersion>(),
                new IndexStatus(IndexStatusState.Delisted, "delisted")),
        },
        new ContentIndexGameVersions(1, "master-server", new[] { "2026.9.4.5400", "2026.9.7.5402" }),
        diagnostics);

    public void Dispose() => _host.Dispose();
}
