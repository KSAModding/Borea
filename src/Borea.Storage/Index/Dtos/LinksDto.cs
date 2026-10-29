using System.Text.Json;
using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

public class LinksDto
{
    /// <summary>Absent only on a vehicle or save, which the domain types check.</summary>
    [JsonPropertyName("forums")]
    public string? Forums { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? OtherLinks { get; set; }
}
