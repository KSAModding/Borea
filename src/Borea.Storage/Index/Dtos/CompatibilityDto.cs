using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

public sealed class CompatibilityDto
{
    /// <summary>Absent only on a vehicle or save, which ModMetadata and ModPackMetadata check.</summary>
    [JsonPropertyName("game_min")]
    public string? GameMin { get; set; }

    [JsonPropertyName("game_max")]
    public string? GameMax { get; set; }

    [JsonPropertyName("os")]
    public List<string>? Os { get; set; }
}
