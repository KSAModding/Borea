using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

public sealed class AuthoredDto
{
    [JsonPropertyName("spec_version")]
    public required int SpecVersion { get; set; }

    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("authors")]
    public required List<string> Authors { get; set; }

    [JsonPropertyName("abstract")]
    public required string Abstract { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("license")]
    public required string License { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("superseded_by")]
    public string? SupersededBy { get; set; }

    [JsonPropertyName("compatibility")]
    public required CompatibilityDto Compatibility { get; set; }

    [JsonPropertyName("links")]
    public required LinksDto Links { get; set; }

    [JsonPropertyName("releases")]
    public ReleasesInfoDto? Releases { get; set; }

    [JsonPropertyName("loader")]
    public LoaderDto? Loader { get; set; }

    [JsonPropertyName("dependencies")]
    public List<DependencyEntryDto>? Dependencies { get; set; }

    [JsonPropertyName("install")]
    public InstallDescriptorDto? Install { get; set; }

    [JsonPropertyName("provides")]
    public ProvidesDto? Provides { get; set; }
}

