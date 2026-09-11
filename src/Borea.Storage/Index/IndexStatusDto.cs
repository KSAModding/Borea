using System.Text.Json.Serialization;

namespace Borea.Storage.Index;

public sealed class IndexStatusDto
{
    [JsonPropertyName("state")]
    public required string State { get; set; }

    [JsonPropertyName("since")]
    public string? Since { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}
