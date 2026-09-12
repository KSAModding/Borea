using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

public sealed class SourcesDto
{
    [JsonPropertyName("authored")]
    public SourceRefDto? Authored { get; set; }

    [JsonPropertyName("generated")]
    public SourceRefDto? Generated { get; set; }
}
