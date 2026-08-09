namespace Borea.Storage.Mods;

/// <summary>
/// Flat, TOML-serializable representation of a DownloadInfo.
/// </summary>
public sealed class DownloadInfoDto
{
    public string Url { get; set; } = string.Empty;
    public string? Sha256 { get; set; }
    public long? SizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public List<string> Mirrors { get; set; } = new();
}
