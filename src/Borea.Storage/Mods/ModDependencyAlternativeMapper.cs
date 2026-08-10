using Borea.Core.Dependencies;

namespace Borea.Storage.Mods;

public static class ModDependencyAlternativeMapper
{
    public static ModDependencyAlternativeDto ToDto(ModDependencyAlternative alternative) => new()
    {
        ModId = alternative.ModId,
        MinVersion = alternative.MinVersion?.ToString(),
        MaxVersion = alternative.MaxVersion?.ToString(),
    };

    public static ModDependencyAlternative FromDto(ModDependencyAlternativeDto dto) => new(
        dto.ModId,
        MetadataEnumMapper.ParseVersion(dto.MinVersion),
        MetadataEnumMapper.ParseVersion(dto.MaxVersion));
}
