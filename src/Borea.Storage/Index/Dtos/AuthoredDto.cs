using System.Text.Json;
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

    /// <summary>Optional on a vehicle or save only, which ModMetadata checks.</summary>
    [JsonPropertyName("compatibility")]
    public CompatibilityDto? Compatibility { get; set; }

    /// <summary>Optional on a vehicle or save only, which ModMetadata checks.</summary>
    [JsonPropertyName("links")]
    public LinksDto? Links { get; set; }

    // Fails loudly if releases is declared but can't be parsed
    [JsonPropertyName("releases")]
    public ReleasesInfoDto? Releases { get; set; }

    // Fails loudly if a loader is declared but can't be parsed
    [JsonPropertyName("loader")]
    public LoaderDto? Loader { get; set; }

    // Fails loudly if dependencies are declared but can't be parsed
    [JsonPropertyName("dependencies")]
    public List<DependencyEntryDto>? Dependencies { get; set; }

    [JsonPropertyName("install")]
    public InstallDescriptorDto? Install { get; set; }

    [JsonPropertyName("provides")]
    public ProvidesDto? Provides { get; set; }
}

