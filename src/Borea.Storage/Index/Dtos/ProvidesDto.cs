using System.Text.Json;
using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

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

    /// <summary>
    /// How the mod-loader is told which instance to run
    /// </summary>
    [JsonPropertyName("instance")]
    public InstanceDto? Instance { get; set; }

    /// <summary>
    /// What the mod-loader starts per platform. Values stay raw, so a platform
    /// name Borea does not know is never read.
    /// </summary>
    [JsonPropertyName("platform")]
    public Dictionary<string, JsonElement>? Platform { get; set; }
}
