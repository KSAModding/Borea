using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Storage.Mods;

namespace Borea.Storage.ModPacks;

public static class ModPackMetadataMapper
{
    public static ModPackMetadataDto ToDto(ModPackMetadata modPack) => new()
    {
        SpecVersion = modPack.SpecVersion,
        ModPackId = modPack.ModPackId,
        Type = MetadataEnumMapper.ToDto(modPack.Type),
        Source = modPack.Source,
        Name = modPack.Name,
        Authors = modPack.Authors.ToList(),
        Abstract = modPack.Abstract,
        Description = modPack.Description,
        License = modPack.License,
        Tags = modPack.Tags.ToList(),
        Status = MetadataEnumMapper.ToDto(modPack.Status),
        SupersededBy = modPack.SupersededBy,
        Links = modPack.Links.ToDictionary(p => p.Key, p => p.Value),
        GameMin = modPack.GameMin,
        GameMax = modPack.GameMax,
        Os = modPack.Os?.ToList(),
        Version = modPack.Version.ToString(),
        ReleasedAt = modPack.ReleasedAt,
        Changelog = modPack.Changelog,
        Mods = modPack.Mods.Select(ToDto).ToList(),
        Vehicles = modPack.Vehicles.Select(ToDto).ToList(),
        Saves = modPack.Saves.Select(ToDto).ToList(),
    };

    public static ModPackMetadata FromDto(ModPackMetadataDto dto)
    {
        if (dto.SpecVersion < 1)
            throw new FormatException(dto.SpecVersion == 0
                ? "The pack file predates the metadata model (no spec version)."
                : $"The pack file carries an invalid spec version ({dto.SpecVersion}).");

        var type = MetadataEnumMapper.ParseContentType(dto.Type);
        if (type != ContentType.ModPack)
            throw new FormatException($"The pack file declares type '{dto.Type}' and not a pack.");

        return new ModPackMetadata(
            specVersion: dto.SpecVersion,
            modPackId: dto.ModPackId,
            source: dto.Source,
            name: dto.Name,
            authors: dto.Authors,
            abstractText: dto.Abstract,
            license: dto.License,
            links: dto.Links,
            gameMin: dto.GameMin,
            version: ModVersion.Parse(dto.Version),
            releasedAt: dto.ReleasedAt,
            mods: dto.Mods.Select(FromDto).ToList(),
            tags: dto.Tags,
            description: dto.Description,
            status: dto.Status is null ? ModStatus.Active : MetadataEnumMapper.ParseModStatus(dto.Status),
            supersededBy: dto.SupersededBy,
            gameMax: dto.GameMax,
            os: dto.Os,
            changelog: dto.Changelog,
            vehicles: dto.Vehicles.Select(FromDto).ToList(),
            saves: dto.Saves.Select(FromDto).ToList());
    }

    private static ModPackEntryDto ToDto(ModPackEntry entry) => new()
    {
        ContentId = entry.ContentId,
        Version = entry.Version.ToString(),
    };

    private static ModPackEntry FromDto(ModPackEntryDto dto) => new(
        dto.ContentId,
        ModVersion.Parse(dto.Version));
}
