using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

public sealed class InstallDescriptorDto
{
    [JsonPropertyName("root")]
    public string? Root { get; set; }

    [JsonPropertyName("target")]
    public string? Target { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("manages")]
    public List<string>? Manages { get; set; }

    [JsonPropertyName("steps")]
    public List<string>? Steps { get; set; }

    [JsonPropertyName("uninstall")]
    public List<string>? Uninstall { get; set; }
}
