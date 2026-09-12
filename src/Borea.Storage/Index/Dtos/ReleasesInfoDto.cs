using System.Text.Json;
using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

public sealed class ReleasesInfoDto
{
    [JsonPropertyName("authority")]
    public string? Authority { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Hosts { get; set; }
}
