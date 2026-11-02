using System.Text.Json.Serialization;

namespace Borea.Storage.Index.Dtos;

public sealed class PlatformDownloadDto
{
    [JsonPropertyName("url")]
    public required string URL { get; set; }

    [JsonPropertyName("sha256")]
    public required string SHA256 { get; set; }

    [JsonPropertyName("size")]
    public required long Size { get; set; }

    [JsonPropertyName("content_type")]
    public required string ContentType { get; set; }

    [JsonPropertyName("install_size")]
    public required long InstallSize { get; set; }

    [JsonPropertyName("unavailable_since")]
    public string? UnavailableSince { get; set; }
}
