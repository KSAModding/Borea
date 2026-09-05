using Borea.Core.ModLoaders;

namespace Borea.Storage.Mods;

public static class LoaderProvidesMapper
{
    public static LoaderProvidesDto ToDto(LoaderProvides provides) => new()
    {
        Launch = provides.Launch,
        ContentDir = provides.ContentDir is { } anchor ? MetadataEnumMapper.ToDto(anchor) : null,
        ContentPath = provides.ContentPath,
        Configure = provides.Configure is null ? null : ToDto(provides.Configure),
    };

    public static LoaderProvides FromDto(LoaderProvidesDto dto) => new(
        dto.Launch,
        dto.ContentDir is null ? null : MetadataEnumMapper.ParseAnchor(dto.ContentDir),
        dto.ContentPath,
        dto.Configure is null ? null : FromDto(dto.Configure));

    public static LoaderConfigureDto ToDto(LoaderConfigure configure) => new()
    {
        File = configure.File,
        Format = MetadataEnumMapper.ToDto(configure.Format),
        GamePath = configure.GamePath,
    };

    public static LoaderConfigure FromDto(LoaderConfigureDto dto) => new(
        dto.File,
        MetadataEnumMapper.ParseConfigureFormat(dto.Format),
        dto.GamePath);
}
