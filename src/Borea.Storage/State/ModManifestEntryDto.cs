using Tomlyn.Serialization;

namespace Borea.Storage.State;

/// <summary>
/// One [[mods]] entry. ModManifest.Save writes these two keys and destroys the
/// rest, so this is the whole schema.
/// </summary>
public sealed class ModManifestEntryDto
{
    [TomlPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Absent means enabled, the way ModEntry.Enabled's initializer leaves it.
    /// An entry the game creates for a new folder states false instead.
    /// </summary>
    [TomlPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}
