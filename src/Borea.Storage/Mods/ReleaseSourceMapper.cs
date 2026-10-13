using Borea.Core.Mods;

namespace Borea.Storage.Mods;

public static class ReleaseSourceMapper
{
    public static ReleaseSourceDto ToDto(ReleaseSource source) => new()
    {
        Hosts = source.Hosts.Select(h => new ReleaseHostDto { Host = h.Host, Reference = h.Reference }).ToList(),
        Authority = source.Authority,
        Since = source.Since?.ToString(),
        TagPrefix = source.TagPrefix,
    };

    public static ReleaseSource FromDto(ReleaseSourceDto dto) => new(
        dto.Hosts.Select(h => new ReleaseHost(h.Host, h.Reference)).ToList(),
        dto.Authority,
        MetadataEnumMapper.ParseVersion(dto.Since),
        dto.TagPrefix);
}
