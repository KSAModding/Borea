using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.Mods;
using Borea.Network.Index;
using Borea.Storage.Index;

namespace Borea.Cli.Tests;

public sealed class ShowCommandTests : IDisposable
{
    private readonly CliHost _host = new();

    [Fact]
    public async Task Show_PrintsTheListingLinksStatusAndEveryReleaseNewestFirst()
    {
        var listing = ContentCommandFixtures.Listing(
            status: ModStatus.Deprecated,
            supersededBy: "new-flight-tools");
        var newest = ContentCommandFixtures.Release(version: "2.0.0");
        var yanked = ContentCommandFixtures.Release(
            version: "1.0.0",
            yanked: true,
            yankedReason: "This release is broken.");
        _host.Mods.Listings.Add(listing);
        _host.Mods.Releases.AddRange(new[] { yanked, newest });
        _host.IndexReader.Snapshot = Snapshot(new ContentIndexListing(
            listing.ModId,
            listing,
            new[] { yanked, newest },
            new IndexStatus(IndexStatusState.Disputed, "disputed", reason: "Ownership is under review.")));
        _host.InstalledVersion = Installed("2026.9.7.5402");

        var run = await _host.RunAsync("show", "flight-tools");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Flight Tools (flight-tools)", run.Output);
        Assert.Contains("Status: deprecated", run.Output);
        Assert.Contains("Superseded by: new-flight-tools", run.Output);
        Assert.Contains("repository: https://example.com/flight-tools", run.Output);
        Assert.Contains("Index status: disputed", run.Output);
        Assert.True(run.Output.IndexOf("2.0.0", StringComparison.Ordinal) < run.Output.IndexOf("1.0.0", StringComparison.Ordinal));
        Assert.Contains("1.0.0  compatible  stable  yanked: This release is broken.", run.Output);
    }

    [Fact]
    public async Task Show_PrintsTheChangelogUnderEachReleaseThatHasOne()
    {
        var listing = ContentCommandFixtures.Listing();
        var text = ContentCommandFixtures.Release(version: "2.0.0", changelog: "## Fixes\n\n- Orbit hold stays stable.");
        var link = ContentCommandFixtures.Release(version: "1.1.0", changelog: "https://example.com/flight-tools/1.1.0");
        var none = ContentCommandFixtures.Release(version: "1.0.0");
        _host.Mods.Listings.Add(listing);
        _host.Mods.Releases.AddRange(new[] { none, link, text });
        _host.IndexReader.Snapshot = Snapshot(new ContentIndexListing(listing.ModId, listing, new[] { none, link, text }, null));

        var run = await _host.RunAsync("show", listing.ModId);

        Assert.Equal(0, run.ExitCode);
        var output = run.Output.ReplaceLineEndings("\n");
        Assert.Contains("    Changelog:\n      ## Fixes\n\n      - Orbit hold stays stable.\n", output);
        Assert.Contains("    Changelog: https://example.com/flight-tools/1.1.0\n", output);
        Assert.Equal(2, output.Split("Changelog:").Length - 1);
    }

    [Fact]
    public async Task Show_Json_CarriesTheChangelogOfEachRelease()
    {
        var listing = ContentCommandFixtures.Listing();
        var linked = ContentCommandFixtures.Release(version: "2.0.0", changelog: "https://example.com/flight-tools/2.0.0");
        var none = ContentCommandFixtures.Release(version: "1.0.0");
        _host.Mods.Listings.Add(listing);
        _host.Mods.Releases.AddRange(new[] { none, linked });
        _host.IndexReader.Snapshot = Snapshot(new ContentIndexListing(listing.ModId, listing, new[] { none, linked }, null));

        var run = await _host.RunAsync("show", listing.ModId, "--json");

        Assert.Equal(0, run.ExitCode);
        var releases = run.Json.GetProperty("releases");
        Assert.Equal("https://example.com/flight-tools/2.0.0", releases[0].GetProperty("changelog").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, releases[1].GetProperty("changelog").ValueKind);
    }

