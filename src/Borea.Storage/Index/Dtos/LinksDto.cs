using System.Text.Json;
using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

public class LinksDto
{
    [JsonPropertyName("forums")]
    public required string Forums { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? OtherLinks { get; set; }
}
