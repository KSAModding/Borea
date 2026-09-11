using System.Text.Json.Serialization;

namespace Borea.Storage.Index;

public sealed class ProvidesDto
{
    [JsonPropertyName("launch")]
    public string? Launch { get; set; }

    /// <summary>
    /// Anchor from where ContentPath goes from
    /// </summary>
    [JsonPropertyName("content-dir")]
    public string? ContentDirectory { get; set; }

    [JsonPropertyName("content-path")]
    public string? ContentPath { get; set; }

    /// <summary>
    /// Info needed to configure the mod-loader
    /// </summary>
    [JsonPropertyName("configure")]
    public ConfigureDto? Configure { get; set; }
}
