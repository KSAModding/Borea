using System.Text.Json.Serialization;

namespace Borea.Storage.Index;

public sealed class DownloadInfoDto
{
    [JsonPropertyName("url")]
    public required string URL { get; set; }

    [JsonPropertyName("sha256")]
    public required string SHA256 { get; set; }

    [JsonPropertyName("size")]
    public required int Size { get; set; }

    [JsonPropertyName("content_type")]
    public required string ContentType { get; set; }
}
