using Borea.Core.Mods;

namespace Borea.Storage.Mods;

public static class ModMetadataMapper
{
    public static ModMetadataDto ToDto(ModMetadata metadata) => new()
    {
        SpecVersion = metadata.SpecVersion,
        ModId = metadata.ModId,
        Type = MetadataEnumMapper.ToDto(metadata.Type),
        Source = metadata.Source,
        Name = metadata.Name,
        Authors = metadata.Authors.ToList(),
        Abstract = metadata.Abstract,
        Description = metadata.Description,
        License = metadata.License,
        Tags = metadata.Tags.ToList(),
        Status = MetadataEnumMapper.ToDto(metadata.Status),
        SupersededBy = metadata.SupersededBy,
        Links = metadata.Links.ToDictionary(p => p.Key, p => p.Value),
        Releases = metadata.Releases is null ? null : ReleaseSourceMapper.ToDto(metadata.Releases),
        GameMin = metadata.GameMin,
        GameMax = metadata.GameMax,
        Os = metadata.Os?.ToList(),
        Loader = metadata.Loader is null ? null : LoaderRequirementMapper.ToDto(metadata.Loader),
        Dependencies = metadata.Dependencies.Select(ModDependencyMapper.ToDto).ToList(),
        InstallRootOverride = metadata.InstallRootOverride,
    };

    public static ModMetadata FromDto(ModMetadataDto dto)
    {
        if (dto.SpecVersion == 0)
            throw new FormatException("The metadata file predates the metadata model (no spec version).");

        return new ModMetadata(
        specVersion: dto.SpecVersion,
        modId: dto.ModId,
        source: dto.Source,
        name: dto.Name,
        authors: dto.Authors,
        abstractText: dto.Abstract,
        license: dto.License,
        links: dto.Links,
        gameMin: dto.GameMin,
        type: MetadataEnumMapper.ParseContentType(dto.Type),
        tags: dto.Tags,
        description: dto.Description,
        status: dto.Status is null ? ModStatus.Active : MetadataEnumMapper.ParseModStatus(dto.Status),
        supersededBy: dto.SupersededBy,
        releases: dto.Releases is null ? null : ReleaseSourceMapper.FromDto(dto.Releases),
        gameMax: dto.GameMax,
        os: dto.Os,
        loader: dto.Loader is null ? null : LoaderRequirementMapper.FromDto(dto.Loader),
        dependencies: dto.Dependencies.Select(ModDependencyMapper.FromDto).ToList(),
        installRootOverride: dto.InstallRootOverride);
    }
}
