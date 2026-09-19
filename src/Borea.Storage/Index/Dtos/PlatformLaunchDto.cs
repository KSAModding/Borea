using System.Text.Json;
using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

public sealed class PlatformLaunchDto
{
    [JsonPropertyName("launch")]
    public required string Launch { get; set; }

    [JsonPropertyName("runtime")]
    public string? Runtime { get; set; }

    /// <summary>
    /// Fields that this Dto doesn't know. Kept so the launch on this platform
    /// can refuse them, while other platforms still start
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownFields { get; set; }
}
