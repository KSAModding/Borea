using System.Text.Json.Serialization;

namespace Borea.Storage.Index;

public sealed class ReleaseListingDto
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("authors")]
    public required List<string> Authors { get; set; }

    [JsonPropertyName("abstract")]
    public required string Abstract { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("license")]
    public required string License { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("links")]
    public required LinksDto Links { get; set; }
}
