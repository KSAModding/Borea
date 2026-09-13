using Borea.Core.Index;
using Borea.Core.Mods;
using Borea.Storage.Index;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Index;

public sealed class DownloadCountsParserTests : IDisposable
{
    private const string FullDownloadsJson = """
        "downloads": {
            "total": 1200,
            "hosts": { "github": 479, "spacedock": 721 },
            "releases": [
                { "version": "2.0.0", "total": 40, "hosts": { "github": 17, "spacedock": 23 } },
                { "version": "1.0.0", "total": 60, "hosts": { "github": 20, "spacedock": 40 } }
            ]
        }
        """;

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;

    public DownloadCountsParserTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
    }

    [Fact]
    public void Parse_FullCounts_ReadsTotalHostsAndReleases()
    {
        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", FullDownloadsJson, "2.0.0", "1.0.0")));

        var listing = Assert.Single(result.ValidListings);
        Assert.Empty(listing.DownloadsErrors);
        var counts = listing.Downloads!;
        Assert.Equal(1200, counts.Total);
        Assert.Equal(479, counts.Hosts["github"]);
        Assert.Equal(721, counts.Hosts["spacedock"]);
        Assert.Equal(2, counts.Releases.Count);
        var release = counts.FindRelease(ModVersion.Parse("2.0.0"))!;
        Assert.Equal(40, release.Total);
        Assert.Equal(17, release.Hosts["github"]);
        Assert.Equal(2, listing.ValidReleases.Count);
    }

    [Fact]
    public void Parse_NoDownloads_LeavesTheCountsUnknown()
    {
        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", null, "1.0.0")));

        var listing = Assert.Single(result.ValidListings);
        Assert.Null(listing.Downloads);
        Assert.Empty(listing.DownloadsErrors);
        Assert.Single(listing.ValidReleases);
    }

    [Fact]
    public void Parse_OneHost_KeepsTheOtherHostUnknown()
    {
        var downloads = """
            "downloads": {
                "total": 721,
                "hosts": { "spacedock": 721 },
                "releases": [{ "version": "1.0.0", "total": 23, "hosts": { "spacedock": 23 } }]
            }
            """;

        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", downloads, "1.0.0")));

        var counts = Assert.Single(result.ValidListings).Downloads!;
        Assert.Equal(721, counts.Total);
        Assert.Equal(["spacedock"], counts.Hosts.Keys);
        Assert.False(counts.Hosts.ContainsKey("github"));
    }

    [Fact]
    public void Parse_ReleaseWithoutVersionEntry_LeavesThatReleaseUnknown()
    {
        var downloads = """
            "downloads": {
                "total": 50,
                "hosts": { "github": 50 },
                "releases": [{ "version": "2.0.0", "total": 40, "hosts": { "github": 40 } }]
            }
            """;

        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", downloads, "2.0.0", "1.0.0")));

        var listing = Assert.Single(result.ValidListings);
        Assert.Equal(2, listing.ValidReleases.Count);
        Assert.Equal(40, listing.Downloads!.FindRelease(ModVersion.Parse("2.0.0"))!.Total);
        Assert.Null(listing.Downloads.FindRelease(ModVersion.Parse("1.0.0")));
    }

    [Fact]
    public void Parse_NoReleasesArray_KeepsTheListingTotal()
    {
        var downloads = """
            "downloads": { "total": 5, "hosts": { "github": 5 } }
            """;

        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", downloads, "1.0.0")));

        var counts = Assert.Single(result.ValidListings).Downloads!;
        Assert.Equal(5, counts.Total);
        Assert.Empty(counts.Releases);
    }

    [Fact]
    public void Parse_UnknownHostKeysAndField_KeepTheHostValuesAndIgnoreTheField()
    {
        var downloads = """
            "downloads": {
                "total": 15,
                "hosts": { "github": 5, "future-host": 10 },
                "future_field": true,
                "releases": [{ "version": "1.0.0", "total": 3, "hosts": { "future-host": 2, "github": 1 }, "future_field": 1 }]
            }
            """;

        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", downloads, "1.0.0")));

        var listing = Assert.Single(result.ValidListings);
        Assert.Empty(listing.DownloadsErrors);
        Assert.Equal(10, listing.Downloads!.Hosts["future-host"]);
        Assert.Equal(2, listing.Downloads.FindRelease(ModVersion.Parse("1.0.0"))!.Hosts["future-host"]);
    }

    [Fact]
    public void Parse_ReleaseHostWithoutAListingHost_IsValid()
    {
        var downloads = """
            "downloads": {
                "total": 5,
                "hosts": { "github": 5 },
                "releases": [{ "version": "1.0.0", "total": 9, "hosts": { "spacedock": 9 } }]
            }
            """;

        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", downloads, "1.0.0")));

        var listing = Assert.Single(result.ValidListings);
        Assert.Empty(listing.DownloadsErrors);
        Assert.Equal(9, listing.Downloads!.FindRelease(ModVersion.Parse("1.0.0"))!.Hosts["spacedock"]);
    }

    [Fact]
    public void Parse_MalformedDownloadsBesideAValidSibling_KeepsBothListingsAndDropsOnlyTheBadCounts()
    {
        var malformed = """
            "downloads": { "total": 1000, "hosts": { "github": 479, "spacedock": 721 } }
            """;
        var listings = Listing("broken-counts", malformed, "1.0.0") + "," + Listing("test-mod", FullDownloadsJson, "2.0.0", "1.0.0");

        var result = SnapshotParser.Parse(Snapshot(listings));

        Assert.Empty(result.MalformedListings);
        Assert.Equal(2, result.ValidListings.Count);
        var broken = result.ValidListings.Single(listing => listing.Id == "broken-counts");
        Assert.Null(broken.Downloads);
        Assert.Single(broken.ValidReleases);
        Assert.NotNull(broken.Authored);
        var error = Assert.Single(broken.DownloadsErrors);
        Assert.Equal("broken-counts", error.Id);
        Assert.Contains("not the sum", error.Reason);
        var sibling = result.ValidListings.Single(listing => listing.Id == "test-mod");
        Assert.Equal(1200, sibling.Downloads!.Total);
        Assert.Empty(sibling.DownloadsErrors);
    }

    [Fact]
    public void Parse_Tombstone_CarriesNoCounts()
    {
        var tombstone = """
            {
                "id": "removed-mod",
                "index_status": { "state": "delisted", "since": "2026-08-10T00:00:00Z" },
                "downloads": { "total": 5, "hosts": { "github": 5 } }
            }
            """;

        var result = SnapshotParser.Parse(Snapshot(tombstone));

        var listing = Assert.Single(result.ValidListings);
        Assert.Null(listing.Authored);
        Assert.Null(listing.Downloads);
        Assert.Empty(listing.DownloadsErrors);
    }

    [Theory]
    [InlineData("\"downloads\": []", "must be an object")]
    [InlineData("\"downloads\": null", "must be an object")]
    [InlineData("\"downloads\": { \"hosts\": { \"github\": 5 } }", "must contain total")]
    [InlineData("\"downloads\": { \"total\": 5 }", "must contain hosts")]
    [InlineData("\"downloads\": { \"total\": \"5\", \"hosts\": { \"github\": 5 } }", "whole number")]
    [InlineData("\"downloads\": { \"total\": 1.5, \"hosts\": { \"github\": 1.5 } }", "whole number")]
    [InlineData("\"downloads\": { \"total\": -5, \"hosts\": { \"github\": -5 } }", "cannot be negative")]
    [InlineData("\"downloads\": { \"total\": 0, \"hosts\": {} }", "at least one host")]
    [InlineData("\"downloads\": { \"total\": 10, \"hosts\": { \"github\": 5, \"github\": 5 } }", "more than once")]
    [InlineData("\"downloads\": { \"total\": 4, \"hosts\": { \"github\": 5 }, \"releases\": [{ \"version\": \"1.0.0\", \"total\": 5, \"hosts\": { \"github\": 5 } }] }", "not the sum")]
    public void Parse_InvalidListingCounts_DropsTheWholeValueAndKeepsTheListing(string downloads, string reason)
    {
        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", downloads, "1.0.0")));

        var listing = Assert.Single(result.ValidListings);
        Assert.Null(listing.Downloads);
        Assert.Single(listing.ValidReleases);
        var error = Assert.Single(listing.DownloadsErrors);
        Assert.Null(error.Version);
        Assert.Contains("The downloads value is unreadable.", error.Reason);
        Assert.Contains(reason, error.Reason);
    }

    [Theory]
    [InlineData("{ \"version\": \"v1\", \"total\": 5, \"hosts\": { \"github\": 5 } }", "v1", "not a valid semantic version")]
    [InlineData("{ \"version\": \"1.0.0\", \"total\": 4, \"hosts\": { \"github\": 5 } }", "1.0.0", "not the sum")]
    [InlineData("{ \"version\": \"1.0.0\", \"total\": 5, \"hosts\": { \"github\": -5 } }", "1.0.0", "cannot be negative")]
    [InlineData("{ \"version\": \"1.0.0\", \"total\": 5 }", "1.0.0", "must contain hosts")]
    [InlineData("{ \"total\": 5, \"hosts\": { \"github\": 5 } }", null, "must contain a string version")]
    [InlineData("7", null, "must be an object")]
    public void Parse_InvalidReleaseEntry_DropsOnlyThatEntry(string badEntry, string? version, string reason)
    {
        var downloads = $$"""
            "downloads": {
                "total": 1200,
                "hosts": { "github": 479, "spacedock": 721 },
                "releases": [{ "version": "2.0.0", "total": 40, "hosts": { "github": 17, "spacedock": 23 } }, {{badEntry}}]
            }
            """;

        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", downloads, "2.0.0", "1.0.0")));

        var listing = Assert.Single(result.ValidListings);
        Assert.Equal(1200, listing.Downloads!.Total);
        Assert.Equal(40, Assert.Single(listing.Downloads.Releases).Total);
        Assert.Null(listing.Downloads.FindRelease(ModVersion.Parse("1.0.0")));
        var error = Assert.Single(listing.DownloadsErrors);
        Assert.Equal("test-mod", error.Id);
        Assert.Equal(version, error.Version);
        Assert.Contains("The downloads releases item at index 1 is unreadable.", error.Reason);
        Assert.Contains(reason, error.Reason);
    }

    [Fact]
    public void Parse_ReleasesNotAnArray_KeepsTheListingTotalWithoutVersionCounts()
    {
        var downloads = """
            "downloads": { "total": 5, "hosts": { "github": 5 }, "releases": {} }
            """;

        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", downloads, "1.0.0")));

        var listing = Assert.Single(result.ValidListings);
        Assert.Equal(5, listing.Downloads!.Total);
        Assert.Empty(listing.Downloads.Releases);
        Assert.Contains("must be an array", Assert.Single(listing.DownloadsErrors).Reason);
    }

    [Fact]
    public void Parse_DuplicateReleaseVersion_DropsEveryEntryOfThatVersion()
    {
        var downloads = """
            "downloads": {
                "total": 6,
                "hosts": { "github": 6 },
                "releases": [
                    { "version": "2.0.0", "total": 3, "hosts": { "github": 3 } },
                    { "version": "1.0.0", "total": 1, "hosts": { "github": 1 } },
                    { "version": "1.0.0", "total": 2, "hosts": { "github": 2 } }
                ]
            }
            """;

        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", downloads, "2.0.0", "1.0.0")));

        var listing = Assert.Single(result.ValidListings);
        Assert.Equal(6, listing.Downloads!.Total);
        Assert.Equal(3, Assert.Single(listing.Downloads.Releases).Total);
        Assert.Equal(2, listing.DownloadsErrors.Count);
        Assert.All(listing.DownloadsErrors, error =>
        {
            Assert.Equal("1.0.0", error.Version);
            Assert.Contains("more than one download count", error.Reason);
        });
    }

    [Fact]
    public async Task ReadAsync_DownloadCounts_AttachToTheListing()
    {
        await WriteIndexAsync(Snapshot(Listing("test-mod", FullDownloadsJson, "2.0.0", "1.0.0")));

        var snapshot = await new ContentIndexReader(_paths).ReadAsync();

        var listing = Assert.Single(snapshot.Listings);
        Assert.Equal(1200, listing.Downloads!.Total);
        Assert.Equal(60, listing.Downloads.FindRelease(ModVersion.Parse("1.0.0"))!.Total);
        Assert.Empty(snapshot.Diagnostics);
    }

    [Fact]
    public async Task ReadAsync_MalformedDownloads_KeepsTheListingAndAddsADiagnostic()
    {
        await WriteIndexAsync(Snapshot(Listing("test-mod", "\"downloads\": 12", "1.0.0")));

        var snapshot = await new ContentIndexReader(_paths).ReadAsync();

        var listing = Assert.Single(snapshot.Listings);
        Assert.Null(listing.Downloads);
        Assert.Single(listing.Releases);
        var diagnostic = Assert.Single(snapshot.Diagnostics);
        Assert.Equal(ContentIndexDiagnosticKind.Malformed, diagnostic.Kind);
        Assert.Equal(ContentIndexDiagnosticScope.Downloads, diagnostic.Scope);
        Assert.Equal("test-mod", diagnostic.Id);
        Assert.Null(diagnostic.Version);
    }

    [Fact]
    public async Task ReadAsync_MalformedReleaseEntry_KeepsTheTotalAndAddsAVersionDiagnostic()
    {
        var downloads = """
            "downloads": {
                "total": 5,
                "hosts": { "github": 5 },
                "releases": [{ "version": "1.0.0", "total": 4, "hosts": { "github": 5 } }]
            }
            """;
        await WriteIndexAsync(Snapshot(Listing("test-mod", downloads, "1.0.0")));

        var snapshot = await new ContentIndexReader(_paths).ReadAsync();

        Assert.Equal(5, Assert.Single(snapshot.Listings).Downloads!.Total);
        var diagnostic = Assert.Single(snapshot.Diagnostics);
        Assert.Equal(ContentIndexDiagnosticScope.Downloads, diagnostic.Scope);
        Assert.Equal("1.0.0", diagnostic.Version);
    }

    private static string Listing(string id, string? downloads, params string[] versions)
    {
        var releases = string.Join(",", versions.Select(version => Release(id, version)));
        var suffix = downloads is null ? string.Empty : "," + downloads;
        return $$"""
            {
                "id": "{{id}}",
                "authored": {
                    "spec_version": 1,
                    "id": "{{id}}",
                    "type": "mod",
                    "name": "Test Mod",
                    "authors": ["Test Author"],
                    "abstract": "A mod used for testing.",
                    "license": "MIT",
                    "compatibility": { "game_min": "2026.7.4.2131" },
                    "links": { "forums": "https://forums.example/thread/1" }
                },
                "releases": [{{releases}}]{{suffix}}
            }
            """;
    }

    private static string Release(string id, string version) => $$"""
        {
            "spec_version": 1,
            "id": "{{id}}",
            "type": "mod",
            "version": "{{version}}",
            "version_scheme": "semver",
            "release_status": "stable",
            "release_date": "2026-08-08T12:00:00Z",
            "game_min": "2026.7.4.2131",
            "game_min_revision": 2131,
            "download": { "url": "https://example.com/mod.zip", "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "size": 1024, "content_type": "application/zip" },
            "install_size": 2048,
            "dependencies": []
        }
        """;

    private static string Snapshot(string listings) => $$"""
        {
            "snapshot_version": 1,
            "listings": [{{listings}}],
            "packs": [],
            "game_versions": { "spec_version": 1, "source": "master-server", "versions": ["2026.9.7.5402"] }
        }
        """;

    private async Task WriteIndexAsync(string content)
    {
        var path = _paths.GetIndexPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
