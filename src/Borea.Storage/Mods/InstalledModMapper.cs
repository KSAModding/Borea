using Borea.Core.Mods;

namespace Borea.Storage.Mods;

public static class InstalledModMapper
{
    public static InstalledModDto ToDto(InstalledMod mod) => new()
    {
        ModId = mod.ModId,
        Version = mod.Version.ToString(),
        Reason = mod.Reason.ToString(),
        InstalledAt = mod.InstalledAt,
        Checksum = mod.Checksum,
        Metadata = ModMetadataMapper.ToDto(mod.Metadata),
        Dependencies = mod.Dependencies.Select(ModDependencyMapper.ToDto).ToList(),
    };

    public static InstalledMod FromDto(InstalledModDto dto) => new(
        dto.ModId,
        ModVersion.Parse(dto.Version),
        Enum.Parse<InstallReason>(dto.Reason),
        dto.InstalledAt,
        ModMetadataMapper.FromDto(dto.Metadata),
        dto.Dependencies.Select(ModDependencyMapper.FromDto).ToList(),
        dto.Checksum);
}
