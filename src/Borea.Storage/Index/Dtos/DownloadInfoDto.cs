using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

public sealed class DownloadInfoDto
{
    [JsonPropertyName("url")]
    public required string URL { get; set; }

    [JsonPropertyName("sha256")]
    public required string SHA256 { get; set; }

    [JsonPropertyName("size")]
    public required long Size { get; set; }

    [JsonPropertyName("content_type")]
    public required string ContentType { get; set; }

    [JsonPropertyName("mirrors")]
    public List<string>? Mirrors { get; set; }
}
