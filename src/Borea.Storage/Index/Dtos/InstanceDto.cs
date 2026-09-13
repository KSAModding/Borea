using System.Text.Json;
using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

public sealed class InstanceDto
{
    [JsonPropertyName("flag")]
    public string? Flag { get; set; }

    [JsonPropertyName("variable")]
    public string? Variable { get; set; }

    /// <summary>
    /// Fields that this Dto doesn't know. Collects so unknown fields
    /// reject this mod-loader listing, because the table is closed and
    /// an ignored key could hand the instance to the loader the wrong way
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownFields { get; set; }
}
