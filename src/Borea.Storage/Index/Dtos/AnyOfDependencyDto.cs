using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

public sealed class AnyOfDependencyDto
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("min")]
    public string? Min { get; set; }

    [JsonPropertyName("max")]
    public string? Max { get; set; }
}
