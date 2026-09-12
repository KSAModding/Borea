using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

public sealed class IndexModPackItemEntryDto
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("version")]
    public required string Version { get; set; }
}
