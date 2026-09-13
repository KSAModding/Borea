using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

public sealed class CuratedTagsDto
{
    [JsonPropertyName("spec_version")]
    public required int SpecVersion { get; set; }

    [JsonPropertyName("mod")]
    public List<CuratedTagDto> Mod { get; set; } = new();
}

public sealed class CuratedTagDto
{
    [JsonPropertyName("tag")]
    public required string Tag { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("meaning")]
    public required string Meaning { get; set; }

    [JsonPropertyName("forum_prefix")]
    public string? ForumPrefix { get; set; }
}
