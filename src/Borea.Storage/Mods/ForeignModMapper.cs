using Borea.Core.Mods;

namespace Borea.Storage.Mods;

public static class ForeignModMapper
{
    public static ForeignModDto ToDto(ForeignMod mod) => new()
    {
        FolderName = mod.FolderName,
        Dependencies = mod.Dependencies.Select(dependency => new LocalModDependencyDto
        {
            ModId = dependency.ModId,
            Optional = dependency.Optional,
        }).ToList(),
        DependencyReadError = mod.DependencyReadError,
    };

    public static ForeignMod FromDto(ForeignModDto dto) => new(
        dto.FolderName,
        dto.Dependencies.Select(dependency => new LocalModDependency(dependency.ModId, dependency.Optional)).ToList(),
        dto.DependencyReadError);
}
