using Borea.Core.Dependencies;

namespace Borea.Storage.Mods;

public static class ModDependencyMapper
{
    public static ModDependencyDto ToDto(ModDependency dependency) => new()
    {
        ModId = dependency.ModId,
        VersionRange = dependency.RequiredVersion.ToString(),
        IsOptional = dependency.IsOptional,
    };

    public static ModDependency FromDto(ModDependencyDto dto) => new(
        dto.ModId,
        Core.Mods.VersionRange.Parse(dto.VersionRange),
        dto.IsOptional);
}