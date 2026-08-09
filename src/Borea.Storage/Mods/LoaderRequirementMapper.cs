using Borea.Core.ModLoaders;
using Borea.Core.Mods;

namespace Borea.Storage.Mods;

public static class LoaderRequirementMapper
{
    public static LoaderRequirementDto ToDto(LoaderRequirement loader) => new()
    {
        LoaderId = loader.LoaderId,
        MinVersion = loader.MinVersion.ToString(),
        MaxVersion = loader.MaxVersion?.ToString(),
        Source = MetadataEnumMapper.ToDto(loader.Source),
    };

    public static LoaderRequirement FromDto(LoaderRequirementDto dto) => new(
        dto.LoaderId,
        ModVersion.Parse(dto.MinVersion),
        MetadataEnumMapper.ParseVersion(dto.MaxVersion),
        MetadataEnumMapper.ParseSource(dto.Source));
}
