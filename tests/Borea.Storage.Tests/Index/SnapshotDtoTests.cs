using System.Text.Json;
using Borea.Storage.Index.Dtos;

namespace Borea.Storage.Tests.Index;

public sealed class SnapshotDtoTests
{
    [Fact]
    public void Deserialize_CurrentSnapshot_PreservesTheRootEnvelopeAndRawEntries()
    {
        var snapshot = IndexJson.Deserialize<SnapshotDto>(LoadCurrentSnapshot());

        Assert.Equal(1, snapshot.SnapshotVersion);
        Assert.Equal("e7759e6304d38392eecd89392f3da5139406dcb7", snapshot.Sources!.Authored!.Commit);
        Assert.Equal("83331326cd7471a859fcd0117af0345b99cb5bf5", snapshot.Sources.Generated!.Commit);
        Assert.Equal(4, snapshot.Listings.Count);
        Assert.Empty(snapshot.Packs);
        Assert.Equal("2026.9.7.5402", snapshot.GameVersions.Versions[^1]);

        var ids = snapshot.Listings
            .Select(listing => listing.GetProperty("id").GetString())
            .ToArray();
        Assert.Equal(new[] { "AdvancedFlightComputer", "KSArmory", "MeasureTools", "StarMap" }, ids);

        var releaseCount = snapshot.Listings
            .Sum(listing => listing.GetProperty("releases").GetArrayLength());
        Assert.Equal(10, releaseCount);
    }

    [Fact]
    public void Deserialize_CurrentSnapshot_DeserializesEveryListingAndRelease()
    {
        var snapshot = IndexJson.Deserialize<SnapshotDto>(LoadCurrentSnapshot());

        foreach (var listingElement in snapshot.Listings)
        {
            var listing = IndexJson.Deserialize<ListingEntryDto>(listingElement);
            foreach (var releaseElement in listing.Releases ?? [])
                IndexJson.Deserialize<ReleasesEntryDto>(releaseElement);
        }
    }

    [Fact]
    public void Deserialize_MalformedJson_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => IndexJson.Deserialize<SnapshotDto>("{"));
    }

    [Fact]
    public void Deserialize_ArrayRoot_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => IndexJson.Deserialize<SnapshotDto>("[]"));
    }

    [Fact]
    public void Deserialize_MissingRequiredEnvelopeMember_ThrowsJsonException()
    {
        const string json = """
            {
              "snapshot_version": 1,
              "listings": [],
              "game_versions": {
                "spec_version": 1,
                "source": "https://example.test/versions",
                "versions": []
              }
            }
            """;

        Assert.Throws<JsonException>(() => IndexJson.Deserialize<SnapshotDto>(json));
    }

    [Fact]
    public void Deserialize_NullRequiredEnvelopeMember_ThrowsJsonException()
    {
        const string json = """
            {
              "snapshot_version": 1,
              "listings": null,
              "packs": [],
              "game_versions": {
                "spec_version": 1,
                "source": "https://example.test/versions",
                "versions": []
              }
            }
            """;

        Assert.Throws<JsonException>(() => IndexJson.Deserialize<SnapshotDto>(json));
    }

    [Fact]
    public void Deserialize_MissingGameVersions_ThrowsJsonException()
    {
        const string json = """
            {
              "snapshot_version": 1,
              "listings": [],
              "packs": []
            }
            """;

        Assert.Throws<JsonException>(() => IndexJson.Deserialize<SnapshotDto>(json));
    }

    [Fact]
    public void Deserialize_NullGameVersions_ThrowsJsonException()
    {
        const string json = """
            {
              "snapshot_version": 1,
              "listings": [],
              "packs": [],
              "game_versions": null
            }
            """;

        Assert.Throws<JsonException>(() => IndexJson.Deserialize<SnapshotDto>(json));
    }

    [Fact]
    public void Deserialize_InvalidGameVersions_ThrowsJsonException()
    {
        const string json = """
            {
              "snapshot_version": 1,
              "listings": [],
              "packs": [],
              "game_versions": {
                "spec_version": 1,
                "source": "https://example.test/versions",
                "versions": [1]
              }
            }
            """;

        Assert.Throws<JsonException>(() => IndexJson.Deserialize<SnapshotDto>(json));
    }

    private static string LoadCurrentSnapshot()
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Index", "Fixtures", "current-snapshot.json"));
}
