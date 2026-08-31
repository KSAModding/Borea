namespace Borea.Storage.Mods;

/// <summary>
/// TOML-serializable representation of the authored ModMetadata.
/// Absent optional fields stay null so they round-trip as absent.
/// Mirrors Borea's local persistence, not the RFC 0031 wire format; the index
/// reader gets its own DTOs.
/// </summary>
public sealed class ModMetadataDto
{
    public int SpecVersion { get; set; }
    public string ModId { get; set; } = string.Empty;

    /// <summary>The content type, lowercase ("mod", "mod-loader").</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Which source this metadata came from. Borea-internal, not a format field.</summary>
    public string Source { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public List<string> Authors { get; set; } = new();
    public string Abstract { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string License { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();

    /// <summary>The author's listing status, lowercase ("active", "deprecated"). Absent means active.</summary>
    public string? Status { get; set; }

    public string? SupersededBy { get; set; }
    public Dictionary<string, string> Links { get; set; } = new();
    public ReleaseSourceDto? Releases { get; set; }
    public string GameMin { get; set; } = string.Empty;
    public string? GameMax { get; set; }

    /// <summary>Null means no known platform restriction.</summary>
    public List<string>? Os { get; set; }

    public LoaderRequirementDto? Loader { get; set; }
    public List<ModDependencyDto> Dependencies { get; set; } = new();
    public InstallDescriptorDto? Install { get; set; }

    public LoaderProvidesDto? Provides { get; set; }
}
