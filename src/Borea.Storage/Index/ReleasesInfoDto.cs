using System.Text.Json;
using System.Text.Json.Serialization;

namespace Borea.Storage.Index;

public sealed class ReleasesInfoDto
{
    // If exists, must have an authority
    [JsonPropertyName("authority")]
    public required string Authority { get; set; }

    // if exists, must have at least one other element besides authority
    [JsonExtensionData]
    public required Dictionary<string, JsonElement> Hosts { get; set; }
}
