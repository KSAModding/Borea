using System.Text.Json;
using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

public sealed class ReleasesInfoDto
{
    [JsonPropertyName("authority")]
    public string? Authority { get; set; }

    // An element and not a string, so a since that is not a version cannot fail the whole listing.
    [JsonPropertyName("since")]
    public JsonElement? Since { get; set; }

    // Named here, because a prefix such as "release/v" has a slash and would otherwise read as a repository.
    [JsonPropertyName("tag_prefix")]
    public JsonElement? TagPrefix { get; set; }

    /// <summary>Every other key. Only the ones whose value is a host reference are release hosts.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Hosts { get; set; }
}
