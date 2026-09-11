using System.Text.Json;
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

    /// <summary>
    /// Fields that this Dto doesn't know. Collects so unknown fields
    /// reject this mod-loader listing as an unknown field may cause
    /// the loader to work improperly
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownFields { get; set; }
}