    [Fact]
    public async Task Show_PrintsTheChangelogTextInsteadOfTheLink()
    {
        var listing = ContentCommandFixtures.Listing();
        var release = ContentCommandFixtures.Release(
            changelog: "https://example.com/flight-tools/2.0.0",
            changelogText: "## Changes\n- Orbit hold stays stable.");
        _host.Mods.Listings.Add(listing);
        _host.Mods.Releases.Add(release);
        _host.IndexReader.Snapshot = Snapshot(new ContentIndexListing(listing.ModId, listing, new[] { release }, null));

        var run = await _host.RunAsync("show", listing.ModId);

        Assert.Equal(0, run.ExitCode);
        var output = run.Output.ReplaceLineEndings("\n");
        Assert.Contains("    Changelog:\n      ## Changes\n      - Orbit hold stays stable.\n", output);
        Assert.DoesNotContain("https://example.com/flight-tools/2.0.0", output);
    }

    [Fact]
    public async Task Show_Json_CarriesTheChangelogTextBesideTheLink()
    {
        var listing = ContentCommandFixtures.Listing();
        var release = ContentCommandFixtures.Release(
            changelog: "https://example.com/flight-tools/2.0.0",
            changelogText: "## Changes\n- Orbit hold stays stable.");
        _host.Mods.Listings.Add(listing);
        _host.Mods.Releases.Add(release);
        _host.IndexReader.Snapshot = Snapshot(new ContentIndexListing(listing.ModId, listing, new[] { release }, null));

        var run = await _host.RunAsync("show", listing.ModId, "--json");

        Assert.Equal(0, run.ExitCode);
        var json = Assert.Single(run.Json.GetProperty("releases").EnumerateArray());
        Assert.Equal("https://example.com/flight-tools/2.0.0", json.GetProperty("changelog").GetString());
        Assert.Equal("## Changes\n- Orbit hold stays stable.", json.GetProperty("changelogText").GetString());
    }

    [Fact]
    public async Task ShowVersion_PrintsOneReleaseAndItsDependencies()
    {
        var listing = ContentCommandFixtures.Listing();
        var release = ContentCommandFixtures.Release(dependencies: new ModDependency[]
        {
            new("required-lib", ModDependencyKind.Required, ModVersion.Parse("1.0.0"), source: MetadataSource.Authored),
            ModDependency.OfAlternatives(ModDependencyKind.Recommends, new[]
            {
                new ModDependencyAlternative("first-lib", ModVersion.Parse("2.0.0")),
                new ModDependencyAlternative("second-lib"),
            }, MetadataSource.Derived),
        });
        _host.Mods.Listings.Add(listing);
        _host.Mods.Releases.Add(release);
        _host.IndexReader.Snapshot = Snapshot(new ContentIndexListing(listing.ModId, listing, new[] { release }, null));

        var run = await _host.RunAsync("show", "flight-tools", "--version", "2.0.0");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("required  required-lib >= 1.0.0 (authored)", run.Output);
        Assert.Contains("recommends  any of [first-lib >= 2.0.0, second-lib] (derived)", run.Output);
        Assert.Contains((listing.ModId, ModVersion.Parse("2.0.0")), _host.Mods.ReleaseRequests);
    }

