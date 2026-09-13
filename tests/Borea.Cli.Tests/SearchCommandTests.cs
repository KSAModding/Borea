using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Network.Index;
using Borea.Storage.Index;

namespace Borea.Cli.Tests;

public sealed class SearchCommandTests : IDisposable
{
    private readonly CliHost _host = new();

    [Fact]
    public async Task Search_Hit_PrintsIdNameNewestReleaseAndCompatibility()
    {
        _host.Mods.Listings.Add(ContentCommandFixtures.Listing());
        _host.Mods.Releases.Add(ContentCommandFixtures.Release());
        _host.InstalledVersion = Installed("2026.9.7.5402");

        var run = await _host.RunAsync("search", "Flight");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("flight-tools  Flight Tools  2.0.0  compatible", run.Output);
        Assert.Equal(string.Empty, run.Error);
    }

    [Fact]
    public async Task Search_DesignSnapshotFixture_UsesTheMappedIndexWithoutNetwork()
    {
        var indexPath = _host.Paths.GetIndexPath();
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Index", "Fixtures", "current-snapshot.json"),
            indexPath);
        var snapshot = await new ContentIndexReader(_host.Paths).ReadAsync();
        _host.IndexReader.Snapshot = snapshot;
        foreach (var listing in snapshot.Listings.Where(listing => listing.Authored is not null))
        {
            _host.Mods.Listings.Add(listing.Authored!);
            _host.Mods.Releases.AddRange(listing.Releases);
        }
        _host.InstalledVersion = Installed("2026.9.7.5402");

        var run = await _host.RunAsync("search", "Advanced Flight");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("AdvancedFlightComputer  Advanced Flight Computer  0.7.5  compatible", run.Output);
        Assert.Null(_host.IndexFetcher.DestinationPath);
    }

    [Fact]
    public async Task Search_WarmedSnapshot_RemainsUsableWhenTheReaderFails()
    {
        var listing = ContentCommandFixtures.Listing();
        var release = ContentCommandFixtures.Release();
        _host.IndexReader.Snapshot = Snapshot(
            new[] { new ContentIndexListing(listing.ModId, listing, new[] { release }, null) },
            new[]
            {
                new ContentIndexDiagnostic(
                    ContentIndexDiagnosticKind.UnsupportedValue,
                    ContentIndexDiagnosticScope.IndexStatus,
                    "Index status state 'future-state' is not supported.",
                    listing.ModId),
            });
        var snapshots = new ContentIndexSnapshotProvider(_host.IndexFetcher, _host.IndexReader, _host.Paths);
        _host.IndexSnapshots = snapshots;
        _host.ModRepository = new ContentIndexModRepository(snapshots);
        Assert.Single(await _host.ModRepository.GetAvailableModsAsync());
        _host.IndexReader.Read = _ => throw new IOException("The cache file is temporarily unreadable.");

        var run = await _host.RunAsync("search", "flight");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("flight-tools  Flight Tools  2.0.0", run.Output);
        Assert.Contains("unsupported-value index-status flight-tools", run.Output);
        Assert.Equal(string.Empty, run.Error);
    }

    [Fact]
    public async Task Search_NoHit_PrintsAnEmptyResult()
    {
        _host.Mods.Listings.Add(ContentCommandFixtures.Listing());

        var run = await _host.RunAsync("search", "engines");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("No listings match 'engines'.", run.Output);
    }

    [Fact]
    public async Task Search_ReleaseNeedsANewerGame_PrintsIncompatible()
    {
        _host.Mods.Listings.Add(ContentCommandFixtures.Listing());
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(gameMinRevision: 5402));
        _host.InstalledVersion = Installed("2026.8.22.5348");

        var run = await _host.RunAsync("search", "Flight");

        Assert.Contains("2.0.0  incompatible", run.Output);
    }

    [Fact]
    public async Task Search_NoInstalledBuild_PrintsUnknownCompatibility()
    {
        _host.Mods.Listings.Add(ContentCommandFixtures.Listing());
        _host.Mods.Releases.Add(ContentCommandFixtures.Release());

        var run = await _host.RunAsync("search", "Flight");

        Assert.Contains("2.0.0  unknown", run.Output);
    }

    [Fact]
    public async Task Search_UnknownSpecVersion_KeepsTheListingAndDiagnostic()
    {
        _host.IndexReader.Snapshot = Snapshot(new ContentIndexDiagnostic(
            ContentIndexDiagnosticKind.UnsupportedVersion,
            ContentIndexDiagnosticScope.Listing,
            "Listing spec version 2 is newer than this client.",
            "future-tools",
            SpecVersion: 2));

        var run = await _host.RunAsync("search", "future");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("future-tools  unknown listing  unknown  unknown", run.Output);
        Assert.Contains("unsupported-version listing future-tools (spec version 2)", run.Output);
    }

    [Fact]
    public async Task Search_Json_CarriesKnownAndUnknownResultsAndStatusDiagnostics()
    {
        _host.Mods.Listings.Add(ContentCommandFixtures.Listing());
        _host.Mods.Releases.Add(ContentCommandFixtures.Release());
        _host.InstalledVersion = Installed("2026.9.7.5402");
        _host.IndexReader.Snapshot = Snapshot(
            new ContentIndexDiagnostic(
                ContentIndexDiagnosticKind.UnsupportedValue,
                ContentIndexDiagnosticScope.IndexStatus,
                "Index status state 'future-state' is not supported.",
                "flight-tools"),
            new ContentIndexDiagnostic(
                ContentIndexDiagnosticKind.UnsupportedVersion,
                ContentIndexDiagnosticScope.Listing,
                "Listing spec version 2 is newer than this client.",
                "flight-future",
                SpecVersion: 2));

        var run = await _host.RunAsync("search", "flight", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("2026.9.7.5402", run.Json.GetProperty("installedGameVersion").GetString());
        var results = run.Json.GetProperty("results");
        Assert.Equal(2, results.GetArrayLength());
        Assert.Equal("flight-future", results[0].GetProperty("id").GetString());
        Assert.Equal("unknown", results[0].GetProperty("state").GetString());
        Assert.Equal("flight-tools", results[1].GetProperty("id").GetString());
        Assert.Equal("2.0.0", results[1].GetProperty("latestVersion").GetString());
        Assert.Equal("compatible", results[1].GetProperty("compatibility").GetString());
        var diagnostics = run.Json.GetProperty("diagnostics");
        Assert.Equal(2, diagnostics.GetArrayLength());
        Assert.Contains(diagnostics.EnumerateArray(), item => item.GetProperty("scope").GetString() == "index-status");
    }

    [Fact]
    public async Task Search_EmptyText_IsAUsageErrorWithoutBuildingServices()
    {
        var run = await _host.RunAsync("search", " ");

        Assert.Equal(2, run.ExitCode);
        Assert.Equal(0, _host.Builds);
    }

    private static FakeInstalledGameVersionProvider Installed(string version) => new()
    {
        Installed = new InstalledGameVersion(GameVersion.Parse(version), version),
    };

    private static ContentIndexSnapshot Snapshot(params ContentIndexDiagnostic[] diagnostics) =>
        Snapshot(Array.Empty<ContentIndexListing>(), diagnostics);

    private static ContentIndexSnapshot Snapshot(
        IReadOnlyList<ContentIndexListing> listings,
        IReadOnlyList<ContentIndexDiagnostic> diagnostics) => new(
        1,
        listings,
        Array.Empty<ContentIndexPack>(),
        null,
        diagnostics);

    public void Dispose() => _host.Dispose();
}
