using Borea.Core.Mods;

namespace Borea.Storage.Mods;

public static class InstallDescriptorMapper
{
    public static InstallDescriptorDto ToDto(InstallDescriptor install) => new()
    {
        Root = install.Root,
        Target = install.Target is { } target ? MetadataEnumMapper.ToDto(target) : null,
        Path = install.Path,
        Manages = install.Manages?.ToList(),
        Steps = install.Steps?.ToList(),
        Uninstall = install.Uninstall?.ToList(),
    };

    public static InstallDescriptor FromDto(InstallDescriptorDto dto) => new(
        dto.Root,
        dto.Target is null ? null : MetadataEnumMapper.ParseAnchor(dto.Target),
        dto.Path,
        dto.Manages,
        dto.Steps,
        dto.Uninstall);
}
