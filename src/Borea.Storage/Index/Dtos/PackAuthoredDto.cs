using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

public sealed class PackAuthoredDto
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

    // Assumed Mod Packs will also have status
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    // Assumed can be superseded
    [JsonPropertyName("superseded_by")]
    public string? SupersededBy { get; set; }

    [JsonPropertyName("version")]
    public required string Version { get; set; }

    [JsonPropertyName("released_at")]
    public required string ReleasedAt { get; set; }

    [JsonPropertyName("changelog")]
    public string? ChangeLog { get; set; }

    [JsonPropertyName("links")]
    public required LinksDto Links { get; set; }

    [JsonPropertyName("compatibility")]
    public required CompatibilityDto Compatibility { get; set; }

    // Currently [Mods], [Saves], and [Vehicles] all just declare Id and Version
    // for each entry so they can use the same type in the list

    [JsonPropertyName("mods")]
    public required List<IndexModPackItemEntryDto> Mods { get; set; }

    [JsonPropertyName("vehicles")]
    public List<IndexModPackItemEntryDto>? Vehicles { get; set; }

    [JsonPropertyName("saves")]
    public List<IndexModPackItemEntryDto>? Saves { get; set; }

    [JsonPropertyName("index_status")]
    public IndexStatusDto? IndexStatus { get; set; }
}
