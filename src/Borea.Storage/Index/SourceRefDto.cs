using System.Text.Json.Serialization;

namespace Borea.Storage.Index;

public sealed class SourceRefDto
{
    [JsonPropertyName("repository")]
    public string? Repository { get; set; }

    [JsonPropertyName("commit")]
    public string? Commit { get; set; }
}
