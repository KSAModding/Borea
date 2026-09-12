using System.Text.Json;
using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

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

    [JsonPropertyName("release_status")]
    public required string ReleaseStatus { get; set; }

    [JsonPropertyName("release_date")]
    public required string ReleaseDate { get; set; }

    [JsonPropertyName("game_min")]
    public required string GameMin { get; set; }

    [JsonPropertyName("game_min_revision")]
    public required int GameMinRevision { get; set; }

    [JsonPropertyName("os")]
    public List<string>? Os { get; set; }

    [JsonPropertyName("game_max")]
    public string? GameMax { get; set; }

    [JsonPropertyName("game_max_revision")]
    public int? GameMaxRevision { get; set; }

    [JsonPropertyName("download")]
    public required DownloadInfoDto Download { get; set; }

    [JsonPropertyName("install_size")]
    public required long InstallSize { get; set; }

    [JsonPropertyName("install")]
    public InstallInfoDto? Install { get; set; }

    [JsonPropertyName("loader")]
    public LoaderDto? Loader { get; set; }

    [JsonPropertyName("dependencies")]
    public required List<DependencyEntryDto> Dependencies { get; set; }

    [JsonPropertyName("changelog")]
    public string? Changelog { get; set; }

    // Fails quietly as an incomplete listing in a release
    // will cause a fallback to the authored listing
    [JsonPropertyName("listing")]
    public JsonElement? Listing { get; set; }

    [JsonPropertyName("yanked")]
    public bool? Yanked { get; set; }

    [JsonPropertyName("yanked_reason")]
    public string? YankedReason { get; set; }
}
