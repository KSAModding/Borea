using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

public sealed class DependencyEntryDto
{
    // Nullable since AnyOf replaces Id
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("kind")]
    public required string Kind { get; set; }

    [JsonPropertyName("min")]
    public string? Min { get; set; }

    [JsonPropertyName("max")]
    public string? Max { get; set; }

    [JsonPropertyName("any_of")]
    public List<AnyOfDependencyDto>? AnyOf { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }
}
