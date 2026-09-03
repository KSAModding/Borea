namespace Borea.Storage.ModPacks;

/// <summary>
/// TOML-serializable representation of one ModPackMetadata version.
/// </summary>
public sealed class ModPackMetadataDto
{
    public int SpecVersion { get; set; }
    public string ModPackId { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public List<string> Authors { get; set; } = new();
    public string Abstract { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string License { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();

    /// <summary>Absent means active.</summary>
    public string? Status { get; set; }

    public string? SupersededBy { get; set; }
    public Dictionary<string, string> Links { get; set; } = new();
    public string GameMin { get; set; } = string.Empty;
    public string? GameMax { get; set; }

    public List<string>? Os { get; set; }

    public string Version { get; set; } = string.Empty;
    public DateTimeOffset ReleasedAt { get; set; }
    public string? Changelog { get; set; }

    public List<ModPackEntryDto> Mods { get; set; } = new();
    public List<ModPackEntryDto> Vehicles { get; set; } = new();
    public List<ModPackEntryDto> Saves { get; set; } = new();
}
