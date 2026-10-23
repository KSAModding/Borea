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
        Assert.Equal(
            "2026.9.7.5402",
            snapshot.GameVersions.GetProperty("versions").EnumerateArray().Last().GetString());

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

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("""{ "snapshot_version": 1, "listings": [], "game_versions": { "spec_version": 1, "source": "https://example.test/versions", "versions": [] } }""")]
    [InlineData("""{ "snapshot_version": 1, "listings": null, "packs": [], "game_versions": { "spec_version": 1, "source": "https://example.test/versions", "versions": [] } }""")]
    [InlineData("""{ "snapshot_version": 1, "listings": [], "packs": [] }""")]
    public void Deserialize_UnusableEnvelope_ThrowsJsonException(string json)
    {
        Assert.Throws<JsonException>(() => IndexJson.Deserialize<SnapshotDto>(json));
    }

    [Theory]
    [InlineData("null", JsonValueKind.Null)]
    [InlineData("""{ "spec_version": 1, "source": "https://example.test/versions", "versions": [1] }""", JsonValueKind.Object)]
    public void Deserialize_UnusableGameVersions_KeepsRawValue(string gameVersions, JsonValueKind kind)
    {
        var json = $$"""{ "snapshot_version": 1, "listings": [], "packs": [], "game_versions": {{gameVersions}} }""";

        var result = IndexJson.Deserialize<SnapshotDto>(json);

        Assert.Equal(kind, result.GameVersions.ValueKind);
    }

    private static string LoadCurrentSnapshot()
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Index", "Fixtures", "current-snapshot.json"));
}
