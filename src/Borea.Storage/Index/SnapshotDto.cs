using System.Text.Json;
using System.Text.Json.Serialization;

namespace Borea.Storage.Index;

public sealed class SnapshotDto
{
    [JsonPropertyName("snapshot_version")]
    public required int SnapshotVersion { get; set; }

    [JsonPropertyName("sources")]
    public SourcesDto? Sources { get; set; }

    /// <summary>
    /// Stores <see cref="JsonElement"/> instead of <see cref="ListingEntryDto"/>
    /// because storing <see cref="ListingEntryDto"/> here would cause any unparseable
    /// listing to throw an exception stopping the validation for the whole index.
    /// </summary>
    [JsonPropertyName("listings")]
    public required List<JsonElement> Listings { get; set; }

    /// <summary>
    /// Same reason as Listings
    /// </summary>
    [JsonPropertyName("packs")]
    public required List<JsonElement> Packs { get; set; }

    /// <summary>
    /// Chose to let the serializer throw if GameVersions is incorrect
    /// </summary>
    [JsonPropertyName("game_versions")]
    public required GameVersionsDto GameVersions { get; set; }
}
