using System.Text.Json;
using Borea.Storage.Index.Dtos;

namespace Borea.Storage.Tests.Index;

public sealed class PackEntryDtoTests
{
    [Fact]
    public void Deserialize_BadPackVersion_PreservesThePackAndVersionBoundary()
    {
        const string json = """
            {
              "snapshot_version": 1,
              "listings": [],
              "packs": [
                {
                  "id": "BrokenPackVersion",
                  "versions": [
                    {
                      "authored": { "spec_version": "invalid" },
                      "index_status": { "state": "retracted" }
                    }
                  ]
                }
              ],
              "game_versions": {
                "spec_version": 1,
                "source": "https://example.test/versions",
                "versions": []
              }
            }
            """;

        var snapshot = IndexJson.Deserialize<SnapshotDto>(json);
        var pack = IndexJson.Deserialize<PackEntryDto>(snapshot.Packs[0]);

        Assert.Equal("BrokenPackVersion", pack.Id);
        var versions = pack.Versions!;
        Assert.Single(versions);
        Assert.Equal(JsonValueKind.Object, versions[0].ValueKind);
        Assert.Equal("retracted", versions[0].GetProperty("index_status").GetProperty("state").GetString());
        Assert.Throws<JsonException>(() => IndexJson.Deserialize<PackVersionDto>(versions[0]));
    }

    [Fact]
    public void Deserialize_BadPackBesideTombstone_PreservesBothForEntryValidation()
    {
        const string json = """
            {
              "snapshot_version": 1,
              "listings": [],
              "packs": [
                {
                  "id": "RemovedPack",
                  "index_status": { "state": "delisted" }
                },
                "invalid pack"
              ],
              "game_versions": {
                "spec_version": 1,
                "source": "https://example.test/versions",
                "versions": []
              }
            }
            """;

        var snapshot = IndexJson.Deserialize<SnapshotDto>(json);

        Assert.Equal(2, snapshot.Packs.Count);
        var tombstone = IndexJson.Deserialize<PackEntryDto>(snapshot.Packs[0]);
        Assert.Equal("RemovedPack", tombstone.Id);
        Assert.Equal("delisted", tombstone.IndexStatus!.State);
        Assert.Throws<JsonException>(() => IndexJson.Deserialize<PackEntryDto>(snapshot.Packs[1]));
    }
}
