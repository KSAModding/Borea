using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

public sealed class GameVersionsDto
{
    [JsonPropertyName("spec_version")]
    public required int SpecVersion { get; set; }

    [JsonPropertyName("source")]
    public required string Source { get; set; }

    [JsonPropertyName("versions")]
    public required List<string> Versions { get; set; }
}

