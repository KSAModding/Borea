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
        Ownership = mod.Ownership.ToString(),
        OwnershipToken = mod.OwnershipToken,
        Metadata = ModVersionMetadataMapper.ToDto(mod.Metadata),
    };

    public static InstalledMod FromDto(InstalledModDto dto) => new(
        dto.ModId,
        ModVersion.Parse(dto.Version),
        Enum.Parse<InstallReason>(dto.Reason),
        dto.InstalledAt,
        ModVersionMetadataMapper.FromDto(dto.Metadata),
        dto.Checksum,
        string.IsNullOrWhiteSpace(dto.Ownership)
            ? ModInstallOwnership.Borea
            : Enum.Parse<ModInstallOwnership>(dto.Ownership),
        dto.OwnershipToken);
}
