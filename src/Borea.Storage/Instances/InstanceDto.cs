using Borea.Storage.Mods;

namespace Borea.Storage.Instances;

/// <summary>
/// Flat, TOML-serializable representation of an Instance.
/// </summary>
public sealed class InstanceDto
{
    public string InstanceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public bool Favorite { get; set; }

    /// <summary>"ModPack" or "Custom" — discriminator for InstanceSource.</summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>Only populated when SourceType is "ModPack".</summary>
    public string? SourceModPackId { get; set; }

    /// <summary>Only populated when SourceType is "ModPack". ModVersion as a string.</summary>
    public string? SourceModPackVersion { get; set; }

    public List<InstalledModDto> Mods { get; set; } = new();
}