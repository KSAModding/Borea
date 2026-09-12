using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

public sealed class LoaderDto
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("min")]
    public required string Min { get; set; }

    [JsonPropertyName("max")]
    public string? Max { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }
}