    [Fact]
    public async Task Show_UnknownReleaseSpecVersion_KeepsItInOrderInJson()
    {
        var listing = ContentCommandFixtures.Listing();
        var known = ContentCommandFixtures.Release(version: "2.0.0");
        _host.Mods.Listings.Add(listing);
        _host.Mods.Releases.Add(known);
        _host.IndexReader.Snapshot = Snapshot(
            new[] { new ContentIndexListing(listing.ModId, listing, new[] { known }, null) },
            new[]
            {
                new ContentIndexDiagnostic(
                    ContentIndexDiagnosticKind.UnsupportedVersion,
                    ContentIndexDiagnosticScope.Release,
                    "Release spec version 2 is newer than this client.",
                    listing.ModId,
                    "3.0.0",
                    2),
            });

        var run = await _host.RunAsync("show", listing.ModId, "--json");

        Assert.Equal(0, run.ExitCode);
        var releases = run.Json.GetProperty("releases");
        Assert.Equal(2, releases.GetArrayLength());
        Assert.Equal("3.0.0", releases[0].GetProperty("version").GetString());
        Assert.Equal("unknown", releases[0].GetProperty("state").GetString());
        Assert.Equal(2, releases[0].GetProperty("specVersion").GetInt32());
        Assert.Equal("2.0.0", releases[1].GetProperty("version").GetString());
        Assert.Equal("known", releases[1].GetProperty("state").GetString());
    }

    [Fact]
    public async Task Show_UnknownSpecVersion_KeepsTheListingIdAndDiagnostic()
    {
        _host.IndexReader.Snapshot = Snapshot(
            listings: Array.Empty<ContentIndexListing>(),
            diagnostics: new[]
            {
                new ContentIndexDiagnostic(
                    ContentIndexDiagnosticKind.UnsupportedVersion,
                    ContentIndexDiagnosticScope.Listing,
                    "Listing spec version 2 is newer than this client.",
                    "future-tools",
                    SpecVersion: 2),
            });

        var run = await _host.RunAsync("show", "future-tools");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("future-tools (unknown)", run.Output);
        Assert.Contains("unsupported-version listing future-tools (spec version 2)", run.Output);
    }

    [Fact]
    public async Task Show_UnknownIndexStatus_KeepsRawStatusAndDiagnostic()
    {
        var listing = ContentCommandFixtures.Listing();
        var status = new IndexStatus(IndexStatusState.Unknown, "future-state", reason: "A future warning.");
        _host.Mods.Listings.Add(listing);
        _host.IndexReader.Snapshot = Snapshot(
            new[] { new ContentIndexListing(listing.ModId, listing, Array.Empty<ModVersionMetadata>(), status) },
            new[]
            {
                new ContentIndexDiagnostic(
                    ContentIndexDiagnosticKind.UnsupportedValue,
                    ContentIndexDiagnosticScope.IndexStatus,
                    "Index status state 'future-state' is not supported.",
                    listing.ModId),
            });

        var run = await _host.RunAsync("show", listing.ModId);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Index status: future-state", run.Output);
        Assert.Contains("unsupported-value index-status flight-tools", run.Output);
    }

    [Fact]
    public async Task Show_DelistedTombstone_PrintsTheIndexStatus()
    {
        var tombstone = new ContentIndexListing(
            "removed-tools",
            null,
            Array.Empty<ModVersionMetadata>(),
            new IndexStatus(IndexStatusState.Delisted, "delisted", reason: "Removed by a steward."));
        _host.IndexReader.Snapshot = Snapshot(tombstone);

        var run = await _host.RunAsync("show", "removed-tools");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("removed-tools (delisted)", run.Output);
        Assert.Contains("Index status: delisted", run.Output);
        Assert.Contains("Index reason: Removed by a steward.", run.Output);
    }

