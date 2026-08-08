using Borea.Core.Mods;

namespace Borea.Storage.Mods;

public static class DownloadInfoMapper
{
    public static DownloadInfoDto ToDto(DownloadInfo download) => new()
    {
        Url = download.Url,
        Sha256 = download.Sha256,
        SizeBytes = download.SizeBytes,
        ContentType = download.ContentType,
        Mirrors = download.Mirrors.ToList(),
    };

    public static DownloadInfo FromDto(DownloadInfoDto dto) => new(
        dto.Url,
        dto.Sha256,
        dto.SizeBytes,
        dto.ContentType,
        dto.Mirrors);
}
