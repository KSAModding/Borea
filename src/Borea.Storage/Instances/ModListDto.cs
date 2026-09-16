using Tomlyn.Serialization;

namespace Borea.Storage.Instances;

public sealed class ModListDto
{
    [TomlPropertyName("format")]
    public int? Format { get; set; }

    [TomlPropertyName("name")]
    public string? Name { get; set; }

    [TomlPropertyName("mods")]
    public List<ModListEntryDto> Mods { get; set; } = new();
}

public sealed class ModListEntryDto
{
    [TomlPropertyName("id")]
    public string? Id { get; set; }

    [TomlPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>Absent means enabled, the way the game reads a manifest entry without the key.</summary>
    [TomlPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}
