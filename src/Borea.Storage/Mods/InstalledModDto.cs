namespace Borea.Storage.Mods;

/// <summary>
/// Flat, TOML-serializable representation of an InstalledMod.
/// </summary>
public sealed class InstalledModDto
{
    public string ModId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;

    /// <summary>InstallReason as a string — "Manual", "ModPack", or "Dependency".</summary>
    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset InstalledAt { get; set; }
    public string? Checksum { get; set; }
    public ModMetadataDto Metadata { get; set; } = new();
}
