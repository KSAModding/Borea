using System.Text.Json.Serialization;

namespace Borea.Storage.Index;

public sealed class ReleasesEntryDto
{
    [JsonPropertyName("spec_version")]
    public required int SpecVersion { get; set; }

    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [JsonPropertyName("version")]
    public required string Version { get; set; }

    [JsonPropertyName("version_scheme")]
    public required string VersionScheme { get; set; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("game_min")]
    public required string GameMin { get; set; }

    [JsonPropertyName("game_min_revision")]
    public int? GameMinRevision { get; set; }

    [JsonPropertyName("game_max")]
    public string? GameMax { get; set; }

    [JsonPropertyName("game_max_revision")]
    public int? GameMaxRevision { get; set; }

    [JsonPropertyName("download")]
    public DownloadInfoDto? Download { get; set; }

    [JsonPropertyName("install_size")]
    public int? InstallSize { get; set; }

    [JsonPropertyName("install")]
    public InstallInfoDto? Install { get; set; }

    [JsonPropertyName("loeader")]
    public LoaderDto? Loader { get; set; }

    [JsonPropertyName("dependencies")]
    public List<DependencyEntryDto>? Dependencies { get; set; }

    [JsonPropertyName("changelog")]
    public string? Changelog { get; set; }

    [JsonPropertyName("listing")]
    public required ReleaseListingDto Listing { get; set; }

    [JsonPropertyName("yanked")]
    public bool? Yanked { get; set; }

    [JsonPropertyName("yanked_reason")]
    public string? YankedReason { get; set; }
}
