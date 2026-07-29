using Borea.Core.Mods;
using Borea.Core.Game;

namespace Borea.Storage.Mods;

public static class ModMetadataMapper
{
    public static ModMetadataDto ToDto(ModMetadata metadata) => new()
    {
        ModId = metadata.ModId,
        Source = metadata.Source,
        Name = metadata.Name,
        Author = metadata.Author,
        Version = metadata.Version.ToString(),
        BuiltForGameVersion = metadata.BuiltForGameVersion.ToString(),
        Description = metadata.Description,
        HomepageUrl = metadata.HomepageUrl,
        ChangeLog = metadata.ChangeLog,
        ReleasedAt = metadata.ReleasedAt,
        FileSizeBytes = metadata.FileSizeBytes,
        Dependencies = metadata.Dependencies.Select(ModDependencyMapper.ToDto).ToList(),
        Tags = metadata.Tags.ToList(),
    };

    public static ModMetadata FromDto(ModMetadataDto dto) => new(
        dto.ModId,
        dto.Source,
        dto.Name,
        dto.Author,
        ModVersion.Parse(dto.Version),
        GameVersion.Parse(dto.BuiltForGameVersion),
        dto.Description,
        dto.ReleasedAt,
        dto.FileSizeBytes,
        dto.Dependencies.Select(ModDependencyMapper.FromDto).ToList(),
        dto.Tags,
        dto.HomepageUrl,
        dto.ChangeLog);
}