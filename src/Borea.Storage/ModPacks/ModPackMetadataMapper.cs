using Borea.Core.Game;
using Borea.Core.ModPacks;
using Borea.Core.Mods;

namespace Borea.Storage.ModPacks;

public static class ModPackMetadataMapper
{
    public static ModPackMetadataDto ToDto(ModPackMetadata modPack) => new()
    {
        ModPackId = modPack.ModPackId,
        Source = modPack.Source,
        Name = modPack.Name,
        Author = modPack.Author,
        Version = modPack.Version.ToString(),
        BuiltForGameVersion = modPack.BuiltForGameVersion.ToString(),
        Description = modPack.Description,
        HomepageUrl = modPack.HomepageUrl,
        ReleasedAt = modPack.ReleasedAt,
        Mods = modPack.Mods.Select(ToDto).ToList(),
    };

    public static ModPackMetadata FromDto(ModPackMetadataDto dto) => new(
        dto.ModPackId,
        dto.Source,
        dto.Name,
        dto.Author,
        ModVersion.Parse(dto.Version),
        GameVersion.Parse(dto.BuiltForGameVersion),
        dto.Description,
        dto.ReleasedAt,
        dto.Mods.Select(FromDto).ToList(),
        dto.HomepageUrl);

    private static ModPackEntryDto ToDto(ModPackEntry entry) => new()
    {
        ContentId = entry.ContentId,
        Version = entry.Version.ToString(),
    };

    private static ModPackEntry FromDto(ModPackEntryDto dto) => new(
        dto.ContentId,
        ModVersion.Parse(dto.Version));
}
