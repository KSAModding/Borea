using Borea.Core.Mods;

namespace Borea.Storage.Mods;

public static class InstallInfoMapper
{
    public static InstallInfoDto ToDto(InstallInfo install) => new()
    {
        Root = install.Root,
        Derived = install.Derived,
        Target = install.Target is { } target ? MetadataEnumMapper.ToDto(target) : null,
        Path = install.Path,
    };

    public static InstallInfo FromDto(InstallInfoDto dto) => new(
        dto.Root,
        dto.Derived,
        dto.Target is null ? null : MetadataEnumMapper.ParseAnchor(dto.Target),
        dto.Path);
}
