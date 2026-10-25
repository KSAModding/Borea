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
    public bool IsFavorite { get; set; }
    public DateTimeOffset? LastPlayedAt { get; set; }
    public List<string> LaunchArguments { get; set; } = new();

    /// <summary>The kept game build as a GameVersion string. Absent when the instance runs on the game folder of the settings, so such a record does not change.</summary>
    public string? GameBuild { get; set; }

    /// <summary>"ModPack" or "Custom" discriminator for InstanceSource.</summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>Only populated when SourceType is "ModPack".</summary>
    public string? SourceModPackId { get; set; }

    /// <summary>Only populated when SourceType is "ModPack". ModVersion as a string.</summary>
    public string? SourceModPackVersion { get; set; }

    /// <summary>The pack mods the player detached. Absent when there are none, so such a record does not change.</summary>
    public List<string>? SourceModPackDetached { get; set; }

    public List<InstalledModDto> Mods { get; set; } = new();
    public List<ForeignModDto> ForeignMods { get; set; } = new();
}
