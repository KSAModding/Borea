namespace Borea.Storage.Mods;

/// <summary>
/// TOML-serializable representation of a ModVersionMetadata release.
/// Absent optional fields stay null so they round-trip as absent.
/// Mirrors Borea's local persistence, not the RFC 0031 wire format; the index
/// reader gets its own DTOs.
/// </summary>
public sealed class ModVersionMetadataDto
{
    public int SpecVersion { get; set; }
    public string ModId { get; set; } = string.Empty;

    /// <summary>The content type, lowercase ("mod", "mod-loader").</summary>
    public string Type { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    /// <summary>Defaults to "semver", the only value in spec version 1, so an absent key stays readable.</summary>
    public string VersionScheme { get; set; } = "semver";

    /// <summary>The release maturity, lowercase ("stable", "testing", "dev").</summary>
    public string ReleaseStatus { get; set; } = string.Empty;

    public DateTimeOffset ReleaseDate { get; set; }
    public string GameMin { get; set; } = string.Empty;
    public int GameMinRevision { get; set; }
    public string? GameMax { get; set; }
    public int? GameMaxRevision { get; set; }

    /// <summary>Null means no known platform restriction.</summary>
    public List<string>? Os { get; set; }

    public DownloadInfoDto Download { get; set; } = new();
    public long InstallSizeBytes { get; set; }
    public InstallInfoDto? Install { get; set; }
    public LoaderRequirementDto? Loader { get; set; }
    public List<ModDependencyDto> Dependencies { get; set; } = new();
    public string? Changelog { get; set; }
    public ListingSnapshotDto? Listing { get; set; }
    public bool Yanked { get; set; }
    public string? YankedReason { get; set; }
}
