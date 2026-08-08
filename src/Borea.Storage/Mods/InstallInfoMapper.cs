using Borea.Core.Mods;

namespace Borea.Storage.Mods;

public static class InstallInfoMapper
{
    public static InstallInfoDto ToDto(InstallInfo install) => new()
    {
        Root = install.Root,
        Derived = install.Derived,
    };

    public static InstallInfo FromDto(InstallInfoDto dto) => new(dto.Root, dto.Derived);
}
