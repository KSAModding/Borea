namespace Borea.Storage.Mods;

/// <summary>
/// Flat, TOML-serializable representation of one any_of alternative.
/// </summary>
public sealed class ModDependencyAlternativeDto
{
    public string ModId { get; set; } = string.Empty;
    public string? MinVersion { get; set; }
    public string? MaxVersion { get; set; }
}
