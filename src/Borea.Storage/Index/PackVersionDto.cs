using System.Text.Json.Serialization;

namespace Borea.Storage.Index;

public sealed class PackVersionDto
{
    [JsonPropertyName("authored")]
    public required PackAuthoredDto Authored { get; set; }

    [JsonPropertyName("index_status")]
    public IndexStatusDto? IndexStatus { get; set; }
}
