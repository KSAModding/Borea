using System.Text.Json;
using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

public sealed class PackEntryDto
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    // Uses PackVersionDto
    [JsonPropertyName("versions")]
    public List<JsonElement>? Versions { get; set; }

    [JsonPropertyName("index_status")]
    public IndexStatusDto? IndexStatus { get; set; }
}
