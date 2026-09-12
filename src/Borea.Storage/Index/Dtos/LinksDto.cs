using System.Text.Json;
using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

public class LinksDto
{
    [JsonPropertyName("forums")]
    public required string Forums { get; set; }

    /// <summary>
    /// Uses JsonExtensionData to catch any other objects/links in this objects.
    /// The string is the name, JsonElement stores the value
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? OtherLinks { get; set; }
}
