using System.Text.Json.Serialization;

namespace Borea.Storage.Index;

public sealed class ConfigureDto
{
    [JsonPropertyName("file")]
    public required string File { get; set; }

    [JsonPropertyName("format")]
    public required string Format { get; set; }

    [JsonPropertyName("game-path")]
    public string? GamePath { get; set; }
}
