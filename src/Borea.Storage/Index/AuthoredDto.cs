using System.Text.Json;
using System.Text.Json.Serialization;

namespace Borea.Storage.Index;

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

    // Fails silently as a broken [releases] shouldn't break the whole Authored section
    [JsonPropertyName("releases")]
    public JsonElement? Releases { get; set; }

    // Fails loudly if a loader is declared but can't be parsed
    [JsonPropertyName("loader")]
    public LoaderDto? Loader { get; set; }

    // Fails loudly if dependencies are declared but can't be parsed
    [JsonPropertyName("dependencies")]
    public List<DependencyEntryDto>? Dependencies { get; set; }
}