    [Fact]
    public async Task ShowVersion_UnknownReleaseSpecVersion_PrintsUnknownAndTheDiagnostic()
    {
        var listing = ContentCommandFixtures.Listing();
        _host.Mods.Listings.Add(listing);
        _host.IndexReader.Snapshot = Snapshot(
            new[] { new ContentIndexListing(listing.ModId, listing, Array.Empty<ModVersionMetadata>(), null) },
            new[]
            {
                new ContentIndexDiagnostic(
                    ContentIndexDiagnosticKind.UnsupportedVersion,
                    ContentIndexDiagnosticScope.Release,
                    "Release spec version 2 is newer than this client.",
                    listing.ModId,
                    "3.0.0",
                    2),
            });

        var run = await _host.RunAsync("show", listing.ModId, "--version", "3.0.0+local.1");
        var json = await _host.RunAsync("show", listing.ModId, "--version", "3.0.0+local.1", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("3.0.0  unknown (spec version 2)", run.Output);
        Assert.Contains("unsupported-version release flight-tools 3.0.0", run.Output);
        var unknown = Assert.Single(json.Json.GetProperty("releases").EnumerateArray());
        Assert.Equal("unknown", unknown.GetProperty("state").GetString());
        Assert.Equal("3.0.0", unknown.GetProperty("version").GetString());
        Assert.Equal(2, unknown.GetProperty("specVersion").GetInt32());
    }

    [Fact]
    public async Task Show_DesignSnapshotFixture_UsesMappedReleaseDataWithoutNetwork()
    {
        var indexPath = _host.Paths.GetIndexPath();
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Index", "Fixtures", "current-snapshot.json"),
            indexPath);
        var snapshot = await new ContentIndexReader(_host.Paths).ReadAsync();
        _host.IndexReader.Snapshot = snapshot;
        foreach (var entry in snapshot.Listings.Where(entry => entry.Authored is not null))
        {
            _host.Mods.Listings.Add(entry.Authored!);
            _host.Mods.Releases.AddRange(entry.Releases);
        }
        _host.InstalledVersion = Installed("2026.9.7.5402");

        var run = await _host.RunAsync(
            "show",
            "AdvancedFlightComputer",
            "--version",
            "0.7.5",
            "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("Advanced Flight Computer", run.Json.GetProperty("listing").GetProperty("name").GetString());
        var release = Assert.Single(run.Json.GetProperty("releases").EnumerateArray());
        Assert.Equal("0.7.5", release.GetProperty("version").GetString());
        Assert.Equal("compatible", release.GetProperty("compatibility").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Array, release.GetProperty("dependencies").ValueKind);
        Assert.Null(_host.IndexFetcher.DestinationPath);
    }

    [Fact]
    public async Task Show_WarmedSnapshot_RemainsUsableWhenTheReaderFails()
    {
        var listing = ContentCommandFixtures.Listing();
        var release = ContentCommandFixtures.Release();
        _host.IndexReader.Snapshot = Snapshot(
            new[]
            {
                new ContentIndexListing(
                    listing.ModId,
                    listing,
                    new[] { release },
                    new IndexStatus(IndexStatusState.Disputed, "disputed", reason: "Ownership is under review.")),
            },
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

        var run = await _host.RunAsync("show", listing.ModId);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Flight Tools (flight-tools)", run.Output);
        Assert.Contains("Index status: disputed", run.Output);
        Assert.Contains("unsupported-value index-status flight-tools", run.Output);
        Assert.Equal(string.Empty, run.Error);
    }

    [Fact]
    public async Task ShowVersion_Json_CarriesCompatibilityYankAndDependencies()
    {
        var listing = ContentCommandFixtures.Listing();
        var release = ContentCommandFixtures.Release(
            gameMaxRevision: 5348,
            yanked: true,
            yankedReason: "Broken on newer games.",
            dependencies: new[]
            {
                new ModDependency("required-lib", ModDependencyKind.Required, ModVersion.Parse("1.0.0")),
            });
        _host.Mods.Listings.Add(listing);
        _host.Mods.Releases.Add(release);
        _host.IndexReader.Snapshot = Snapshot(new ContentIndexListing(listing.ModId, listing, new[] { release }, null));
        _host.InstalledVersion = Installed("2026.9.7.5402");

        var run = await _host.RunAsync("show", listing.ModId, "--version", "2.0.0", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("known", run.Json.GetProperty("state").GetString());
        Assert.Equal("Flight Tools", run.Json.GetProperty("listing").GetProperty("name").GetString());
        var shown = run.Json.GetProperty("releases")[0];
        Assert.Equal("untested", shown.GetProperty("compatibility").GetString());
        Assert.True(shown.GetProperty("yanked").GetBoolean());
        Assert.Equal("Broken on newer games.", shown.GetProperty("yankedReason").GetString());
        Assert.Equal("required", shown.GetProperty("dependencies")[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Show_MissingListing_Fails()
    {
        var run = await _host.RunAsync("show", "missing-tools");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Listing 'missing-tools' was not found.", run.Error);
    }

    [Fact]
    public async Task Show_KnownDownloadCounts_PrintsTotalHostsAndEachListedReleaseWithACount()
    {
        var listing = ContentCommandFixtures.Listing();
        var newest = ContentCommandFixtures.Release(version: "2.0.0");
        var older = ContentCommandFixtures.Release(version: "1.0.0");
        _host.Mods.Listings.Add(listing);
        _host.Mods.Releases.AddRange(new[] { newest, older });
        _host.IndexReader.Snapshot = Snapshot(
            new ContentIndexListing(listing.ModId, listing, new[] { newest, older }, null, Counts()));

        var run = await _host.RunAsync("show", listing.ModId);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Downloads: 1200 (github 479, spacedock 721)", run.Output);
        Assert.Contains("    Downloads: 40 (github 17, spacedock 23)", run.Output);
        Assert.Equal(2, CountOccurrences(run.Output, "Downloads:"));
        Assert.True(run.Output.IndexOf("40 (github 17", StringComparison.Ordinal) < run.Output.IndexOf("1.0.0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Show_Json_CarriesListingAndReleaseDownloadCounts()
    {
        var listing = ContentCommandFixtures.Listing();
        var newest = ContentCommandFixtures.Release(version: "2.0.0");
        var older = ContentCommandFixtures.Release(version: "1.0.0");
        _host.Mods.Listings.Add(listing);
        _host.Mods.Releases.AddRange(new[] { newest, older });
        _host.IndexReader.Snapshot = Snapshot(
            new ContentIndexListing(listing.ModId, listing, new[] { newest, older }, null, Counts()));

        var run = await _host.RunAsync("show", listing.ModId, "--json");

        Assert.Equal(0, run.ExitCode);
        var downloads = run.Json.GetProperty("downloads");
        Assert.Equal(1200, downloads.GetProperty("total").GetInt64());
        Assert.Equal(721, downloads.GetProperty("hosts").GetProperty("spacedock").GetInt64());
        var releases = run.Json.GetProperty("releases");
        Assert.Equal("2.0.0", releases[0].GetProperty("version").GetString());
        Assert.Equal(40, releases[0].GetProperty("downloads").GetProperty("total").GetInt64());
        Assert.Equal(17, releases[0].GetProperty("downloads").GetProperty("hosts").GetProperty("github").GetInt64());
        Assert.Equal("1.0.0", releases[1].GetProperty("version").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, releases[1].GetProperty("downloads").ValueKind);
    }

    [Fact]
    public async Task Show_UnknownDownloadCounts_OmitsTheLineAndWritesNullInJson()
    {
        var listing = ContentCommandFixtures.Listing();
        var release = ContentCommandFixtures.Release();
        _host.Mods.Listings.Add(listing);
        _host.Mods.Releases.Add(release);
        _host.IndexReader.Snapshot = Snapshot(new ContentIndexListing(listing.ModId, listing, new[] { release }, null));

        var run = await _host.RunAsync("show", listing.ModId);
        var json = await _host.RunAsync("show", listing.ModId, "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("Downloads", run.Output);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, json.Json.GetProperty("downloads").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, json.Json.GetProperty("releases")[0].GetProperty("downloads").ValueKind);
    }

    [Fact]
    public async Task ShowVersion_MalformedDownloads_PrintsTheReleaseAndTheDiagnostic()
    {
        var listing = ContentCommandFixtures.Listing();
        var release = ContentCommandFixtures.Release();
        _host.Mods.Listings.Add(listing);
        _host.Mods.Releases.Add(release);
        _host.IndexReader.Snapshot = Snapshot(
            new[] { new ContentIndexListing(listing.ModId, listing, new[] { release }, null) },
            new[]
            {
                new ContentIndexDiagnostic(
                    ContentIndexDiagnosticKind.Malformed,
                    ContentIndexDiagnosticScope.Downloads,
                    "The downloads value is unreadable. The downloads value must be an object, but was Array.",
                    listing.ModId),
            });

        var run = await _host.RunAsync("show", listing.ModId, "--version", "2.0.0");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("2.0.0  unknown  stable", run.Output);
        Assert.DoesNotContain("Downloads:", run.Output);
        Assert.Contains("malformed downloads flight-tools: The downloads value is unreadable.", run.Output);
    }

    [Fact]
    public async Task Show_YankedRelease_KeepsItsCount()
    {
        var listing = ContentCommandFixtures.Listing();
        var yanked = ContentCommandFixtures.Release(version: "1.0.0", yanked: true, yankedReason: "This release is broken.");
        var counts = new ListingDownloadCounts(
            60,
            new Dictionary<string, long> { ["github"] = 60 },
            new[] { new ReleaseDownloadCounts(ModVersion.Parse("1.0.0"), 60, new Dictionary<string, long> { ["github"] = 60 }) });
        _host.Mods.Listings.Add(listing);
        _host.Mods.Releases.Add(yanked);
        _host.IndexReader.Snapshot = Snapshot(new ContentIndexListing(listing.ModId, listing, new[] { yanked }, null, counts));

        var run = await _host.RunAsync("show", listing.ModId);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("1.0.0  unknown  stable  yanked: This release is broken.", run.Output);
        Assert.Contains("    Downloads: 60 (github 60)", run.Output);
    }

    [Fact]
    public async Task Show_Json_KeepsHostKeysAsTheIndexWritesThem()
    {
        var listing = ContentCommandFixtures.Listing();
        var counts = new ListingDownloadCounts(
            12,
            new Dictionary<string, long> { ["SpaceDock"] = 12 },
            Array.Empty<ReleaseDownloadCounts>());
        _host.Mods.Listings.Add(listing);
        _host.IndexReader.Snapshot = Snapshot(
            new ContentIndexListing(listing.ModId, listing, Array.Empty<ModVersionMetadata>(), null, counts));

        var run = await _host.RunAsync("show", listing.ModId, "--json");

        Assert.Equal(0, run.ExitCode);
        var hosts = run.Json.GetProperty("downloads").GetProperty("hosts");
        Assert.Equal(12, hosts.GetProperty("SpaceDock").GetInt64());
        Assert.False(hosts.TryGetProperty("spaceDock", out _));
    }

    [Fact]
    public async Task Show_UnknownReleaseSpecVersion_KeepsItsDownloadCount()
    {
        var listing = ContentCommandFixtures.Listing();
        var known = ContentCommandFixtures.Release(version: "2.0.0");
        _host.Mods.Listings.Add(listing);
        _host.Mods.Releases.Add(known);
        _host.IndexReader.Snapshot = Snapshot(
            new[] { new ContentIndexListing(listing.ModId, listing, new[] { known }, null, Counts()) },
            new[]
            {
                new ContentIndexDiagnostic(
                    ContentIndexDiagnosticKind.UnsupportedVersion,
                    ContentIndexDiagnosticScope.Release,
                    "Release spec version 2 is newer than this client.",
                    listing.ModId,
                    "0.9.0",
                    2),
            });

        var run = await _host.RunAsync("show", listing.ModId);
        var json = await _host.RunAsync("show", listing.ModId, "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("    Downloads: 7 (spacedock 7)", run.Output);
        var releases = json.Json.GetProperty("releases");
        Assert.Equal("0.9.0", releases[1].GetProperty("version").GetString());
        Assert.Equal("unknown", releases[1].GetProperty("state").GetString());
        Assert.Equal(7, releases[1].GetProperty("downloads").GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task ShowVersion_ReleaseCountDiagnostic_ShowsOnlyTheRequestedVersion()
    {
        var listing = ContentCommandFixtures.Listing();
        var release = ContentCommandFixtures.Release();
        _host.Mods.Listings.Add(listing);
        _host.Mods.Releases.Add(release);
        _host.IndexReader.Snapshot = Snapshot(
            new[] { new ContentIndexListing(listing.ModId, listing, new[] { release }, null) },
            new[]
            {
                new ContentIndexDiagnostic(
                    ContentIndexDiagnosticKind.Malformed,
                    ContentIndexDiagnosticScope.Downloads,
                    "The downloads releases item at index 0 is unreadable.",
                    listing.ModId,
                    "2.0.0"),
                new ContentIndexDiagnostic(
                    ContentIndexDiagnosticKind.Malformed,
                    ContentIndexDiagnosticScope.Downloads,
                    "The downloads releases item at index 1 is unreadable.",
                    listing.ModId,
                    "1.0.0"),
            });

        var run = await _host.RunAsync("show", listing.ModId, "--version", "2.0.0");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("malformed downloads flight-tools 2.0.0", run.Output);
        Assert.DoesNotContain("flight-tools 1.0.0", run.Output);
    }

    [Theory]
    [InlineData("not-a-version")]
    [InlineData("")]
    public async Task ShowVersion_InvalidVersion_IsAUsageError(string version)
    {
        var run = await _host.RunAsync("show", "flight-tools", "--version", version);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal(0, _host.Builds);
    }

    [Fact]
    public async Task Show_BothDates_PrintsThemAndWritesThemInJson()
    {
        var listing = ContentCommandFixtures.Listing();
        var release = ContentCommandFixtures.Release();
        var published = new DateTimeOffset(2026, 8, 2, 13, 18, 41, TimeSpan.Zero);
        var updated = new DateTimeOffset(2026, 9, 2, 10, 14, 5, TimeSpan.Zero);
        _host.Mods.Listings.Add(listing);
        _host.Mods.Releases.Add(release);
        _host.IndexReader.Snapshot = Snapshot(new ContentIndexListing(
            listing.ModId, listing, new[] { release }, null, publishedAt: published, updatedAt: updated));

        var run = await _host.RunAsync("show", listing.ModId);
        var json = await _host.RunAsync("show", listing.ModId, "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Published: 2026-08-02T13:18:41.0000000+00:00", run.Output);
        Assert.Contains("Updated: 2026-09-02T10:14:05.0000000+00:00", run.Output);
        Assert.Equal(published, json.Json.GetProperty("publishedAt").GetDateTimeOffset());
        Assert.Equal(updated, json.Json.GetProperty("updatedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Show_OnlyPublishedAt_OmitsTheUpdatedLine()
    {
        var listing = ContentCommandFixtures.Listing();
        _host.Mods.Listings.Add(listing);
        _host.IndexReader.Snapshot = Snapshot(new ContentIndexListing(
            listing.ModId,
            listing,
            Array.Empty<ModVersionMetadata>(),
            null,
            publishedAt: new DateTimeOffset(2026, 8, 2, 13, 18, 41, TimeSpan.Zero)));

        var run = await _host.RunAsync("show", listing.ModId);
        var json = await _host.RunAsync("show", listing.ModId, "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Published: 2026-08-02T13:18:41.0000000+00:00", run.Output);
        Assert.DoesNotContain("Updated:", run.Output);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, json.Json.GetProperty("updatedAt").ValueKind);
    }

    [Fact]
    public async Task Show_NoDates_OmitsBothLinesAndWritesNullInJson()
    {
        var listing = ContentCommandFixtures.Listing();
        _host.Mods.Listings.Add(listing);
        _host.IndexReader.Snapshot = Snapshot(new ContentIndexListing(listing.ModId, listing, Array.Empty<ModVersionMetadata>(), null));

        var run = await _host.RunAsync("show", listing.ModId);
        var json = await _host.RunAsync("show", listing.ModId, "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("Published:", run.Output);
        Assert.DoesNotContain("Updated:", run.Output);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, json.Json.GetProperty("publishedAt").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, json.Json.GetProperty("updatedAt").ValueKind);
    }

    [Theory]
    [InlineData(ContentIndexDiagnosticScope.Images, "The images icon is unreadable.", "malformed images flight-tools: The images icon is unreadable.")]
    [InlineData(ContentIndexDiagnosticScope.Dates, "The updated_at value is unreadable.", "malformed dates flight-tools: The updated_at value is unreadable.")]
    public async Task ShowVersion_ListingImagesOrDatesDiagnostic_IsPrinted(
        ContentIndexDiagnosticScope scope,
        string reason,
        string expected)
    {
        var listing = ContentCommandFixtures.Listing();
        var release = ContentCommandFixtures.Release();
        _host.Mods.Listings.Add(listing);
        _host.Mods.Releases.Add(release);
        _host.IndexReader.Snapshot = Snapshot(
            new[] { new ContentIndexListing(listing.ModId, listing, new[] { release }, null) },
            new[]
            {
                new ContentIndexDiagnostic(
                    ContentIndexDiagnosticKind.Malformed,
                    scope,
                    reason,
                    listing.ModId),
            });

        var run = await _host.RunAsync("show", listing.ModId, "--version", "2.0.0");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains(expected, run.Output);
    }

    [Fact]
    public async Task ShowVersion_ChangelogTextDiagnostic_ShowsOnlyTheRequestedVersion()
    {
        var listing = ContentCommandFixtures.Listing();
        var release = ContentCommandFixtures.Release();
        _host.Mods.Listings.Add(listing);
        _host.Mods.Releases.Add(release);
        _host.IndexReader.Snapshot = Snapshot(
            new[] { new ContentIndexListing(listing.ModId, listing, new[] { release }, null) },
            new[]
            {
                new ContentIndexDiagnostic(
                    ContentIndexDiagnosticKind.Malformed,
                    ContentIndexDiagnosticScope.ChangelogText,
                    "The changelog_text value must be a string.",
                    listing.ModId,
                    "2.0.0"),
                new ContentIndexDiagnostic(
                    ContentIndexDiagnosticKind.Malformed,
                    ContentIndexDiagnosticScope.ChangelogText,
                    "The changelog_text value must be a string.",
                    listing.ModId,
                    "1.0.0"),
            });

        var run = await _host.RunAsync("show", listing.ModId, "--version", "2.0.0");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("malformed changelog-text flight-tools 2.0.0: The changelog_text value must be a string.", run.Output);
        Assert.DoesNotContain("flight-tools 1.0.0", run.Output);
    }

    private static FakeInstalledGameVersionProvider Installed(string version) => new()
    {
        Installed = new InstalledGameVersion(GameVersion.Parse(version), version),
    };

    private static ListingDownloadCounts Counts() => new(
        1200,
        new Dictionary<string, long> { ["github"] = 479, ["spacedock"] = 721 },
        new[]
        {
            new ReleaseDownloadCounts(
                ModVersion.Parse("2.0.0"),
                40,
                new Dictionary<string, long> { ["github"] = 17, ["spacedock"] = 23 }),
            new ReleaseDownloadCounts(
                ModVersion.Parse("0.9.0"),
                7,
                new Dictionary<string, long> { ["spacedock"] = 7 }),
        });

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static ContentIndexSnapshot Snapshot(params ContentIndexListing[] listings) =>
        Snapshot(listings, Array.Empty<ContentIndexDiagnostic>());

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
