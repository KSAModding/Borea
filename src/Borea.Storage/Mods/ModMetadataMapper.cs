using Borea.Core.Mods;

namespace Borea.Storage.Mods;

public static class ModMetadataMapper
{
    public static ModMetadataDto ToDto(ModMetadata metadata) => new()
    {
        ModId = metadata.ModId,
        Name = metadata.Name,
        Author = metadata.Author,
        Version = metadata.Version.ToString(),
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
        dto.Name,
        dto.Author,
        ModVersion.Parse(dto.Version),
        dto.Description,
        dto.ReleasedAt,
        dto.FileSizeBytes,
        dto.Dependencies.Select(ModDependencyMapper.FromDto).ToList(),
        dto.Tags,
        dto.HomepageUrl,
        dto.ChangeLog);
}