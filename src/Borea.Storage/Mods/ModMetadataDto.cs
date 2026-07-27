namespace Borea.Storage.Mods;

/// <summary>
/// Flat, TOML-serializable representation of a ModMetadata snapshot.
/// </summary>
public sealed class ModMetadataDto
{
    public string ModId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? HomepageUrl { get; set; }
    public string? ChangeLog { get; set; }
    public System.DateTimeOffset ReleasedAt { get; set; }
    public long FileSizeBytes { get; set; }
    public List<ModDependencyDto> Dependencies { get; set; } = new();
    public List<string> Tags { get; set; } = new();
}