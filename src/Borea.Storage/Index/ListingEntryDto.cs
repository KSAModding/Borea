using System.Text.Json;
using System.Text.Json.Serialization;

namespace Borea.Storage.Index;

public sealed class ListingEntryDto
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("authored")]
    public AuthoredDto? Authored { get; set; }

    /// <summary>
    /// Storing <see cref="JsonElement"/> to not throw if
    /// one release is bad
    /// </summary>
    [JsonPropertyName("releases")]
    public List<JsonElement>? Releases { get; set; }

    [JsonPropertyName("index_status")]
    public IndexStatusDto? IndexStatus { get; set; }
}
