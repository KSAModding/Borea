using System.Text.Json;
using Borea.Storage.Index.Dtos;

namespace Borea.Storage.Tests.Index;

public sealed class ListingEntryDtoTests
{
    [Fact]
    public void Deserialize_BadListingBesideTombstone_PreservesBothForEntryValidation()
    {
        const string json = """
            {
              "snapshot_version": 1,
              "listings": [
                {
                  "id": "RemovedMod",
                  "index_status": { "state": "delisted" }
                },
                {
                  "id": "BrokenMod",
                  "authored": { "spec_version": "invalid" }
                }
              ],
              "packs": [],
              "game_versions": {
                "spec_version": 1,
                "source": "https://example.test/versions",
                "versions": []
              }
            }
            """;

        var snapshot = IndexJson.Deserialize<SnapshotDto>(json);

        Assert.Equal(2, snapshot.Listings.Count);
        var tombstone = IndexJson.Deserialize<ListingEntryDto>(snapshot.Listings[0]);
        Assert.Equal("RemovedMod", tombstone.Id);
        Assert.Equal("delisted", tombstone.IndexStatus!.Value.GetProperty("state").GetString());
        Assert.Null(tombstone.Authored);
        Assert.Null(tombstone.Releases);
        Assert.Throws<JsonException>(() => IndexJson.Deserialize<ListingEntryDto>(snapshot.Listings[1]));
        Assert.Equal("BrokenMod", snapshot.Listings[1].GetProperty("id").GetString());
    }

    [Fact]
    public void Deserialize_EmptyReleaseList_IsValid()
    {
        const string json = """
            {
              "id": "WaitingForRelease",
              "releases": []
            }
            """;

        var listing = IndexJson.Deserialize<ListingEntryDto>(json);

        Assert.Equal("WaitingForRelease", listing.Id);
        Assert.NotNull(listing.Releases);
        Assert.Empty(listing.Releases);
    }

    [Fact]
    public void Deserialize_UnknownIndexStatus_PreservesTheValue()
    {
        const string json = """
            {
              "id": "WarningMod",
              "index_status": { "state": "future-warning" }
            }
            """;

        var listing = IndexJson.Deserialize<ListingEntryDto>(json);

        Assert.Equal("future-warning", listing.IndexStatus!.Value.GetProperty("state").GetString());
    }

    [Fact]
    public void Deserialize_BadReleaseBesideGoodRelease_PreservesBothForReleaseValidation()
    {
        const string json = """
            {
              "id": "ReleaseBoundary",
              "releases": [
                {
                  "spec_version": 1,
                  "id": "ReleaseBoundary",
                  "type": "mod",
                  "version": "1.0.0",
                  "version_scheme": "semver",
                  "release_status": "stable",
                  "release_date": "2026-09-10T00:00:00Z",
                  "game_min": "2026.9.7.5402",
                  "game_min_revision": 5402,
                  "download": {
                    "url": "https://example.test/ReleaseBoundary.zip",
                    "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                    "size": 1,
                    "content_type": "application/zip"
                  },
                  "install_size": 1,
                  "dependencies": []
                },
                "invalid release"
              ]
            }
            """;

        var listing = IndexJson.Deserialize<ListingEntryDto>(json);

        Assert.Equal(2, listing.Releases!.Count);
        var release = IndexJson.Deserialize<ReleasesEntryDto>(listing.Releases[0]);
        Assert.Equal("1.0.0", release.Version);
        Assert.Throws<JsonException>(() => IndexJson.Deserialize<ReleasesEntryDto>(listing.Releases[1]));
    }

    [Fact]
    public void Deserialize_NullRequiredReleaseMember_FailsInsideTheReleaseBoundary()
    {
        const string json = """
            {
              "id": "ReleaseBoundary",
              "releases": [
                {
                  "spec_version": 1,
                  "id": "ReleaseBoundary",
                  "type": "mod",
                  "version": "1.0.0",
                  "version_scheme": "semver",
                  "release_status": "stable",
                  "release_date": "2026-09-10T00:00:00Z",
                  "game_min": "2026.9.7.5402",
                  "game_min_revision": 5402,
                  "download": null,
                  "install_size": 1,
                  "dependencies": []
                }
              ]
            }
            """;

        var listing = IndexJson.Deserialize<ListingEntryDto>(json);

        var releases = listing.Releases!;
        Assert.Single(releases);
        Assert.Throws<JsonException>(() => IndexJson.Deserialize<ReleasesEntryDto>(releases[0]));
    }
}
