using System.Text.Json.Serialization;

namespace Borea.Storage.Index;

public sealed class ReleaseListingDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("authors")]
    public List<string>? Authors { get; set; }

    [JsonPropertyName("abstract")]
    public string? Abstract { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("links")]
    public LinksDto? Links { get; set; }
}
