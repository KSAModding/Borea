using System.Text.Json.Serialization;

namespace Borea.Storage.State;

/// <summary>
/// Root of StarMap's manifest.toml — a single [[mods]] array of entries.
/// </summary>
public sealed class ManifestDto
{
    [JsonPropertyName("mods")]
    public List<ModManifestEntryDto> Mods { get; set; } = new();
}