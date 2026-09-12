using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

public sealed class InstallInfoDto
{
    [JsonPropertyName("root")]
    public string? Root { get; set; }

    [JsonPropertyName("derived")]
    public required bool Derived { get; set; }

    [JsonPropertyName("target")]
    public string? Target { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }
}
