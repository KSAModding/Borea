using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

public sealed class PackVersionDto
{
    [JsonPropertyName("authored")]
    public required PackAuthoredDto Authored { get; set; }

    [JsonPropertyName("index_status")]
    public System.Text.Json.JsonElement? IndexStatus { get; set; }
}
