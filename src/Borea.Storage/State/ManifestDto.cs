using Tomlyn.Serialization;

namespace Borea.Storage.State;

/// <summary>
/// Root of the game's manifest.toml. The key is stated, not derived since Tomlyn maps
/// a property to its own name, and [[Mods]] is a manifest the game reads as empty.
/// </summary>
public sealed class ManifestDto
{
    [TomlPropertyName("mods")]
    public List<ModManifestEntryDto> Mods { get; set; } = new();
}
