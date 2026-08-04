namespace Borea.Storage.Mods;

/// <summary>
/// Flat, TOML-serializable representation of a ModDependency.
/// </summary>
public sealed class ModDependencyDto
{
    public string ModId { get; set; } = string.Empty;
    public string VersionRange { get; set; } = string.Empty;
    public bool IsOptional { get; set; }
}
