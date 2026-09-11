using System.Text.Json.Serialization;

namespace Borea.Storage.Index;

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

    [JsonPropertyName("manages")]
    public List<string>? Manages { get; set; }

    [JsonPropertyName("steps")]
    public List<string>? Steps { get; set; }

    [JsonPropertyName("uninstall")]
    public List<string>? Uninstall { get; set; }
}
