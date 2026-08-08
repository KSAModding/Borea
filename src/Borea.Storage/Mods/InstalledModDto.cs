namespace Borea.Storage.Mods;

/// <summary>
/// TOML-serializable representation of an InstalledMod.
/// </summary>
public sealed class InstalledModDto
{
    public string ModId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;

    /// <summary>InstallReason as a string - "Manual", "ModPack", or "Dependency".</summary>
    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset InstalledAt { get; set; }
    public string? Checksum { get; set; }
    public ModMetadataDto Metadata { get; set; } = new();

    /// <summary>The dependency list of the installed release, as its release file stamped it.</summary>
    public List<ModDependencyDto> Dependencies { get; set; } = new();
}
