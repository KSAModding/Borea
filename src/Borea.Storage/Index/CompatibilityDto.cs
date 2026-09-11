using System.Text.Json.Serialization;

namespace Borea.Storage.Index;

public sealed class CompatibilityDto
{
    [JsonPropertyName("game_min")]
    public required string GameMin { get; set; }

    [JsonPropertyName("game_max")]
    public string? GameMax { get; set; }

    [JsonPropertyName("os")]
    public List<string>? Os { get; set; }
}
