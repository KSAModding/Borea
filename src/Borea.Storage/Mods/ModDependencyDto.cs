namespace Borea.Storage.Mods;

/// <summary>
/// TOML-serializable representation of a ModDependency.
/// </summary>
public sealed class ModDependencyDto
{
    /// <summary>Null when this is an any_of entry.</summary>
    public string? ModId { get; set; }

    /// <summary>The dependency kind, lowercase ("required", "conflict", ...).</summary>
    public string Kind { get; set; } = string.Empty;

    public string? MinVersion { get; set; }
    public string? MaxVersion { get; set; }

    /// <summary>"authored" or "derived" in release data, null in authored metadata.</summary>
    public string? Source { get; set; }

    /// <summary>The alternatives of an any_of entry, null for a single-id entry.</summary>
    public List<ModDependencyAlternativeDto>? AnyOf { get; set; }
}
