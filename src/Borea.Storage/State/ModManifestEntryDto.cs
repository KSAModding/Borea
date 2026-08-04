using System.Text.Json.Serialization;

namespace Borea.Storage.State;

/// <summary>
/// A single [[mods]] entry in StarMap's manifest.toml.
/// </summary>
public sealed class ModManifestEntryDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}
